using System.Diagnostics;
using System.Text;

namespace AMZExtractor;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Fail(ex.Message);
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (options.Input is null)
        {
            PrintUsage();
            return 2;
        }

        if (!File.Exists(options.Input))
        {
            Fail($"file not found: {options.Input}");
            return 1;
        }

        string input = Path.GetFullPath(options.Input);
        string outputRoot = options.OutputDirectory ?? DefaultOutputDirectory(input);

        try
        {
            using var archive = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 20, FileOptions.SequentialScan);

            AmzArchive amz = AmzArchive.Parse(archive);
            Coverage coverage = Coverage.Analyse(amz, archive.Length);

            Log($"archive: {input}");
            Log($"header table: 0x0 .. 0x{amz.HeaderEnd:X8} ({amz.HeaderEnd} bytes)");
            Log($"BASE_OFF: 0x{amz.BaseOffset:X8}");
            Log($"file size: 0x{archive.Length:X8} ({archive.Length} bytes)");
            Log($"entries: {amz.Entries.Count}");

            if (amz.Warning is not null)
                Fail(amz.Warning);

            if (options.ListOnly)
                List(amz);
            else
                ExtractEntries(archive, amz, outputRoot);

            Log(string.Empty);
            Report(coverage);

            return coverage.IsFullyCovered ? 0 : 1;
        }
        catch (Exception ex)
        {
            Fail($"{input}: {ex.Message}");
            return 1;
        }
    }

    private static void List(AmzArchive amz)
    {
        foreach (AmzEntry entry in amz.Entries)
        {
            string name = string.IsNullOrEmpty(entry.ArchivePath) ? entry.OutputName : entry.ArchivePath + "/" + entry.OutputName;
            Console.WriteLine($"{entry.Offset:X8} {entry.ZipSize,12} {entry.Size,12} {(entry.IsSplit ? "split" : "     ")} {name}");
        }
    }

    private static void ExtractEntries(Stream archive, AmzArchive amz, string outputRoot)
    {
        HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
        Stopwatch stopwatch = Stopwatch.StartNew();
        int done = 0;
        int failures = 0;
        long bytes = 0;

        Directory.CreateDirectory(outputRoot);

        for (int i = 0; i < amz.Entries.Count; i++)
        {
            AmzEntry entry = amz.Entries[i];
            done++;

            string path = OutputPath.Build(outputRoot, entry.ArchivePath, entry.OutputName);

            /*
                a non split entry always starts a fresh file
                2 entries mapping to the same output path would silently destroy each other in quickbms, so keep both
            */
            if (!entry.IsSplit && written.Contains(path))
            {
                string deduped = Deduplicate(path, written);
                Log($"duplicate name, writing \"{Path.GetFileName(deduped)}\" instead of \"{Path.GetFileName(path)}\"");
                path = deduped;
            }

            OutputPath.EnsureParent(path);

            try
            {
                MiniZipHeader header = MiniZip.ReadHeader(archive, entry.Offset, entry.Size, entry.ZipSize);

                using FileStream destination = new(path, entry.IsSplit && written.Contains(path) ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1 << 20);

                long produced = MiniZip.Write(archive, destination, header);
                bytes += produced;
                written.Add(path);

                Log($"[{done}/{amz.Entries.Count}] {(entry.IsSplit ? "appended" : "extracted"),-9} {produced,12}  {Relative(outputRoot, path)}");

                if (produced != entry.Size)
                    Fail($"  size mismatch: archive says {entry.Size}, got {produced}");

                if (!header.SizeRecoveredFromArchive && header.UncompressedSize != 0 && header.UncompressedSize != produced)
                    Fail($"  size mismatch: zip header says {header.UncompressedSize}, got {produced}");
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or EndOfStreamException or IOException)
            {
                failures++;
                Fail($"[{done}/{amz.Entries.Count}] failed  {Relative(outputRoot, path)}: {ex.Message}");
            }
        }

        stopwatch.Stop();
        Log($"{done} entries, {failures} failed, {bytes} bytes in {stopwatch.Elapsed.TotalSeconds:0.0}s");
    }

    /// <summary>
    /// The entries should tile the payload region exactly
    /// anything they do not claim is data the archive carries but its file table never names, so it cannot be extracted under a real name
    /// it's worth reporting rather than dumping into a filler file
    /// </summary>
    private static void Report(Coverage coverage)
    {
        Log($"payload: {coverage.PayloadBytes} bytes from 0x{coverage.PayloadStart:X8}");
        Log($"claimed: {coverage.ClaimedBytes} bytes across {coverage.SpanCount} spans");
        Log($"gaps: {coverage.Gaps.Count}");
        Log($"overlaps: {coverage.OverlapCount}");
        Log($"unmapped end: {coverage.TrailingBytes} bytes");

        foreach ((long offset, long length) in coverage.Gaps.Take(10))
            Log($"  unmapped   0x{offset:X8} .. 0x{offset + length - 1:X8} ({length} bytes)");

        if (coverage.Gaps.Count > 10)
            Log($"  ... and {coverage.Gaps.Count - 10} more gaps");

        Log(coverage.IsFullyCovered ? "fully covered, every byte after the header table belongs to an entry so nothing left over" : "not fully covered, the bytes listed above are in the archive but not described by the file table");
    }

    private static string Deduplicate(string path, HashSet<string> taken)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int n = 1; ; n++)
        {
            string candidate = Path.Combine(OutputPath.DirectoryName(path), $"{stem} ({n}){extension}");
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    private static string Relative(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(TrimLongPath(root), TrimLongPath(path));
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string TrimLongPath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

    private static string DefaultOutputDirectory(string input)
    {
        string? directory = Path.GetDirectoryName(input);
        return Path.Combine(directory ?? ".", Path.GetFileNameWithoutExtension(input) + "_extracted");
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"""
            {Path.GetFileName(Environment.ProcessPath) ?? "AMZExtractor"} - Order & Chaos Online (*.amz) extractor

            Usage:
                AMZExtractor <input.amz> [options]

            Options:
                -o, --out <dir>   Output directory (default: <input>_extracted)
                -l, --list        List entries only, write nothing
                -h, --help        Show this help
            """);
    }

    private static void Log(string message) => Console.WriteLine(message);

    private static void Fail(string message) => Console.Error.WriteLine(message);

    private sealed class Options
    {
        public string? Input { get; private init; }
        public string? OutputDirectory { get; private init; }
        public bool ListOnly { get; private init; }
        public bool ShowHelp { get; private init; }

        public static Options Parse(string[] args)
        {
            string? input = null;
            string? output = null;
            bool list = false;
            bool help = false;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "-h" or "--help":
                        help = true;
                        break;
                    case "-l" or "--list":
                        list = true;
                        break;
                    case "-o" or "--out":
                        if (++i >= args.Length)
                            throw new ArgumentException($"{arg} requires a directory");
                        output = args[i];
                        break;
                    default:
                        if (arg.Length > 1 && arg[0] == '-')
                            throw new ArgumentException($"unknown option: {arg}");
                        if (input is not null)
                            throw new ArgumentException("only one input file can be given");
                        input = arg;
                        break;
                }
            }

            return new Options
            {
                Input = input,
                OutputDirectory = output,
                ListOnly = list,
                ShowHelp = help,
            };
        }
    }
}