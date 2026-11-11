namespace FolderSync.Tests;

public class ArgumentParserTests
{
    [Fact]
    public void Parse_returns_absolute_paths_for_a_valid_command_line()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        var replica = Path.Combine(root.Path, "replica");
        var log = Path.Combine(root.Path, "logs", "sync.log");
        Directory.CreateDirectory(source);

        var result = ArgumentParser.Parse(
        [
            "--source", source,
            "--replica", replica,
            "--interval", "60",
            "--log", log
        ]);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Options);
        Assert.Equal(source, result.Options.SourcePath);
        Assert.Equal(replica, result.Options.ReplicaPath);
        Assert.Equal(log, result.Options.LogFilePath);
        Assert.Equal(TimeSpan.FromSeconds(60), result.Options.Interval);
        Assert.True(Path.IsPathRooted(result.Options.SourcePath));
    }

    [Fact]
    public void Parse_accepts_short_options_equals_syntax_and_a_relative_source()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);
        var replica = Path.Combine(root.Path, "replica");
        var log = Path.Combine(root.Path, "sync.log");
        var relativeSource = Path.GetRelativePath(Directory.GetCurrentDirectory(), source);

        var result = ArgumentParser.Parse(
        [
            "-s", relativeSource,
            "-r", replica,
            "--interval=15",
            "-l", log
        ]);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Options);
        Assert.Equal(Path.GetFullPath(source), result.Options.SourcePath);
        Assert.Equal(TimeSpan.FromSeconds(15), result.Options.Interval);
    }

    [Fact]
    public void Parse_trims_a_trailing_directory_separator()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);

        var result = ArgumentParser.Parse(
        [
            "--source", source + Path.DirectorySeparatorChar,
            "--replica", Path.Combine(root.Path, "replica") + Path.DirectorySeparatorChar,
            "--interval", "5",
            "--log", Path.Combine(root.Path, "sync.log")
        ]);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Options);
        Assert.Equal(source, result.Options.SourcePath);
        Assert.False(result.Options.ReplicaPath.EndsWith(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Parse_requests_help_without_requiring_the_other_arguments()
    {
        var result = ArgumentParser.Parse(["--source", "ignored", "--help"]);

        Assert.True(result.HelpRequested);
        Assert.False(result.Success);
        Assert.Contains("--source", ArgumentParser.Usage, StringComparison.Ordinal);
        Assert.Contains("--replica", ArgumentParser.Usage, StringComparison.Ordinal);
        Assert.Contains("--interval", ArgumentParser.Usage, StringComparison.Ordinal);
        Assert.Contains("--log", ArgumentParser.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_an_empty_command_line()
    {
        var result = ArgumentParser.Parse([]);

        Assert.False(result.Success);
        Assert.Contains("No arguments", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_missing_required_options()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);

        var result = ArgumentParser.Parse(["--source", source]);

        Assert.False(result.Success);
        Assert.Contains("--replica", result.Error, StringComparison.Ordinal);
        Assert.Contains("--interval", result.Error, StringComparison.Ordinal);
        Assert.Contains("--log", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_an_unknown_option()
    {
        var result = ArgumentParser.Parse(["--bogus", "value"]);

        Assert.False(result.Success);
        Assert.Contains("Unknown option", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_an_unexpected_argument()
    {
        var result = ArgumentParser.Parse(["source", "replica"]);

        Assert.False(result.Success);
        Assert.Contains("Unexpected argument", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_missing_option_value()
    {
        var result = ArgumentParser.Parse(["--interval"]);

        Assert.False(result.Success);
        Assert.Contains("Missing value", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_duplicated_option()
    {
        var result = ArgumentParser.Parse(["--interval", "5", "--interval", "10"]);

        Assert.False(result.Success);
        Assert.Contains("more than once", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("-1")]
    [InlineData("-30")]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("60s")]
    public void Parse_rejects_an_interval_that_is_not_a_positive_whole_number(string interval)
    {
        var result = ArgumentParser.Parse(
        [
            "--source", "source",
            "--replica", "replica",
            "--interval", interval,
            "--log", "sync.log"
        ]);

        Assert.False(result.Success);
        Assert.Contains("Interval", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_source_folder_that_does_not_exist()
    {
        using var root = TempFolder.Create();
        var missing = Path.Combine(root.Path, "missing");

        var result = Parse(missing, Path.Combine(root.Path, "replica"), Path.Combine(root.Path, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_source_path_that_is_a_file()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "file.txt");
        File.WriteAllText(source, "not a folder");

        var result = Parse(source, Path.Combine(root.Path, "replica"), Path.Combine(root.Path, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("not a folder", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_replica_path_that_is_a_file()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);
        var replica = Path.Combine(root.Path, "replica");
        File.WriteAllText(replica, "not a folder");

        var result = Parse(source, replica, Path.Combine(root.Path, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("not a folder", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_the_same_folder_for_source_and_replica()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);

        var result = Parse(source, source + Path.DirectorySeparatorChar, Path.Combine(root.Path, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("different folders", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_replica_inside_the_source()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);

        var result = Parse(source, Path.Combine(source, "replica"), Path.Combine(root.Path, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("Replica folder must not be inside", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_source_inside_the_replica()
    {
        using var root = TempFolder.Create();
        var replica = Path.Combine(root.Path, "replica");
        var source = Path.Combine(replica, "source");
        Directory.CreateDirectory(source);

        var result = Parse(source, replica, Path.Combine(root.Path, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("Source folder must not be inside", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_log_file_inside_the_source_or_replica()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);
        var replica = Path.Combine(root.Path, "replica");

        var insideSource = Parse(source, replica, Path.Combine(source, "sync.log"));
        var insideReplica = Parse(source, replica, Path.Combine(replica, "nested", "sync.log"));

        Assert.Contains("Log file must not", insideSource.Error, StringComparison.Ordinal);
        Assert.Contains("Log file must not", insideReplica.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_log_path_that_is_a_folder()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);
        var logFolder = Path.Combine(root.Path, "logs");
        Directory.CreateDirectory(logFolder);

        var result = Parse(source, Path.Combine(root.Path, "replica"), logFolder);

        Assert.False(result.Success);
        Assert.Contains("not a file", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_log_folder_that_is_a_file()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        Directory.CreateDirectory(source);
        var notADirectory = Path.Combine(root.Path, "not-a-directory");
        File.WriteAllText(notADirectory, "file");

        var result = Parse(source, Path.Combine(root.Path, "replica"), Path.Combine(notADirectory, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("not a folder", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_a_source_symbolic_link()
    {
        using var root = TempFolder.Create();
        var real = Path.Combine(root.Path, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(root.Path, "link");
        Directory.CreateSymbolicLink(link, real);

        var result = Parse(link, Path.Combine(root.Path, "replica"), Path.Combine(root.Path, "sync.log"));

        Assert.False(result.Success);
        Assert.Contains("symbolic link", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static ParseResult Parse(string source, string replica, string log) =>
        ArgumentParser.Parse(["--source", source, "--replica", replica, "--interval", "5", "--log", log]);
}
