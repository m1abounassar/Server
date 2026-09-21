using Xunit;

namespace SandboxServer.Tests;

public sealed class WorldItemTests
{
    [Fact]
    public void DestroyingDirtSpawnsSeedBlockAndGemInInset()
    {
        var world = new World("START");
        PlayerSnapshot self = Enter(world, 1, "Alice");
        (int x, int y) dirt = DirtCell(self);

        Assert.Equal(EditStatus.Ok, world.TryBreak(1, dirt.x, dirt.y).Status);
        Assert.Equal(EditStatus.Ok, world.TryBreak(1, dirt.x, dirt.y).Status);
        EditResult destroyed = world.TryBreak(1, dirt.x, dirt.y);

        Assert.Equal(EditStatus.Ok, destroyed.Status);
        Assert.Equal(0, destroyed.Cell.Foreground);
        Assert.Equal(3, destroyed.ItemChanges.Length);
        Assert.Contains(destroyed.ItemChanges, c => c.Item.Kind == RewardKind.Item && c.Item.TypeId == ItemId.DirtSeed);
        Assert.Contains(destroyed.ItemChanges, c => c.Item.Kind == RewardKind.Item && c.Item.TypeId == ItemId.DirtBlock);
        Assert.Contains(destroyed.ItemChanges, c => c.Item.Kind == RewardKind.Currency && c.Item.TypeId == CurrencyId.Gems);

        foreach (WorldItemChange change in destroyed.ItemChanges)
        {
            Assert.InRange(change.Item.X, dirt.x + ItemConfig.DropInsetMin, dirt.x + ItemConfig.DropInsetMax);
            Assert.InRange(change.Item.Y, dirt.y + ItemConfig.DropInsetMin, dirt.y + ItemConfig.DropInsetMax);
        }
    }

    [Fact]
    public void NearbyDirtBlocksMergeKeepingSurvivorIdAndPosition()
    {
        var world = new World("MERGE");
        WorldItemChange[] first = world.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, 10, 10);
        Assert.Single(first);
        WorldItemSnapshot survivor = first[0].Item;

        WorldItemChange[] second = world.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, 10, 10);
        Assert.Single(second);
        Assert.Equal(survivor.Id, second[0].Item.Id);
        Assert.Equal(2, second[0].Item.Quantity);
        Assert.Equal(survivor.X, second[0].Item.X);
        Assert.Equal(survivor.Y, second[0].Item.Y);

        WorldItemSnapshot[] items = world.SnapshotItems();
        Assert.Single(items);
        Assert.Equal(2, items[0].Quantity);
    }

    [Fact]
    public void NearbyGemsAddValues()
    {
        var world = new World("GEMS");
        world.InsertRewards(new[] { Reward.Currency(CurrencyId.Gems, 1) }, 4, 4);
        WorldItemChange[] merged = world.InsertRewards(new[] { Reward.Currency(CurrencyId.Gems, 1) }, 4, 4);
        Assert.Single(merged);
        Assert.Equal(2, merged[0].Item.Quantity);

        world.InsertRewards(new[] { Reward.Currency(CurrencyId.Gems, 5) }, 20, 20);
        WorldItemChange[] stacked = world.InsertRewards(new[] { Reward.Currency(CurrencyId.Gems, 10) }, 20, 20);
        Assert.Single(stacked);
        Assert.Equal(15, stacked[0].Item.Quantity);
    }

    [Fact]
    public void MaxStackLeftoverCreatesSecondEntity()
    {
        var world = new World("STACK");
        WorldItemChange[] split = world.InsertRewards(
            new[] { Reward.Item(ItemId.DirtBlock, ItemConfig.DefaultMaxStack + 5) },
            8,
            8);
        Assert.Equal(2, split.Length);
        Assert.Contains(split, change => change.Item.Quantity == ItemConfig.DefaultMaxStack);
        Assert.Contains(split, change => change.Item.Quantity == 5);

        WorldItemSnapshot[] items = world.SnapshotItems();
        Assert.Equal(2, items.Length);
        Assert.Equal(items[0].X, items[1].X);
        Assert.Equal(items[0].Y, items[1].Y);
        Assert.NotEqual(items[0].Id, items[1].Id);
    }

    [Fact]
    public void DistancesBeyondRadiusDoNotMerge()
    {
        var world = new World("FAR");
        world.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, 10, 10);
        world.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, 12, 10);
        WorldItemSnapshot[] items = world.SnapshotItems();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal(1, item.Quantity));
    }

    [Fact]
    public void SeedDoesNotMergeWithBlock()
    {
        var world = new World("KINDS");
        world.InsertRewards(new[] { Reward.Item(ItemId.DirtSeed) }, 6, 6);
        world.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, 6, 6);
        world.InsertRewards(new[] { Reward.Currency(CurrencyId.Gems) }, 6, 6);
        Assert.Equal(3, world.SnapshotItems().Length);
    }

    [Fact]
    public void WorldsIsolateItemEntities()
    {
        var start = new World("START");
        var test = new World("TEST");
        start.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, 3, 3);
        Assert.Single(start.SnapshotItems());
        Assert.Empty(test.SnapshotItems());
    }

    private static PlayerSnapshot Enter(World world, int playerId, string name)
    {
        EnterResult enter = world.TryAddMember(playerId, name);
        Assert.Equal(EnterStatus.Ok, enter.Status);
        return enter.Self;
    }

    private static (int x, int y) DirtCell(PlayerSnapshot self)
    {
        return ((int)MathF.Floor(self.X), (int)MathF.Floor(self.Y) - 2);
    }
}
