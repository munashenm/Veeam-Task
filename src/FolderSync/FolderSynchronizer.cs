using System.Security.Cryptography;

namespace FolderSync;

/// <summary>
/// Makes the replica an exact copy of the source: new files are copied, changed files are
/// updated, and files or folders that are not in the source are removed. Empty folders are kept.
/// One failed entry is logged and the rest of the pass continues.
/// </summary>
public sealed class FolderSynchronizer
{
    private readonly ISyncLogger _logger;
    private int _syncInProgress;

    public FolderSynchronizer(ISyncLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs one pass. A second call that starts while this one is running is skipped,
    /// so two passes never change the replica at the same time.
    /// </summary>
    public SyncResult Synchronize(string sourcePath, string replicaPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(replicaPath);

        if (Interlocked.CompareExchange(ref _syncInProgress, 1, 0) != 0)
        {
            _logger.Info("Skipped sync because the previous run is still in progress.");
            return new SyncResult { Skipped = true };
        }

        var result = new SyncResult();
        try
        {
            _logger.Info($"Sync started. Source: {sourcePath} Replica: {replicaPath}");
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(sourcePath))
            {
                _logger.Error($"Source folder is not available: {sourcePath}");
                result.Failures++;
            }
            else
            {
                SyncDirectory(sourcePath, replicaPath, result, cancellationToken);
            }

            _logger.Info(
                $"Sync finished. Copied: {result.FilesCopied}, updated: {result.FilesUpdated}, " +
                $"folders created: {result.FoldersCreated}, files removed: {result.FilesRemoved}, " +
                $"folders removed: {result.FoldersRemoved}, failed: {result.Failures}.");
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Sync interrupted because shutdown was requested.");
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _syncInProgress, 0);
        }
    }

    private void SyncDirectory(string source, string replica, SyncResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A link that exists in the replica must be replaced by a real folder.
        // Directory.Exists would follow the link and write outside the replica.
        if (SymbolicLinks.IsLink(replica))
        {
            RemoveLink(replica, result, countAsFolder: true);
            if (SymbolicLinks.IsLink(replica))
                return;
        }

        if (!TryEnsureDirectory(replica, result))
            return;

        if (!TryIndex(source, out var sourceFiles, out var sourceDirectories, result))
            return;

        foreach (var (name, sourceFile) in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyncFile(sourceFile, Path.Combine(replica, name), result, cancellationToken);
        }

        if (!TryList(replica, files: true, out var replicaFiles, result))
            return;

        foreach (var replicaFile in replicaFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(replicaFile);
            if (string.IsNullOrEmpty(name) || sourceFiles.ContainsKey(name))
                continue;

            RemoveFile(replicaFile, result);
        }

        foreach (var (name, sourceDirectory) in sourceDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var replicaDirectory = Path.Combine(replica, name);
            if (File.Exists(replicaDirectory) && !Directory.Exists(replicaDirectory))
                RemoveFile(replicaDirectory, result);

            SyncDirectory(sourceDirectory, replicaDirectory, result, cancellationToken);
        }

        if (!TryList(replica, files: false, out var replicaDirectories, result))
            return;

        foreach (var replicaDirectory in replicaDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(replicaDirectory);
            if (string.IsNullOrEmpty(name) || sourceDirectories.ContainsKey(name))
                continue;

            RemoveDirectory(replicaDirectory, result, cancellationToken);
        }
    }

    private void SyncFile(string sourceFile, string replicaFile, SyncResult result, CancellationToken cancellationToken)
    {
        try
        {
            // Replace a link with a real file. Copying through the link would change the target.
            if (SymbolicLinks.IsLink(replicaFile))
            {
                RemoveLink(replicaFile, result, countAsFolder: false);
                if (SymbolicLinks.IsLink(replicaFile))
                    return;
            }
            else if (Directory.Exists(replicaFile))
            {
                // A directory cannot be overwritten by a file of the same name. Remove it first.
                RemoveDirectory(replicaFile, result, cancellationToken);
            }

            if (!File.Exists(replicaFile))
            {
                CopyFile(sourceFile, replicaFile);
                _logger.Info($"Copied file: {sourceFile} -> {replicaFile}");
                result.FilesCopied++;
                return;
            }

            switch (CompareFiles(sourceFile, replicaFile))
            {
                case FileChange.None:
                    return;
                case FileChange.Content:
                    CopyFile(sourceFile, replicaFile);
                    _logger.Info($"Updated file: {sourceFile} -> {replicaFile}");
                    result.FilesUpdated++;
                    return;
                case FileChange.TimestampOnly:
                    File.SetLastWriteTimeUtc(replicaFile, File.GetLastWriteTimeUtc(sourceFile));
                    _logger.Info($"Updated file timestamp: {replicaFile}");
                    result.FilesUpdated++;
                    return;
                default:
                    throw new InvalidOperationException("Unexpected file comparison result.");
            }
        }
        catch (Exception exception) when (IsEntryError(exception))
        {
            _logger.Error($"Failed to sync file '{sourceFile}' to '{replicaFile}': {exception.Message}");
            result.Failures++;
        }
    }

    /// <summary>
    /// Size and last-write time decide the common cases. MD5 runs only when the size matches
    /// but the timestamp does not, so a copied or touched file is not rewritten when the bytes match.
    /// Same size and the same timestamp are treated as unchanged.
    /// </summary>
    private static FileChange CompareFiles(string sourceFile, string replicaFile)
    {
        var sourceInfo = new FileInfo(sourceFile);
        var replicaInfo = new FileInfo(replicaFile);

        if (sourceInfo.Length != replicaInfo.Length)
            return FileChange.Content;

        if (sourceInfo.LastWriteTimeUtc == replicaInfo.LastWriteTimeUtc)
            return FileChange.None;

        return Md5Equals(sourceFile, replicaFile) ? FileChange.TimestampOnly : FileChange.Content;
    }

    private static bool Md5Equals(string leftPath, string rightPath)
    {
        var leftHash = HashFile(leftPath);
        var rightHash = HashFile(rightPath);
        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static byte[] HashFile(string path)
    {
        // FileShare.ReadWrite lets us hash a file another process still has open.
        // An exclusive lock still fails this entry; the caller logs it and moves on.
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);

        return MD5.HashData(stream);
    }

    private static void CopyFile(string sourceFile, string replicaFile)
    {
        if (File.Exists(replicaFile))
            ClearReadOnly(replicaFile);

        var timestamp = File.GetLastWriteTimeUtc(sourceFile);
        File.Copy(sourceFile, replicaFile, overwrite: true);

        // Keep the replica timestamp aligned so the next pass can trust the cheap comparison.
        File.SetLastWriteTimeUtc(replicaFile, timestamp);
    }

    private void RemoveFile(string path, SyncResult result)
    {
        try
        {
            if (SymbolicLinks.IsLink(path))
            {
                File.Delete(path);
                _logger.Info($"Removed symbolic link: {path}");
                result.FilesRemoved++;
                return;
            }

            ClearReadOnly(path);
            File.Delete(path);
            _logger.Info($"Removed file: {path}");
            result.FilesRemoved++;
        }
        catch (Exception exception) when (IsEntryError(exception))
        {
            _logger.Error($"Failed to remove file '{path}': {exception.Message}");
            result.Failures++;
        }
    }

    private void RemoveDirectory(string path, SyncResult result, CancellationToken cancellationToken)
    {
        // Never walk a link. Deleting "through" it could remove files outside the replica.
        if (SymbolicLinks.IsLink(path))
        {
            RemoveLink(path, result, countAsFolder: true);
            return;
        }

        if (!TryList(path, files: true, out var files, result))
            return;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveFile(file, result);
        }

        if (!TryList(path, files: false, out var directories, result))
            return;

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveDirectory(directory, result, cancellationToken);
        }

        try
        {
            Directory.Delete(path, recursive: false);
            _logger.Info($"Removed folder: {path}");
            result.FoldersRemoved++;
        }
        catch (Exception exception) when (IsEntryError(exception))
        {
            _logger.Error($"Failed to remove folder '{path}': {exception.Message}");
            result.Failures++;
        }
    }

    private void RemoveLink(string path, SyncResult result, bool countAsFolder)
    {
        try
        {
            File.Delete(path);
            _logger.Info($"Removed symbolic link: {path}");
            if (countAsFolder)
                result.FoldersRemoved++;
            else
                result.FilesRemoved++;
        }
        catch (Exception exception) when (IsEntryError(exception))
        {
            _logger.Error($"Failed to remove symbolic link '{path}': {exception.Message}");
            result.Failures++;
        }
    }

    private bool TryEnsureDirectory(string path, SyncResult result)
    {
        try
        {
            if (Directory.Exists(path))
                return true;

            Directory.CreateDirectory(path);
            _logger.Info($"Created folder: {path}");
            result.FoldersCreated++;
            return true;
        }
        catch (Exception exception) when (IsEntryError(exception))
        {
            _logger.Error($"Failed to create folder '{path}': {exception.Message}");
            result.Failures++;
            return false;
        }
    }

    private bool TryIndex(
        string directory,
        out Dictionary<string, string> files,
        out Dictionary<string, string> directories,
        SyncResult result)
    {
        files = new Dictionary<string, string>(NameComparer);
        directories = new Dictionary<string, string>(NameComparer);

        try
        {
            AddEntries(Directory.EnumerateFiles(directory), files);
            AddEntries(Directory.EnumerateDirectories(directory), directories);
            return true;
        }
        catch (Exception exception) when (IsEntryError(exception))
        {
            _logger.Error($"Failed to read source folder '{directory}': {exception.Message}");
            result.Failures++;
            return false;
        }
    }

    private bool TryList(string directory, bool files, out List<string> entries, SyncResult result)
    {
        entries = new List<string>();

        try
        {
            var enumerated = files
                ? Directory.EnumerateFiles(directory)
                : Directory.EnumerateDirectories(directory);
            entries.AddRange(enumerated);
            return true;
        }
        catch (Exception exception) when (IsEntryError(exception))
        {
            var kind = files ? "files" : "folders";
            _logger.Error($"Failed to list {kind} in '{directory}': {exception.Message}");
            result.Failures++;
            return false;
        }
    }

    private void AddEntries(IEnumerable<string> paths, Dictionary<string, string> target)
    {
        foreach (var path in paths)
        {
            // A link back to a parent would recurse forever. Links are not recreated.
            if (SymbolicLinks.IsLink(path))
            {
                _logger.Info($"Skipped symbolic link: {path}");
                continue;
            }

            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(name))
                target[name] = path;
        }
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static bool IsEntryError(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;

    private static StringComparer NameComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private enum FileChange
    {
        None,
        Content,
        TimestampOnly
    }
}
