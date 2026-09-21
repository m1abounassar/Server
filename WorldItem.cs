namespace SandboxServer;

internal readonly struct WorldItemSnapshot
{
    public WorldItemSnapshot(int id, RewardKind kind, ushort typeId, int quantity, float x, float y)
    {
        Id = id;
        Kind = kind;
        TypeId = typeId;
        Quantity = quantity;
        X = x;
        Y = y;
    }

    public int Id { get; }
    public RewardKind Kind { get; }
    public ushort TypeId { get; }
    public int Quantity { get; }
    public float X { get; }
    public float Y { get; }
}

internal readonly struct WorldItemChange
{
    public WorldItemChange(WorldItemSnapshot item, bool removed)
    {
        Item = item;
        Removed = removed;
    }

    public WorldItemSnapshot Item { get; }
    public bool Removed { get; }

    public static WorldItemChange Upsert(WorldItemSnapshot item) => new(item, false);

    public static WorldItemChange Remove(int id) => new(new WorldItemSnapshot(id, RewardKind.Item, 0, 0, 0f, 0f), true);
}

internal readonly struct SlotDelta
{
    public SlotDelta(int slot, ushort itemId, int quantity)
    {
        Slot = slot;
        ItemId = itemId;
        Quantity = quantity;
    }

    public int Slot { get; }
    public ushort ItemId { get; }
    public int Quantity { get; }
}

internal readonly struct PickupNotice
{
    public PickupNotice(RewardKind kind, ushort typeId, int quantity)
    {
        Kind = kind;
        TypeId = typeId;
        Quantity = quantity;
    }

    public RewardKind Kind { get; }
    public ushort TypeId { get; }
    public int Quantity { get; }
}

internal readonly struct OwnerCredit
{
    public OwnerCredit(int playerId, SlotDelta[] slots, int? gemBalance, PickupNotice[] pickups)
    {
        PlayerId = playerId;
        Slots = slots;
        GemBalance = gemBalance;
        Pickups = pickups;
    }

    public int PlayerId { get; }
    public SlotDelta[] Slots { get; }
    public int? GemBalance { get; }
    public PickupNotice[] Pickups { get; }
}

internal sealed class WorldItem
{
    public WorldItem(int id, RewardKind kind, ushort typeId, int quantity, float x, float y)
    {
        Id = id;
        Kind = kind;
        TypeId = typeId;
        Quantity = quantity;
        X = x;
        Y = y;
    }

    public int Id { get; }
    public RewardKind Kind { get; }
    public ushort TypeId { get; }
    public int Quantity { get; set; }
    public float X { get; }
    public float Y { get; }

    public WorldItemSnapshot Snapshot() => new(Id, Kind, TypeId, Quantity, X, Y);
}
