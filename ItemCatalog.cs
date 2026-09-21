namespace SandboxServer;

internal enum ItemIdentity : byte
{
    Stackable = 0,
    UniqueInstance = 1
}

internal readonly struct ItemDefinition
{
    public ItemDefinition(
        ushort id,
        string key,
        string displayName,
        int maxStack,
        bool placeable,
        ushort placesBlockId,
        bool permanent,
        int? loadoutSlot,
        ItemIdentity identity)
    {
        Id = id;
        Key = key;
        DisplayName = displayName;
        MaxStack = maxStack < 1 ? 1 : maxStack;
        Placeable = placeable;
        PlacesBlockId = placeable ? placesBlockId : (ushort)0;
        Permanent = permanent;
        LoadoutSlot = loadoutSlot;
        Identity = identity;
    }

    public ushort Id { get; }
    public string Key { get; }
    public string DisplayName { get; }
    public int MaxStack { get; }
    public bool Placeable { get; }
    public ushort PlacesBlockId { get; }
    public bool Permanent { get; }
    public int? LoadoutSlot { get; }
    public ItemIdentity Identity { get; }
}

/// <summary>
/// Authoritative item definitions loaded from the content pack.
/// </summary>
internal static class ItemCatalog
{
    private static Dictionary<ushort, ItemDefinition> _byId = new();
    private static Dictionary<string, ushort> _byKey = new(StringComparer.Ordinal);
    private static ItemDefinition[] _loadout = Array.Empty<ItemDefinition>();

    internal static void Install(ContentPack pack)
    {
        var byId = new Dictionary<ushort, ItemDefinition>();
        var byKey = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var loadout = new List<ItemDefinition>();
        foreach (ItemDto dto in pack.Items)
        {
            bool placeable = dto.Placement is not null;
            ushort places = placeable ? (ushort)dto.Placement!.BlockId : (ushort)0;
            bool permanent = dto.Loadout?.Permanent == true;
            int? slot = dto.Loadout?.Slot;
            ItemIdentity identity = dto.Identity == "uniqueInstance"
                ? ItemIdentity.UniqueInstance
                : ItemIdentity.Stackable;
            var definition = new ItemDefinition(
                (ushort)dto.Id,
                dto.Key,
                dto.DisplayName,
                dto.MaxStack,
                placeable,
                places,
                permanent,
                slot,
                identity);
            byId[definition.Id] = definition;
            byKey[definition.Key] = definition.Id;
            if (definition.LoadoutSlot.HasValue)
            {
                loadout.Add(definition);
            }
        }

        _byId = byId;
        _byKey = byKey;
        _loadout = loadout.OrderBy(item => item.LoadoutSlot).ToArray();
    }

    public static bool TryGet(ushort id, out ItemDefinition definition)
    {
        ContentRuntime.EnsureLoaded();
        return _byId.TryGetValue(id, out definition);
    }

    public static int MaxStack(ushort itemId)
    {
        return TryGet(itemId, out ItemDefinition definition) ? definition.MaxStack : 1;
    }

    public static bool IsPermanent(ushort id)
    {
        return TryGet(id, out ItemDefinition definition) && definition.Permanent;
    }

    public static IReadOnlyList<ItemDefinition> LoadoutItems()
    {
        ContentRuntime.EnsureLoaded();
        return _loadout;
    }

    public static IReadOnlyDictionary<ushort, ItemDefinition> All
    {
        get
        {
            ContentRuntime.EnsureLoaded();
            return _byId;
        }
    }
}
