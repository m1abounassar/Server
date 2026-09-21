namespace SandboxServer;

internal readonly struct CurrencyDefinition
{
    public CurrencyDefinition(ushort id, string key, string displayName, int worldMaxStack)
    {
        Id = id;
        Key = key;
        DisplayName = displayName;
        WorldMaxStack = worldMaxStack < 1 ? 1 : worldMaxStack;
    }

    public ushort Id { get; }
    public string Key { get; }
    public string DisplayName { get; }
    public int WorldMaxStack { get; }
}

/// <summary>
/// Session currencies loaded from the content pack. Gems are not inventory stacks.
/// </summary>
internal static class CurrencyCatalog
{
    public const int DefaultWorldMaxStack = 999999;

    private static Dictionary<ushort, CurrencyDefinition> _byId = new();

    internal static void Install(ContentPack pack)
    {
        var byId = new Dictionary<ushort, CurrencyDefinition>();
        foreach (CurrencyDto dto in pack.Currencies)
        {
            var definition = new CurrencyDefinition((ushort)dto.Id, dto.Key, dto.DisplayName, dto.WorldMaxStack);
            byId[definition.Id] = definition;
        }

        _byId = byId;
    }

    public static bool TryGet(ushort id, out CurrencyDefinition definition)
    {
        ContentRuntime.EnsureLoaded();
        return _byId.TryGetValue(id, out definition);
    }

    public static int WorldMaxStack(ushort currencyId)
    {
        return TryGet(currencyId, out CurrencyDefinition definition) ? definition.WorldMaxStack : 1;
    }

    public static IReadOnlyDictionary<ushort, CurrencyDefinition> All
    {
        get
        {
            ContentRuntime.EnsureLoaded();
            return _byId;
        }
    }
}
