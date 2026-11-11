namespace FolderSync.Tests;

internal sealed class TempFolder : IDisposable
{
    private TempFolder(string path) => Path = path;

    public string Path { get; }

    public static TempFolder Create()
    {
        var directory = Directory.CreateTempSubdirectory("foldersync-");
        return new TempFolder(directory.FullName);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
            return;

        try
        {
            if (!OperatingSystem.IsWindows())
                MakeWritable(Path);

            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup is best-effort. A failed assertion matters more than a temp folder left behind.
        }
    }

    private static void MakeWritable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (IOException)
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetUnixFileMode(
                    entry,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch (IOException)
            {
                // Keep going so the rest of the tree can still be deleted.
            }
        }
    }
}

internal sealed class TempFolders : IDisposable
{
    private readonly TempFolder _root = TempFolder.Create();

    public TempFolders(bool createReplica = false)
    {
        Source = System.IO.Path.Combine(_root.Path, "source");
        Replica = System.IO.Path.Combine(_root.Path, "replica");
        Directory.CreateDirectory(Source);
        if (createReplica)
            Directory.CreateDirectory(Replica);
    }

    public string Source { get; }

    public string Replica { get; }

    public void Dispose() => _root.Dispose();
}
