using Xunit;

namespace SandboxServer.Tests;

public sealed class PlacementTests
{
    [Fact]
    public void PlacesForegroundDirtFromHoleyBagSlot()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        store.TryAddItem(1, ItemId.DirtSeed, 1, out _);
        store.TryAddItem(1, ItemId.GrassSeed, 1, out _);
        store.TryAddItem(1, ItemId.StoneSeed, 1, out _);
        store.TryAddItem(1, ItemId.DirtBlock, 2, out _);
        int dirtSlot = ItemConfig.FirstCollectibleSlot + 3;
        Assert.True(store.TrySelect(1, dirtSlot));

        (int x, int y) cell = SpawnCell(self);
        EditResult placed = world.TryPlaceSelected(1, cell.x, cell.y, store);
        Assert.Equal(EditStatus.Ok, placed.Status);
        Assert.Equal(BlockId.Dirt, placed.Cell.Foreground);
        Assert.Equal(3, placed.Cell.ForegroundHealth);
        Assert.Equal(dirtSlot, Assert.Single(placed.OwnerSlots).Slot);
        Assert.Equal(1, placed.OwnerSlots[0].Quantity);

        SlotDelta[] items = Collectibles(store, 1);
        Assert.Equal(4, items.Length);
        Assert.Equal(ItemId.DirtSeed, items[0].ItemId);
        Assert.Equal(ItemId.GrassSeed, items[1].ItemId);
        Assert.Equal(ItemId.StoneSeed, items[2].ItemId);
        Assert.Equal(dirtSlot, items[3].Slot);
        Assert.Equal(ItemId.DirtBlock, items[3].ItemId);
        Assert.Equal(dirtSlot, store.SelectedSlot(1));
    }

    [Fact]
    public void EmptyCollectibleCannotBeSelected()
    {
        var store = new PlayerStore();
        store.Ensure(1);
        Assert.False(store.TrySelect(1, 5, out string? error));
        Assert.Equal("empty_slot", error);
        Assert.Equal(ItemConfig.FistSlot, store.SelectedSlot(1));
        Assert.True(store.TrySelect(1, ItemConfig.WrenchSlot));
        Assert.Equal(ItemConfig.WrenchSlot, store.SelectedSlot(1));
    }

    [Fact]
    public void FistAndWrenchAreUnplaceable()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        (int x, int y) cell = SpawnCell(self);

        EditResult fist = world.TryPlaceSelected(1, cell.x, cell.y, store);
        Assert.Equal(EditStatus.Unplaceable, fist.Status);
        Assert.True(store.TrySelect(1, ItemConfig.WrenchSlot));
        EditResult wrench = world.TryPlaceSelected(1, cell.x, cell.y, store);
        Assert.Equal(EditStatus.Unplaceable, wrench.Status);
        Assert.Equal(2, store.OccupiedSlots(1).Length);
    }

    [Fact]
    public void SeedIsNotPlaceable()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        store.TryAddItem(1, ItemId.DirtSeed, 1, out _);
        Assert.True(store.TrySelect(1, ItemConfig.FirstCollectibleSlot));
        EditResult result = world.TryPlaceSelected(1, SpawnCell(self).x, SpawnCell(self).y, store);
        Assert.Equal(EditStatus.Unplaceable, result.Status);
        Assert.Single(Collectibles(store, 1));
    }

    [Fact]
    public void OccupiedAndOutOfRangeFailWithoutConsume()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        store.TryAddItem(1, ItemId.DirtBlock, 2, out _);
        Assert.True(store.TrySelect(1, ItemConfig.FirstCollectibleSlot));
        (int x, int y) spawn = SpawnCell(self);

        EditResult occupied = world.TryPlaceSelected(1, spawn.x, spawn.y - 1, store);
        Assert.Equal(EditStatus.Occupied, occupied.Status);

        EditResult far = world.TryPlaceSelected(1, spawn.x + 20, spawn.y, store);
        Assert.Equal(EditStatus.OutOfRange, far.Status);

        Assert.Equal(2, Collectibles(store, 1)[0].Quantity);
    }

    [Fact]
    public void ConsumeLastItemSelectsFist()
    {
        var world = new World("START");
        var store = new PlayerStore();
        store.Ensure(1);
        PlayerSnapshot self = Enter(world, 1, "Alice");
        store.TryAddItem(1, ItemId.DirtSeed, 1, out _);
        store.TryAddItem(1, ItemId.DirtBlock, 1, out _);
        int dirtSlot = ItemConfig.FirstCollectibleSlot + 1;
        Assert.True(store.TrySelect(1, dirtSlot));
        (int x, int y) cell = SpawnCell(self);

        EditResult first = world.TryPlaceSelected(1, cell.x, cell.y, store);
        Assert.Equal(EditStatus.Ok, first.Status);
        Assert.Equal(0, first.OwnerSlots[0].Quantity);
        Assert.Equal(ItemConfig.FistSlot, store.SelectedSlot(1));
        Assert.Single(Collectibles(store, 1));
        Assert.Equal(ItemConfig.FirstCollectibleSlot, Collectibles(store, 1)[0].Slot);

        EditResult second = world.TryPlaceSelected(1, cell.x, cell.y + 1, store);
        Assert.Equal(EditStatus.Unplaceable, second.Status);
        Assert.Equal(ItemConfig.FistSlot, store.SelectedSlot(1));
        Assert.Single(Collectibles(store, 1));
    }

    [Fact]
    public void ProtocolPlaceNoLongerAcceptsLayerAndBlockId()
    {
        Assert.False(Protocol.TryParseClientLine("PLACE 1 2 fg 2", out _, out string? error));
        Assert.Equal("invalid_command", error);
        Assert.True(Protocol.TryParseClientLine("PLACE 1 2", out ClientCommand? command, out _));
        PlaceCommand place = Assert.IsType<PlaceCommand>(command);
        Assert.Equal(1, place.X);
        Assert.Equal(2, place.Y);
        Assert.True(Protocol.TryParseClientLine("SELECT 3", out command, out _));
        Assert.Equal(3, Assert.IsType<SelectCommand>(command).Slot);
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
