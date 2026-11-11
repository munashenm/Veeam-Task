namespace FolderSync;

/// <summary>
/// Records what a sync did. Callers may log from more than one thread.
/// </summary>
public interface ISyncLogger
{
    void Info(string message);

    void Error(string message);
}
