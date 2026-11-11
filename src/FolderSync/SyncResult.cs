namespace FolderSync;

/// <summary>
/// Counts for a single synchronization pass.
/// </summary>
public sealed class SyncResult
{
    public bool Skipped { get; internal init; }

    public int FilesCopied { get; internal set; }

    public int FilesUpdated { get; internal set; }

    public int FilesRemoved { get; internal set; }

    public int FoldersCreated { get; internal set; }

    public int FoldersRemoved { get; internal set; }

    public int Failures { get; internal set; }
}
