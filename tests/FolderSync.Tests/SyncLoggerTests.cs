using System.Text.RegularExpressions;

namespace FolderSync.Tests;

public class SyncLoggerTests
{
    private static readonly Regex LinePattern = new(
        @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[(INFO|ERROR)\] .+$",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Logger_writes_the_same_timestamped_line_to_the_console_and_the_file()
    {
        using var root = TempFolder.Create();
        var log = Path.Combine(root.Path, "sync.log");
        var console = new StringWriter();

        using (var logger = new SyncLogger(log, console))
        {
            logger.Info("copied notes.txt");
            logger.Error("failed to remove old.txt");
        }

        var fileBytes = File.ReadAllBytes(log);
        Assert.False(fileBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));

        var consoleLines = Lines(console.ToString());
        var fileLines = Lines(File.ReadAllText(log));

        Assert.Equal(consoleLines, fileLines);
        Assert.Equal(2, fileLines.Count);
        Assert.All(fileLines, line => Assert.Matches(LinePattern, line));
        Assert.EndsWith("[INFO] copied notes.txt", fileLines[0], StringComparison.Ordinal);
        Assert.EndsWith("[ERROR] failed to remove old.txt", fileLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Logger_creates_the_log_directory_and_appends()
    {
        using var root = TempFolder.Create();
        var log = Path.Combine(root.Path, "nested", "logs", "sync.log");

        using (var logger = new SyncLogger(log, new StringWriter()))
            logger.Info("first");

        using (var logger = new SyncLogger(log, new StringWriter()))
            logger.Info("second");

        var text = File.ReadAllText(log);
        Assert.Contains("[INFO] first", text, StringComparison.Ordinal);
        Assert.Contains("[INFO] second", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("first", StringComparison.Ordinal) < text.IndexOf("second", StringComparison.Ordinal));
    }

    private static List<string> Lines(string text) =>
        text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
}
