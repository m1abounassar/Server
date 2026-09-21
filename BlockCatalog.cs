namespace SandboxServer;

internal readonly struct BlockDefinition
{
    public BlockDefinition(
        ushort id,
        string key,
        string name,
        TileLayer layer,
        bool breakable,
        bool placeable,
        byte maxHealth,
        bool solid,
        int regenDelayTicks,
        bool retired)
    {
        Id = id;
        Key = key;
        Name = name;
        Layer = layer;
        Breakable = breakable;
        Placeable = placeable;
        MaxHealth = maxHealth == 0 ? (byte)1 : maxHealth;
        Solid = solid;
        RegenDelayTicks = regenDelayTicks < 0 ? 0 : regenDelayTicks;
        Retired = retired;
    }

    public ushort Id { get; }
    public string Key { get; }
    public string Name { get; }
    public TileLayer Layer { get; }
    public bool Breakable { get; }
    public bool Placeable { get; }
    public byte MaxHealth { get; }
    public bool Solid { get; }
    public int RegenDelayTicks { get; }
    public bool Retired { get; }
}

/// <summary>
/// Authoritative block definitions loaded from the content pack.
/// Lookups are dictionaries and do not assume dense IDs.
/// </summary>
internal static class BlockCatalog
{
    private static Dictionary<ushort, BlockDefinition> _byId = new();
    private static Dictionary<string, ushort> _byKey = new(StringComparer.Ordinal);

    internal static void Install(ContentPack pack)
    {
        var byId = new Dictionary<ushort, BlockDefinition>();
        var byKey = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (BlockDto dto in pack.Blocks)
        {
            ContentValidator.TryParseLayer(dto.Layer, out TileLayer layer);
            var definition = new BlockDefinition(
                (ushort)dto.Id,
                dto.Key,
                dto.DisplayName,
                layer,
                dto.Breakable,
                dto.Placeable,
                (byte)Math.Clamp(dto.MaxHealth, 1, byte.MaxValue),
                dto.Solid,
                dto.RegenDelayTicks,
                dto.Retired);
            byId[definition.Id] = definition;
            byKey[definition.Key] = definition.Id;
        }

        _byId = byId;
        _byKey = byKey;
    }

    public static bool TryGet(ushort id, out BlockDefinition definition)
    {
        ContentRuntime.EnsureLoaded();
        if (_byId.TryGetValue(id, out definition) && !definition.Retired)
        {
            return true;
        }

        definition = default;
        return false;
    }

    public static bool TryGetIncludingRetired(ushort id, out BlockDefinition definition)
    {
        ContentRuntime.EnsureLoaded();
        return _byId.TryGetValue(id, out definition);
    }

    public static ushort RequireKey(string key)
    {
        ContentRuntime.EnsureLoaded();
        if (_byKey.TryGetValue(key, out ushort id) && TryGet(id, out _))
        {
            return id;
        }

        throw new InvalidOperationException("Unknown block key '" + key + "'.");
    }

    public static byte MaxHealth(ushort blockId)
    {
        return TryGet(blockId, out BlockDefinition definition) ? definition.MaxHealth : (byte)0;
    }

    public static bool IsSolid(ushort blockId)
    {
        return TryGet(blockId, out BlockDefinition definition) && definition.Solid;
    }

    public static IReadOnlyDictionary<ushort, BlockDefinition> All
    {
        get
        {
            ContentRuntime.EnsureLoaded();
            return _byId;
        }
    }
}
