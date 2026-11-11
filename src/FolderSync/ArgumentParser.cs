using System.Globalization;

namespace FolderSync;

/// <summary>
/// Hand-rolled parser for the four required settings. There is no third-party command-line package.
/// </summary>
public static class ArgumentParser
{
    public static string Usage { get; } =
        """
        One-way folder synchronization.

        Usage:
          FolderSync --source <path> --replica <path> --interval <seconds> --log <path>

        Options:
          --source, -s <path>       Folder to copy from. Must already exist.
          --replica, -r <path>      Folder to update. Created if it is missing.
          --interval, -i <seconds>  How often to sync. A whole number of seconds, greater than 0.
          --log, -l <path>          Log file to append to. Parent folders are created.
          --help, -h                Show this help.

        Values may also be passed as --option=value.

        Example:
          FolderSync --source ./source --replica ./replica --interval 60 --log ./logs/sync.log

        After each sync the replica tree matches the source tree, including empty folders.
        Source and replica must not be the same folder and must not contain each other.
        The log file must sit outside both folders.
        """;

    public static ParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Any(static arg => arg is "--help" or "-h"))
            return ParseResult.Help();

        if (args.Length == 0)
            return ParseResult.Fail("No arguments were provided.");

        string? source = null;
        string? replica = null;
        string? intervalText = null;
        string? log = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (!TryReadOption(args, ref index, out var name, out var value, out var readError))
                return ParseResult.Fail(readError);

            switch (name)
            {
                case "source":
                    if (source is not null)
                        return ParseResult.Fail("Option --source was specified more than once.");
                    source = value;
                    break;
                case "replica":
                    if (replica is not null)
                        return ParseResult.Fail("Option --replica was specified more than once.");
                    replica = value;
                    break;
                case "interval":
                    if (intervalText is not null)
                        return ParseResult.Fail("Option --interval was specified more than once.");
                    intervalText = value;
                    break;
                case "log":
                    if (log is not null)
                        return ParseResult.Fail("Option --log was specified more than once.");
                    log = value;
                    break;
                default:
                    return ParseResult.Fail($"Unknown option '{args[index]}'. Run with --help to see usage.");
            }
        }

        var missing = new List<string>();
        if (source is null)
            missing.Add("--source");
        if (replica is null)
            missing.Add("--replica");
        if (intervalText is null)
            missing.Add("--interval");
        if (log is null)
            missing.Add("--log");

        if (source is null || replica is null || intervalText is null || log is null)
            return ParseResult.Fail("Missing required option(s): " + string.Join(", ", missing) + ".");

        if (!int.TryParse(intervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            return ParseResult.Fail("Interval must be a whole number of seconds greater than 0.");

        if (!TryNormalize(source, "--source", out var sourcePath, out var pathError)
            || !TryNormalize(replica, "--replica", out var replicaPath, out pathError)
            || !TryNormalize(log, "--log", out var logPath, out pathError))
        {
            return ParseResult.Fail(pathError);
        }

        return Validate(sourcePath, replicaPath, logPath, TimeSpan.FromSeconds(seconds));
    }

    private static ParseResult Validate(string source, string replica, string log, TimeSpan interval)
    {
        if (!Directory.Exists(source))
        {
            return File.Exists(source)
                ? ParseResult.Fail($"Source path is not a folder: {source}")
                : ParseResult.Fail($"Source folder does not exist: {source}");
        }

        if (SymbolicLinks.IsLink(source))
            return ParseResult.Fail("Source folder must not be a symbolic link.");

        if (string.Equals(source, replica, PathComparison))
            return ParseResult.Fail("Source and replica must be different folders.");

        // Syncing a folder into itself would copy files forever, or delete the source while reading it.
        if (IsInside(source, replica))
            return ParseResult.Fail("Replica folder must not be inside the source folder.");

        if (IsInside(replica, source))
            return ParseResult.Fail("Source folder must not be inside the replica folder.");

        if (File.Exists(replica) && !Directory.Exists(replica))
            return ParseResult.Fail($"Replica path exists and is not a folder: {replica}");

        if (Directory.Exists(replica) && SymbolicLinks.IsLink(replica))
            return ParseResult.Fail("Replica folder must not be a symbolic link.");

        if (Directory.Exists(log))
            return ParseResult.Fail($"Log path is a folder, not a file: {log}");

        var logDirectory = Path.GetDirectoryName(log);
        if (!string.IsNullOrEmpty(logDirectory) && File.Exists(logDirectory))
            return ParseResult.Fail($"Log folder path exists and is not a folder: {logDirectory}");

        // A log inside the replica would be deleted, because it is not part of the source.
        // A log inside the source would be copied into the replica and grow without bound.
        if (IsSameOrInside(source, log) || IsSameOrInside(replica, log))
            return ParseResult.Fail("Log file must not be placed inside the source or replica folder.");

        return ParseResult.Ok(new SyncOptions(source, replica, interval, log));
    }

    private static bool TryReadOption(
        string[] args,
        ref int index,
        out string name,
        out string value,
        out string error)
    {
        name = string.Empty;
        value = string.Empty;
        error = string.Empty;

        var token = args[index];
        string rawName;
        string? inline = null;

        var separator = token.IndexOf('=');
        if (separator > 0 && IsOptionToken(token[..separator]))
        {
            rawName = token[..separator];
            inline = token[(separator + 1)..];
        }
        else if (IsOptionToken(token))
        {
            rawName = token;
        }
        else
        {
            error = $"Unexpected argument '{token}'.";
            return false;
        }

        var canonical = CanonicalName(rawName);
        if (canonical is null)
        {
            error = $"Unknown option '{rawName}'. Run with --help to see usage.";
            return false;
        }

        if (inline is null)
        {
            if (index + 1 >= args.Length || IsOptionToken(args[index + 1]))
            {
                error = $"Missing value for {rawName}.";
                return false;
            }

            inline = args[++index];
        }

        if (string.IsNullOrWhiteSpace(inline))
        {
            error = $"Missing value for {rawName}.";
            return false;
        }

        name = canonical;
        value = inline;
        return true;
    }

    private static bool TryNormalize(string raw, string option, out string fullPath, out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        try
        {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid path for {option}: {exception.Message}";
            return false;
        }
    }

    private static bool IsOptionToken(string token)
    {
        if (token.StartsWith("--", StringComparison.Ordinal))
            return token.Length > 2;

        // A negative interval such as -1 is a value, not an option.
        return token.StartsWith('-') && token.Length > 1 && !char.IsDigit(token[1]);
    }

    private static string? CanonicalName(string raw)
    {
        var name = raw.StartsWith("--", StringComparison.Ordinal)
            ? raw[2..]
            : raw.StartsWith('-') ? raw[1..] : raw;

        return name.ToLowerInvariant() switch
        {
            "source" or "s" => "source",
            "replica" or "r" => "replica",
            "interval" or "i" => "interval",
            "log" or "l" => "log",
            _ => null
        };
    }

    private static bool IsInside(string parent, string child) =>
        child.StartsWith(parent + Path.DirectorySeparatorChar, PathComparison);

    private static bool IsSameOrInside(string parent, string child) =>
        string.Equals(parent, child, PathComparison) || IsInside(parent, child);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
