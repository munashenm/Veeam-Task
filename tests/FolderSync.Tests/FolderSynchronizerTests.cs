namespace FolderSync.Tests;

public class FolderSynchronizerTests
{
    [Fact]
    public void Synchronize_copies_a_new_file_and_creates_the_replica()
    {
        using var folders = new TempFolders();
        File.WriteAllText(Path.Combine(folders.Source, "hello.txt"), "hello");
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(folders.Replica, "hello.txt")));
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.Failures);
        Assert.Contains(logger.Infos, message => message.Contains("Created folder", StringComparison.Ordinal));
        Assert.Contains(
            logger.Infos,
            message => message.Contains($"Copied file: {Path.Combine(folders.Source, "hello.txt")}", StringComparison.Ordinal));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void Synchronize_updates_a_file_when_its_content_changes()
    {
        using var folders = new TempFolders();
        var sourceFile = Path.Combine(folders.Source, "notes.txt");
        var replicaFile = Path.Combine(folders.Replica, "notes.txt");
        File.WriteAllText(sourceFile, "version-1");
        var synchronizer = new FolderSynchronizer(new ListLogger());
        synchronizer.Synchronize(folders.Source, folders.Replica);

        File.WriteAllText(sourceFile, "version-2-longer");
        var logger = new ListLogger();
        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        Assert.Equal("version-2-longer", File.ReadAllText(replicaFile));
        Assert.Equal(1, result.FilesUpdated);
        Assert.Equal(0, result.FilesCopied);
        Assert.Contains(logger.Infos, message => message.StartsWith("Updated file:", StringComparison.Ordinal));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void Synchronize_uses_md5_when_the_size_matches_but_the_bytes_differ()
    {
        using var folders = new TempFolders(createReplica: true);
        var sourceFile = Path.Combine(folders.Source, "data.txt");
        var replicaFile = Path.Combine(folders.Replica, "data.txt");
        File.WriteAllText(sourceFile, "aaaa");
        File.WriteAllText(replicaFile, "bbbb");
        File.SetLastWriteTimeUtc(sourceFile, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(replicaFile, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        Assert.Equal("aaaa", File.ReadAllText(replicaFile));
        Assert.Equal(1, result.FilesUpdated);
        Assert.Equal(0, result.FilesCopied);
        Assert.Contains(logger.Infos, message => message.StartsWith("Updated file:", StringComparison.Ordinal));
    }

    [Fact]
    public void Synchronize_aligns_the_timestamp_when_the_bytes_already_match()
    {
        using var folders = new TempFolders(createReplica: true);
        var sourceFile = Path.Combine(folders.Source, "data.txt");
        var replicaFile = Path.Combine(folders.Replica, "data.txt");
        File.WriteAllText(sourceFile, "same-bytes");
        File.WriteAllText(replicaFile, "same-bytes");
        File.SetLastWriteTimeUtc(sourceFile, new DateTime(2024, 6, 1, 8, 30, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(replicaFile, new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        Assert.Equal(File.GetLastWriteTimeUtc(sourceFile), File.GetLastWriteTimeUtc(replicaFile));
        Assert.Equal("same-bytes", File.ReadAllText(replicaFile));
        Assert.Equal(1, result.FilesUpdated);
        Assert.Equal(0, result.FilesCopied);
        Assert.Contains(logger.Infos, message => message.StartsWith("Updated file timestamp:", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Infos, message => message.StartsWith("Updated file:", StringComparison.Ordinal));
    }

    [Fact]
    public void Synchronize_leaves_an_unchanged_tree_alone_on_the_second_run()
    {
        using var folders = new TempFolders();
        Directory.CreateDirectory(Path.Combine(folders.Source, "empty"));
        Directory.CreateDirectory(Path.Combine(folders.Source, "nested"));
        File.WriteAllText(Path.Combine(folders.Source, "a.txt"), "same");
        File.WriteAllText(Path.Combine(folders.Source, "nested", "b.txt"), "nested");
        var synchronizer = new FolderSynchronizer(new ListLogger());

        var first = synchronizer.Synchronize(folders.Source, folders.Replica);
        var second = synchronizer.Synchronize(folders.Source, folders.Replica);

        Assert.True(first.FilesCopied >= 2);
        Assert.True(first.FoldersCreated >= 1);
        Assert.Equal(0, second.FilesCopied);
        Assert.Equal(0, second.FilesUpdated);
        Assert.Equal(0, second.FilesRemoved);
        Assert.Equal(0, second.FoldersCreated);
        Assert.Equal(0, second.FoldersRemoved);
        Assert.Equal(0, second.Failures);
        Assert.False(second.Skipped);
        TreeAssert.Equal(folders.Source, folders.Replica);
    }

    [Fact]
    public void Synchronize_deletes_a_file_that_was_removed_from_the_source()
    {
        using var folders = new TempFolders();
        var sourceKeep = Path.Combine(folders.Source, "keep.txt");
        var sourceDrop = Path.Combine(folders.Source, "drop.txt");
        File.WriteAllText(sourceKeep, "keep");
        File.WriteAllText(sourceDrop, "drop");
        var synchronizer = new FolderSynchronizer(new ListLogger());
        synchronizer.Synchronize(folders.Source, folders.Replica);
        File.Delete(sourceDrop);
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        Assert.False(File.Exists(Path.Combine(folders.Replica, "drop.txt")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(folders.Replica, "keep.txt")));
        Assert.Equal(1, result.FilesRemoved);
        Assert.Contains(
            logger.Infos,
            message => message == $"Removed file: {Path.Combine(folders.Replica, "drop.txt")}");
    }

    [Fact]
    public void Synchronize_copies_nested_folders_and_files()
    {
        using var folders = new TempFolders();
        Directory.CreateDirectory(Path.Combine(folders.Source, "a", "b"));
        File.WriteAllText(Path.Combine(folders.Source, "a", "d.txt"), "mid");
        File.WriteAllText(Path.Combine(folders.Source, "a", "b", "c.txt"), "nested");
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        TreeAssert.Equal(folders.Source, folders.Replica);
        Assert.Equal(2, result.FilesCopied);
        Assert.True(result.FoldersCreated >= 2);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void Synchronize_creates_empty_directories_and_removes_extra_ones()
    {
        using var folders = new TempFolders(createReplica: true);
        Directory.CreateDirectory(Path.Combine(folders.Source, "keep", "empty"));
        Directory.CreateDirectory(Path.Combine(folders.Replica, "stale-empty"));
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        Assert.True(Directory.Exists(Path.Combine(folders.Replica, "keep", "empty")));
        Assert.False(Directory.Exists(Path.Combine(folders.Replica, "stale-empty")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(folders.Replica, "keep", "empty")));
        Assert.True(result.FoldersCreated >= 2);
        Assert.Equal(1, result.FoldersRemoved);
        Assert.Contains(logger.Infos, message => message.Contains($"{Path.DirectorySeparatorChar}empty", StringComparison.Ordinal));
        Assert.Contains(logger.Infos, message => message.Contains("Removed folder:", StringComparison.Ordinal)
            && message.Contains("stale-empty", StringComparison.Ordinal));
    }

    [Fact]
    public void Synchronize_removes_a_nested_folder_that_is_not_in_the_source()
    {
        using var folders = new TempFolders(createReplica: true);
        File.WriteAllText(Path.Combine(folders.Source, "keep.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(folders.Replica, "extra", "deep"));
        File.WriteAllText(Path.Combine(folders.Replica, "extra", "deep", "old.txt"), "old");
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        TreeAssert.Equal(folders.Source, folders.Replica);
        Assert.Equal(1, result.FilesRemoved);
        Assert.Equal(2, result.FoldersRemoved);
        Assert.Contains(logger.Infos, message => message.Contains("Removed file:", StringComparison.Ordinal)
            && message.Contains("old.txt", StringComparison.Ordinal));
        Assert.Contains(logger.Infos, message => message.Contains("Removed folder:", StringComparison.Ordinal)
            && message.EndsWith($"{Path.DirectorySeparatorChar}extra", StringComparison.Ordinal));
    }

    [Fact]
    public void Synchronize_replaces_a_file_with_a_folder_and_a_folder_with_a_file()
    {
        using var folders = new TempFolders(createReplica: true);
        Directory.CreateDirectory(Path.Combine(folders.Source, "now-folder"));
        File.WriteAllText(Path.Combine(folders.Source, "now-folder", "inside.txt"), "inside");
        File.WriteAllText(Path.Combine(folders.Source, "now-file"), "file-body");
        File.WriteAllText(Path.Combine(folders.Replica, "now-folder"), "old-file");
        Directory.CreateDirectory(Path.Combine(folders.Replica, "now-file"));
        File.WriteAllText(Path.Combine(folders.Replica, "now-file", "stale.txt"), "stale");
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

        TreeAssert.Equal(folders.Source, folders.Replica);
        Assert.Equal(0, result.Failures);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void Synchronize_makes_a_mixed_replica_match_the_source()
    {
        using var folders = new TempFolders(createReplica: true);
        Directory.CreateDirectory(Path.Combine(folders.Source, "docs", "empty"));
        File.WriteAllText(Path.Combine(folders.Source, "docs", "readme.txt"), "new");
        File.WriteAllText(Path.Combine(folders.Source, "root.txt"), "root");
        Directory.CreateDirectory(Path.Combine(folders.Replica, "docs"));
        File.WriteAllText(Path.Combine(folders.Replica, "docs", "readme.txt"), "stale");
        File.WriteAllText(Path.Combine(folders.Replica, "gone.txt"), "gone");
        Directory.CreateDirectory(Path.Combine(folders.Replica, "obsolete"));
        File.WriteAllText(Path.Combine(folders.Replica, "obsolete", "old.txt"), "old");

        var result = new FolderSynchronizer(new ListLogger()).Synchronize(folders.Source, folders.Replica);

        TreeAssert.Equal(folders.Source, folders.Replica);
        Assert.Equal(0, result.Failures);
    }

    [Fact]
    public void Synchronize_overwrites_a_read_only_replica_file()
    {
        using var folders = new TempFolders(createReplica: true);
        var sourceFile = Path.Combine(folders.Source, "locked.txt");
        var replicaFile = Path.Combine(folders.Replica, "locked.txt");
        File.WriteAllText(sourceFile, "fresh content");
        File.WriteAllText(replicaFile, "stale");
        File.SetAttributes(replicaFile, FileAttributes.ReadOnly);

        var result = new FolderSynchronizer(new ListLogger()).Synchronize(folders.Source, folders.Replica);

        Assert.Equal("fresh content", File.ReadAllText(replicaFile));
        Assert.Equal(0, result.Failures);
    }

    [Fact]
    public void Synchronize_logs_an_access_error_and_still_copies_the_other_files()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var folders = new TempFolders(createReplica: true);
        var blocked = Path.Combine(folders.Replica, "blocked");
        Directory.CreateDirectory(blocked);
        Directory.CreateDirectory(Path.Combine(folders.Source, "blocked"));
        File.WriteAllText(Path.Combine(folders.Source, "blocked", "a.txt"), "secret");
        File.WriteAllText(Path.Combine(folders.Source, "ok.txt"), "ok");
        File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var logger = new ListLogger();

        try
        {
            var result = new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica);

            Assert.True(result.Failures >= 1);
            Assert.Equal("ok", File.ReadAllText(Path.Combine(folders.Replica, "ok.txt")));
            Assert.False(File.Exists(Path.Combine(blocked, "a.txt")));
            Assert.Contains(logger.Errors, message => message.Contains("a.txt", StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(
                blocked,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Synchronize_skips_links_and_does_not_change_their_targets()
    {
        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        var replica = Path.Combine(root.Path, "replica");
        var outside = Path.Combine(root.Path, "outside");
        var keep = Path.Combine(root.Path, "keep");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(replica);
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(keep);

        var outsideFile = Path.Combine(outside, "secret.txt");
        File.WriteAllText(outsideFile, "secret");
        File.CreateSymbolicLink(Path.Combine(source, "secret.txt"), outsideFile);
        Directory.CreateSymbolicLink(Path.Combine(source, "outside-dir"), outside);

        var keptFile = Path.Combine(keep, "keep.txt");
        File.WriteAllText(keptFile, "keep");
        Directory.CreateSymbolicLink(Path.Combine(replica, "keep-link"), keep);
        File.WriteAllText(Path.Combine(source, "plain.txt"), "plain");
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(source, replica);

        Assert.Equal("plain", File.ReadAllText(Path.Combine(replica, "plain.txt")));
        Assert.False(File.Exists(Path.Combine(replica, "secret.txt")));
        Assert.False(Directory.Exists(Path.Combine(replica, "outside-dir")));
        Assert.False(Directory.Exists(Path.Combine(replica, "keep-link")));
        Assert.Equal("secret", File.ReadAllText(outsideFile));
        Assert.Equal("keep", File.ReadAllText(keptFile));
        Assert.Contains(logger.Infos, message => message.StartsWith("Skipped symbolic link:", StringComparison.Ordinal));
        Assert.Contains(logger.Infos, message => message.StartsWith("Removed symbolic link:", StringComparison.Ordinal));
        Assert.Equal(0, result.Failures);
    }

    [Fact]
    public void Synchronize_does_not_overwrite_a_link_target_when_the_link_cannot_be_removed()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var root = TempFolder.Create();
        var source = Path.Combine(root.Path, "source");
        var replica = Path.Combine(root.Path, "replica");
        var outside = Path.Combine(root.Path, "outside.txt");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(replica);
        File.WriteAllText(outside, "secret");
        File.WriteAllText(Path.Combine(source, "link.txt"), "local");
        File.CreateSymbolicLink(Path.Combine(replica, "link.txt"), outside);
        File.SetUnixFileMode(replica, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var result = new FolderSynchronizer(new ListLogger()).Synchronize(source, replica);

            Assert.Equal("secret", File.ReadAllText(outside));
            Assert.True(result.Failures >= 1);
        }
        finally
        {
            File.SetUnixFileMode(
                replica,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Synchronize_skips_a_second_call_while_the_first_is_running()
    {
        using var folders = new TempFolders();
        File.WriteAllText(Path.Combine(folders.Source, "a.txt"), "a");
        using var inside = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var logger = new ListLogger(message =>
        {
            if (!message.StartsWith("Sync started", StringComparison.Ordinal))
                return;

            inside.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The overlap test did not release the first sync.");
        });
        var synchronizer = new FolderSynchronizer(logger);
        var first = Task.Run(() => synchronizer.Synchronize(folders.Source, folders.Replica));

        SyncResult second;
        try
        {
            Assert.True(inside.Wait(TimeSpan.FromSeconds(5)));
            second = synchronizer.Synchronize(folders.Source, folders.Replica);
        }
        finally
        {
            release.Set();
        }

        var firstResult = await first;
        Assert.False(firstResult.Skipped);
        Assert.Equal(1, firstResult.FilesCopied);
        Assert.True(second.Skipped);
        Assert.Contains(logger.Infos, message => message.Contains("Skipped sync", StringComparison.Ordinal));
    }

    [Fact]
    public void Synchronize_stops_between_files_when_cancellation_is_requested()
    {
        using var folders = new TempFolders();
        for (var index = 0; index < 8; index++)
            File.WriteAllText(Path.Combine(folders.Source, $"file-{index}.txt"), new string('x', 32));

        using var cancellation = new CancellationTokenSource();
        var logger = new ListLogger(message =>
        {
            if (message.StartsWith("Copied file:", StringComparison.Ordinal))
                cancellation.Cancel();
        });

        var exception = Record.Exception(() =>
            new FolderSynchronizer(logger).Synchronize(folders.Source, folders.Replica, cancellation.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Single(Directory.GetFiles(folders.Replica));
        Assert.Contains(logger.Infos, message => message.Contains("interrupted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Synchronize_logs_a_missing_source_and_does_not_throw()
    {
        using var root = TempFolder.Create();
        var missing = Path.Combine(root.Path, "missing");
        var replica = Path.Combine(root.Path, "replica");
        var logger = new ListLogger();

        var result = new FolderSynchronizer(logger).Synchronize(missing, replica);

        Assert.Equal(1, result.Failures);
        Assert.False(Directory.Exists(replica));
        Assert.Contains(logger.Errors, message => message.Contains("not available", StringComparison.Ordinal));
        Assert.Contains(logger.Infos, message => message.StartsWith("Sync finished.", StringComparison.Ordinal));
    }
}
