namespace FolderSync;

/// <summary>
/// Detects symbolic links without following them.
/// </summary>
internal static class SymbolicLinks
{
    public static bool IsLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false) is not null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }
}
