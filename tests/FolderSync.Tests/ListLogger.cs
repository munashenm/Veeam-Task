namespace FolderSync.Tests;

internal sealed class ListLogger : ISyncLogger
{
    private readonly object _gate = new();
    private readonly Action<string>? _onInfo;

    public ListLogger(Action<string>? onInfo = null) => _onInfo = onInfo;

    public List<string> Infos { get; } = new();

    public List<string> Errors { get; } = new();

    public void Info(string message)
    {
        lock (_gate)
            Infos.Add(message);

        // Invoke outside the lock so a callback can start another sync without deadlocking.
        _onInfo?.Invoke(message);
    }

    public void Error(string message)
    {
        lock (_gate)
            Errors.Add(message);
    }
}
