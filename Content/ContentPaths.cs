namespace SandboxServer;

internal static class ContentPaths
{
    public static string Resolve()
    {
        foreach (string root in CandidateRoots())
        {
            string pack = Path.Combine(root, "pack.json");
            if (File.Exists(pack))
            {
                return root;
            }
        }

        throw new InvalidOperationException(
            "Content pack not found. Expected Content/pack.json next to the executable or in Server/Content.");
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Content");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Content");

        string? walk = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && walk is not null; i++)
        {
            yield return Path.Combine(walk, "Content");
            walk = Directory.GetParent(walk)?.FullName;
        }
    }
}
