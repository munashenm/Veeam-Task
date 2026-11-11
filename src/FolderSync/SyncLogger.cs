using System.Globalization;
using System.Text;

namespace FolderSync;

/// <summary>
/// Writes a timestamped line to the console and appends the same line to a log file.
/// </summary>
public sealed class SyncLogger : ISyncLogger, IDisposable
{
    private readonly TextWriter _console;
    private readonly StreamWriter _file;
    private readonly object _gate = new();
    private bool _disposed;

    /// <param name="logFilePath">File to append to. Parent folders are created.</param>
    /// <param name="console">Console writer. Defaults to standard output. Not disposed.</param>
    public SyncLogger(string logFilePath, TextWriter? console = null)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
            throw new ArgumentException("Log file path is required.", nameof(logFilePath));

        var fullPath = Path.GetFullPath(logFilePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _console = console ?? Console.Out;
        // UTF-8 without a BOM, so the log file matches the console text.
        _file = new StreamWriter(fullPath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _file.Dispose();
        }
    }

    private void Write(string level, string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var line = $"{timestamp} [{level}] {message}";

        lock (_gate)
        {
            if (_disposed)
                return;

            // The file is the durable record, so write it first.
            _file.WriteLine(line);
            _console.WriteLine(line);
        }
    }
}
