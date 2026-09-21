namespace SandboxServer;

internal enum RewardKind : byte
{
    Item = 0,
    Currency = 1
}

internal readonly struct Reward
{
    public Reward(RewardKind kind, ushort typeId, int quantity)
    {
        Kind = kind;
        TypeId = typeId;
        Quantity = quantity < 1 ? 1 : quantity;
    }

    public RewardKind Kind { get; }
    public ushort TypeId { get; }
    public int Quantity { get; }

    public static Reward Item(ushort itemId, int quantity = 1) => new(RewardKind.Item, itemId, quantity);

    public static Reward Currency(ushort currencyId, int quantity = 1) => new(RewardKind.Currency, currencyId, quantity);
}

internal readonly struct DropEntry
{
    public DropEntry(RewardKind kind, ushort typeId, float chance, int minQuantity, int maxQuantity)
    {
        Kind = kind;
        TypeId = typeId;
        Chance = chance;
        MinQuantity = minQuantity < 1 ? 1 : minQuantity;
        MaxQuantity = maxQuantity < MinQuantity ? MinQuantity : maxQuantity;
    }

    public RewardKind Kind { get; }
    public ushort TypeId { get; }
    public float Chance { get; }
    public int MinQuantity { get; }
    public int MaxQuantity { get; }
}

/// <summary>
/// Per-block drop tables loaded from the content pack. Independent rolls.
/// </summary>
internal static class DropCatalog
{
    private static Dictionary<ushort, DropEntry[]> _tables = new();

    internal static void Install(ContentPack pack)
    {
        var tables = new Dictionary<ushort, DropEntry[]>();
        foreach (DropTableDto table in pack.Drops)
        {
            DropEntry[] entries = table.Rolls.Select(roll => new DropEntry(
                roll.Kind == "currency" ? RewardKind.Currency : RewardKind.Item,
                (ushort)roll.TypeId,
                roll.Chance,
                roll.MinQuantity,
                roll.MaxQuantity)).ToArray();
            tables[(ushort)table.BlockId] = entries;
        }

        _tables = tables;
    }

    public static Reward[] Resolve(ushort blockId, IChance chance)
    {
        return Resolve(EntriesFor(blockId), chance);
    }

    public static Reward[] Resolve(IReadOnlyList<DropEntry> entries, IChance chance)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<Reward>();
        }

        var rewards = new List<Reward>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            DropEntry entry = entries[i];
            if (chance.NextDouble() >= entry.Chance)
            {
                continue;
            }

            int quantity = entry.MinQuantity;
            if (entry.MaxQuantity > entry.MinQuantity)
            {
                double t = chance.NextDouble();
                quantity = entry.MinQuantity + (int)(t * (entry.MaxQuantity - entry.MinQuantity + 1));
                if (quantity > entry.MaxQuantity)
                {
                    quantity = entry.MaxQuantity;
                }
            }

            rewards.Add(new Reward(entry.Kind, entry.TypeId, quantity));
        }

        return rewards.ToArray();
    }

    public static IReadOnlyList<DropEntry> EntriesFor(ushort blockId)
    {
        ContentRuntime.EnsureLoaded();
        return _tables.TryGetValue(blockId, out DropEntry[]? entries)
            ? entries
            : Array.Empty<DropEntry>();
    }
}
