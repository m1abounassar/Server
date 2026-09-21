using Xunit;

namespace SandboxServer.Tests;

public sealed class DropCatalogTests
{
    [Fact]
    public void ChanceOneAlwaysDropsIndependentEntries()
    {
        var chance = new ScriptedChance(0.0, 0.0, 0.0);
        Reward[] rewards = DropCatalog.Resolve(BlockId.Dirt, chance);
        Assert.Equal(3, rewards.Length);
        Assert.Contains(rewards, r => r.Kind == RewardKind.Item && r.TypeId == ItemId.DirtSeed && r.Quantity == 1);
        Assert.Contains(rewards, r => r.Kind == RewardKind.Item && r.TypeId == ItemId.DirtBlock && r.Quantity == 1);
        Assert.Contains(rewards, r => r.Kind == RewardKind.Currency && r.TypeId == CurrencyId.Gems && r.Quantity == 1);
    }

    [Fact]
    public void ChanceZeroNeverDrops()
    {
        DropEntry[] entries =
        [
            new DropEntry(RewardKind.Item, ItemId.DirtBlock, chance: 0f, minQuantity: 1, maxQuantity: 1),
            new DropEntry(RewardKind.Currency, CurrencyId.Gems, chance: 0f, minQuantity: 1, maxQuantity: 1)
        ];
        Reward[] rewards = DropCatalog.Resolve(entries, new ScriptedChance(0.0, 0.0));
        Assert.Empty(rewards);
    }

    [Fact]
    public void IndependentRollsCanProduceSubset()
    {
        DropEntry[] entries =
        [
            new DropEntry(RewardKind.Item, ItemId.DirtSeed, 1f, 1, 1),
            new DropEntry(RewardKind.Item, ItemId.DirtBlock, 0.5f, 1, 1),
            new DropEntry(RewardKind.Currency, CurrencyId.Gems, 0.5f, 1, 1)
        ];
        Reward[] rewards = DropCatalog.Resolve(entries, new ScriptedChance(0.0, 0.9, 0.1));
        Assert.Equal(2, rewards.Length);
        Assert.Equal(ItemId.DirtSeed, rewards[0].TypeId);
        Assert.Equal(RewardKind.Currency, rewards[1].Kind);
        Assert.Equal(CurrencyId.Gems, rewards[1].TypeId);
    }

    [Fact]
    public void QuantityRangeUsesInjectedRng()
    {
        DropEntry[] entries =
        [
            new DropEntry(RewardKind.Item, ItemId.DirtBlock, 1f, minQuantity: 2, maxQuantity: 5)
        ];
        Reward[] low = DropCatalog.Resolve(entries, new ScriptedChance(0.0, 0.0));
        Reward[] high = DropCatalog.Resolve(entries, new ScriptedChance(0.0, 0.999));
        Assert.Equal(2, low[0].Quantity);
        Assert.Equal(5, high[0].Quantity);
    }

    [Fact]
    public void EmptyTableProducesNothing()
    {
        Assert.Empty(DropCatalog.Resolve(BlockId.Bedrock, new ScriptedChance()));
        Assert.Empty(DropCatalog.Resolve(BlockId.Deco, new ScriptedChance()));
    }

    [Fact]
    public void MarkerHasCustomFourthEntry()
    {
        Reward[] rewards = DropCatalog.Resolve(BlockId.Marker, new ScriptedChance(0, 0, 0, 0));
        Assert.Equal(4, rewards.Length);
        Assert.Contains(rewards, r => r.Kind == RewardKind.Item && r.TypeId == ItemId.TestRare);
    }

    [Fact]
    public void GemRewardIsCurrencyNotItem()
    {
        Reward gem = Reward.Currency(CurrencyId.Gems, 1);
        Assert.Equal(RewardKind.Currency, gem.Kind);
        Assert.Equal(CurrencyId.Gems, gem.TypeId);
        Assert.True(CurrencyCatalog.TryGet(gem.TypeId, out _));
    }

    [Fact]
    public void DirtBlockPlacesForegroundDirt()
    {
        Assert.True(ItemCatalog.TryGet(ItemId.DirtBlock, out ItemDefinition item));
        Assert.True(item.Placeable);
        Assert.Equal(BlockId.Dirt, item.PlacesBlockId);
        Assert.True(BlockCatalog.TryGet(BlockId.Dirt, out BlockDefinition block));
        Assert.Equal(TileLayer.Foreground, block.Layer);
        Assert.False(ItemCatalog.TryGet(ItemId.DirtSeed, out ItemDefinition seed) && seed.Placeable);
    }
}
