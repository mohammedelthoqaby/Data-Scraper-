namespace LazyBean.IntegrationTests.Collection;

internal static class Fixtures
{
    // bin/Debug/net10.0 -> LazyBean.IntegrationTests -> tests -> tests/fixtures
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures"));

    public static string Read(string fileName) => File.ReadAllText(Path.Combine(Root, fileName));
}
