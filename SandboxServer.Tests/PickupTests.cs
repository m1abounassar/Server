using Xunit;

namespace SandboxServer.Tests;

public sealed class PickupTests
{
    [Fact]
    public void OverlapPickupFillsFirstCollectibleThenNextTypeUsesNextSlot()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        (int x, int y) cell = SpawnCell(self);

        world.InsertRewards(new[] { Reward.Item(ItemId.DirtSeed) }, cell.x, cell.y);
        WorldTickResult first = world.Tick(MovementConfig.Dt, store);
        Assert.Contains(first.ItemChanges, c => c.Removed);
        SlotDelta[] items = Collectibles(store, 1);
        Assert.Single(items);
        Assert.Equal(ItemConfig.FirstCollectibleSlot, items[0].Slot);
        Assert.Equal(ItemId.DirtSeed, items[0].ItemId);

        world.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, cell.x, cell.y);
        world.Tick(MovementConfig.Dt, store);
        items = Collectibles(store, 1);
        Assert.Equal(2, items.Length);
        Assert.Equal(ItemId.DirtSeed, items[0].ItemId);
        Assert.Equal(ItemConfig.FirstCollectibleSlot + 1, items[1].Slot);
        Assert.Equal(ItemId.DirtBlock, items[1].ItemId);
    }

    [Fact]
    public void EmptyingFirstCollectibleDoesNotMoveLaterSlot()
    {
        var store = new PlayerStore();
        store.Ensure(1);
        store.TryAddItem(1, ItemId.DirtSeed, 1, out _);
        store.TryAddItem(1, ItemId.DirtBlock, 1, out _);
        Assert.True(store.TryConsumeSlot(1, ItemConfig.FirstCollectibleSlot, out SlotDelta cleared));
        Assert.Equal(ItemConfig.FirstCollectibleSlot, cleared.Slot);
        Assert.Equal(0, cleared.Quantity);

        SlotDelta[] items = Collectibles(store, 1);
        Assert.Single(items);
        Assert.Equal(ItemConfig.FirstCollectibleSlot + 1, items[0].Slot);
        Assert.Equal(ItemId.DirtBlock, items[0].ItemId);
        Assert.Equal(ItemConfig.FistSlot, store.SelectedSlot(1));
    }

    [Fact]
    public void PartialPickupLeavesSameEntityId()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        for (int i = 0; i < ItemConfig.CollectibleSlotCount - 1; i++)
        {
            Assert.Equal(0, store.TryAddItem(1, ItemId.DirtBlock, ItemConfig.DefaultMaxStack, out _));
        }

        Assert.Equal(0, store.TryAddItem(1, ItemId.DirtBlock, ItemConfig.DefaultMaxStack - 3, out _));

        WorldItemChange[] spawned = world.InsertRewards(
            new[] { Reward.Item(ItemId.DirtBlock, 10) },
            SpawnCell(self).x,
            SpawnCell(self).y);
        int entityId = spawned[0].Item.Id;

        WorldTickResult tick = world.Tick(MovementConfig.Dt, store);
        WorldItemChange leftover = Assert.Single(tick.ItemChanges);
        Assert.False(leftover.Removed);
        Assert.Equal(entityId, leftover.Item.Id);
        Assert.Equal(7, leftover.Item.Quantity);
        PickupNotice notice = Assert.Single(Assert.Single(tick.Credits).Pickups);
        Assert.Equal(3, notice.Quantity);

        SlotDelta last = Collectibles(store, 1)[ItemConfig.CollectibleSlotCount - 1];
        Assert.Equal(ItemConfig.DefaultMaxStack, last.Quantity);
    }

    [Fact]
    public void TwoOccupantsSameTickLowestPlayerIdWins()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(10);
        store.Ensure(20);
        PlayerSnapshot first = Enter(world, 20, "Bob");
        Enter(world, 10, "Alice");
        (int x, int y) cell = SpawnCell(first);
        world.InsertRewards(new[] { Reward.Item(ItemId.DirtSeed) }, cell.x, cell.y);

        world.Tick(MovementConfig.Dt, store);
        Assert.Single(Collectibles(store, 10));
        Assert.Empty(Collectibles(store, 20));
        Assert.Empty(world.SnapshotItems());
    }

    [Fact]
    public void GemPickupCreditsEntireMergedQuantity()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        (int x, int y) cell = SpawnCell(self);
        world.InsertRewards(new[] { Reward.Currency(CurrencyId.Gems, 5) }, cell.x, cell.y);
        world.InsertRewards(new[] { Reward.Currency(CurrencyId.Gems, 10) }, cell.x, cell.y);
        Assert.Equal(15, Assert.Single(world.SnapshotItems()).Quantity);

        WorldTickResult tick = world.Tick(MovementConfig.Dt, store);
        Assert.True(Assert.Single(tick.ItemChanges).Removed);
        Assert.Equal(15, store.GemBalance(1));
        OwnerCredit credit = Assert.Single(tick.Credits);
        Assert.Equal(15, credit.GemBalance);
        PickupNotice notice = Assert.Single(credit.Pickups);
        Assert.Equal(RewardKind.Currency, notice.Kind);
        Assert.Equal(15, notice.Quantity);
        Assert.Equal("COLLECTED gem 15", Protocol.CollectedGem(notice.Quantity));
    }

    [Fact]
    public void LeaveWorldKeepsSlots()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        world.InsertRewards(new[] { Reward.Item(ItemId.GrassSeed) }, SpawnCell(self).x, SpawnCell(self).y);
        world.Tick(MovementConfig.Dt, store);
        world.TryRemoveMember(1);

        SlotDelta[] items = Collectibles(store, 1);
        Assert.Single(items);
        Assert.Equal(ItemId.GrassSeed, items[0].ItemId);
        Assert.Equal(ItemConfig.FistSlot, store.SelectedSlot(1));
    }

    [Fact]
    public void OtherWorldPlayerDoesNotReceiveThisWorldPickup()
    {
        var start = new World("START");
        var test = new World("TEST");
        var store = new PlayerStore();
        store.Ensure(1);
        store.Ensure(2);
        PlayerSnapshot alice = Enter(start, 1, "Alice");
        Enter(test, 2, "Charlie");
        start.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, SpawnCell(alice).x, SpawnCell(alice).y);
        start.Tick(MovementConfig.Dt, store);
        test.Tick(MovementConfig.Dt, store);

        Assert.Single(Collectibles(store, 1));
        Assert.Empty(Collectibles(store, 2));
        Assert.Empty(start.SnapshotItems());
        Assert.Empty(test.SnapshotItems());
    }

    [Fact]
    public void FullInventoryLeavesWorldEntity()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        for (int i = 0; i < ItemConfig.CollectibleSlotCount; i++)
        {
            Assert.Equal(0, store.TryAddItem(1, ItemId.DirtBlock, ItemConfig.DefaultMaxStack, out _));
        }

        WorldItemChange[] spawned = world.InsertRewards(
            new[] { Reward.Item(ItemId.DirtBlock, 4) },
            SpawnCell(self).x,
            SpawnCell(self).y);
        WorldTickResult tick = world.Tick(MovementConfig.Dt, store);
        WorldItemSnapshot leftover = Assert.Single(world.SnapshotItems());
        Assert.Equal(spawned[0].Item.Id, leftover.Id);
        Assert.Equal(4, leftover.Quantity);
        Assert.Empty(tick.Credits);
    }

    [Fact]
    public void MatchingStackThenFillHoles()
    {
        var store = new PlayerStore();
        store.Ensure(1);
        store.TryAddItem(1, ItemId.DirtSeed, 1, out _);
        store.TryAddItem(1, ItemId.DirtBlock, 1, out _);
        store.TryConsumeSlot(1, ItemConfig.FirstCollectibleSlot, out _);
        int leftover = store.TryAddItem(1, ItemId.DirtBlock, 1, out SlotDelta[] changes);
        Assert.Equal(0, leftover);
        Assert.Single(changes);
        Assert.Equal(ItemConfig.FirstCollectibleSlot + 1, changes[0].Slot);
        Assert.Equal(2, changes[0].Quantity);

        leftover = store.TryAddItem(1, ItemId.GrassSeed, 1, out changes);
        Assert.Equal(0, leftover);
        Assert.Equal(ItemConfig.FirstCollectibleSlot, changes[0].Slot);
    }

    [Fact]
    public void PickupEmitsCollectedForTransferredQuantityOnly()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        world.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock, 4) }, SpawnCell(self).x, SpawnCell(self).y);
        WorldTickResult tick = world.Tick(MovementConfig.Dt, store);
        PickupNotice notice = Assert.Single(Assert.Single(tick.Credits).Pickups);
        Assert.Equal(RewardKind.Item, notice.Kind);
        Assert.Equal(ItemId.DirtBlock, notice.TypeId);
        Assert.Equal(4, notice.Quantity);
        Assert.Equal("COLLECTED item 1 4", Protocol.CollectedItem(notice.TypeId, notice.Quantity));
    }

    [Fact]
    public void ToolsAreReservedAndCannotBeSelectedEmpty()
    {
        var store = new PlayerStore();
        store.Ensure(1);
        SlotDelta[] occupied = store.OccupiedSlots(1);
        Assert.Equal(2, occupied.Length);
        Assert.Equal(ItemId.Fist, occupied[0].ItemId);
        Assert.Equal(ItemId.Wrench, occupied[1].ItemId);
        Assert.False(store.TrySelect(1, 5, out string? error));
        Assert.Equal("empty_slot", error);
        Assert.Equal(ItemConfig.FistSlot, store.SelectedSlot(1));
        Assert.False(store.TryConsumeSlot(1, ItemConfig.FistSlot, out _));
        Assert.Equal(1, store.TryAddItem(1, ItemId.Fist, 1, out SlotDelta[] added));
        Assert.Empty(added);
    }

    private static SlotDelta[] Collectibles(PlayerStore store, int playerId)
    {
        return store.OccupiedSlots(playerId)
            .Where(slot => slot.Slot >= ItemConfig.FirstCollectibleSlot)
            .ToArray();
    }

    private static PlayerSnapshot Enter(World world, int playerId, string name)
    {
        EnterResult enter = world.TryAddMember(playerId, name);
        Assert.Equal(EnterStatus.Ok, enter.Status);
        return enter.Self;
    }

    private static (int x, int y) SpawnCell(PlayerSnapshot self)
    {
        return ((int)MathF.Floor(self.X), (int)MathF.Floor(self.Y));
    }
}
