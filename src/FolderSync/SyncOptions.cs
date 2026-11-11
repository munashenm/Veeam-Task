namespace FolderSync;

/// <summary>
/// Validated command-line settings. Paths are absolute.
/// </summary>
public sealed record SyncOptions(
    string SourcePath,
    string ReplicaPath,
    TimeSpan Interval,
    string LogFilePath);
