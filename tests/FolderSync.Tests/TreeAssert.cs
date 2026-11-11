namespace FolderSync.Tests;

internal static class TreeAssert
{
    public static void Equal(string expectedRoot, string actualRoot)
    {
        Assert.True(Directory.Exists(expectedRoot), $"Expected folder was not created: {expectedRoot}");
        Assert.True(Directory.Exists(actualRoot), $"Replica folder is missing: {actualRoot}");
        EqualCore(expectedRoot, actualRoot);
    }

    private static void EqualCore(string expected, string actual)
    {
        var expectedFiles = Names(Directory.GetFiles(expected));
        var actualFiles = Names(Directory.GetFiles(actual));
        Assert.Equal(expectedFiles, actualFiles);

        foreach (var name in expectedFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(expected, name)),
                File.ReadAllBytes(Path.Combine(actual, name)));
        }

        var expectedDirectories = Names(Directory.GetDirectories(expected));
        var actualDirectories = Names(Directory.GetDirectories(actual));
        Assert.Equal(expectedDirectories, actualDirectories);

        foreach (var name in expectedDirectories)
            EqualCore(Path.Combine(expected, name), Path.Combine(actual, name));
    }

    private static List<string> Names(IEnumerable<string> paths) =>
        paths
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
}
