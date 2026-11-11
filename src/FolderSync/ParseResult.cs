namespace FolderSync;

/// <summary>
/// Outcome of reading the command line. Exactly one of success, help, or error is set.
/// </summary>
public sealed record ParseResult(bool Success, bool HelpRequested, SyncOptions? Options, string? Error)
{
    public static ParseResult Ok(SyncOptions options) => new(true, false, options, null);

    public static ParseResult Help() => new(false, true, null, null);

    public static ParseResult Fail(string error) => new(false, false, null, error);
}
