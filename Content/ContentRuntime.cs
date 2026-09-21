namespace SandboxServer;

/// <summary>
/// Loads the authoritative content pack once. Fail-fast on invalid data.
/// </summary>
internal static class ContentRuntime
{
    private static readonly object Gate = new();
    private static bool _loaded;

    public static int SchemaVersion { get; private set; }
    public static int ContentRevision { get; private set; }

    public static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            ContentPack pack = ContentPackReader.Read(ContentPaths.Resolve());
            List<string> errors = ContentValidator.Validate(pack);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Invalid content pack:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
            }

            SchemaVersion = pack.SchemaVersion;
            ContentRevision = pack.ContentRevision;
            BlockCatalog.Install(pack);
            ItemCatalog.Install(pack);
            CurrencyCatalog.Install(pack);
            DropCatalog.Install(pack);
            _loaded = true;
            Console.WriteLine(
                "Content next free ids — blocks:" + NextFree(pack.Blocks.ConvertAll(block => block.Id))
                + " items:" + NextFree(pack.Items.ConvertAll(item => item.Id))
                + " currencies:" + NextFree(pack.Currencies.ConvertAll(currency => currency.Id)));
        }
    }

    private static int NextFree(List<int> ids)
    {
        var used = new HashSet<int>(ids);
        for (int id = 1; id <= ushort.MaxValue; id++)
        {
            if (!used.Contains(id))
            {
                return id;
            }
        }

        return -1;
    }
}
