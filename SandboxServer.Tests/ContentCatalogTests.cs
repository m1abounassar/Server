using Xunit;

namespace SandboxServer.Tests;

public sealed class ContentCatalogTests
{
    [Fact]
    public void PackLoadsWithUniqueIdsAndReservedZero()
    {
        ContentRuntime.EnsureLoaded();
        Assert.Equal(1, ContentRuntime.SchemaVersion);
        Assert.True(ContentRuntime.ContentRevision >= 1);
        Assert.False(BlockCatalog.TryGet(0, out _));
        Assert.False(ItemCatalog.TryGet(0, out _));
        Assert.False(CurrencyCatalog.TryGet(0, out _));
        Assert.False(BlockCatalog.TryGet(BlockId.Sky, out _));
        Assert.True(BlockCatalog.TryGetIncludingRetired(BlockId.Sky, out BlockDefinition sky));
        Assert.True(sky.Retired);
    }

    [Fact]
    public void PlaceableItemsPointAtLiveBlocks()
    {
        foreach (ItemDefinition item in ItemCatalog.All.Values)
        {
            if (!item.Placeable)
            {
                continue;
            }

            Assert.True(BlockCatalog.TryGet(item.PlacesBlockId, out BlockDefinition block));
            Assert.True(block.Placeable);
            Assert.False(block.Retired);
            Assert.True(
                block.Layer is TileLayer.Background or TileLayer.Foreground or TileLayer.HyperForeground);
        }
    }

    [Fact]
    public void DropRewardsExistAndExcludePermanentTools()
    {
        foreach (BlockDefinition block in BlockCatalog.All.Values)
        {
            if (block.Retired)
            {
                continue;
            }

            foreach (DropEntry entry in DropCatalog.EntriesFor(block.Id))
            {
                Assert.InRange(entry.Chance, 0f, 1f);
                Assert.True(entry.MinQuantity >= 1);
                Assert.True(entry.MaxQuantity >= entry.MinQuantity);
                if (entry.Kind == RewardKind.Item)
                {
                    Assert.True(ItemCatalog.TryGet(entry.TypeId, out ItemDefinition item));
                    Assert.False(item.Permanent);
                }
                else
                {
                    Assert.True(CurrencyCatalog.TryGet(entry.TypeId, out _));
                }
            }
        }
    }

    [Fact]
    public void LoadoutSlotsAreUnique()
    {
        IReadOnlyList<ItemDefinition> loadout = ItemCatalog.LoadoutItems();
        Assert.Equal(2, loadout.Count);
        Assert.Equal(ItemConfig.FistSlot, loadout[0].LoadoutSlot);
        Assert.Equal(ItemId.Fist, loadout[0].Id);
        Assert.Equal(ItemId.Wrench, loadout[1].Id);
        Assert.True(loadout[0].Permanent);
        Assert.True(loadout[1].Permanent);
        Assert.Equal(loadout.Select(item => item.LoadoutSlot).Distinct().Count(), loadout.Count);
    }

    [Fact]
    public void LookupIsDensityIndependent()
    {
        Assert.True(BlockCatalog.TryGet(BlockId.Bedrock, out _));
        Assert.False(BlockCatalog.TryGet(50000, out _));
        Assert.False(ItemCatalog.TryGet(40000, out _));
        Assert.False(CurrencyCatalog.TryGet(9, out _));
    }

    [Fact]
    public void DirtBlockStillPlacesForegroundDirt()
    {
        Assert.True(ItemCatalog.TryGet(ItemId.DirtBlock, out ItemDefinition item));
        Assert.True(item.Placeable);
        Assert.Equal(BlockId.Dirt, item.PlacesBlockId);
        Assert.True(BlockCatalog.TryGet(BlockId.Dirt, out BlockDefinition block));
        Assert.Equal(TileLayer.Foreground, block.Layer);
        Assert.Equal(ItemIdentity.Stackable, item.Identity);
        Assert.InRange(item.MaxStack, 1, int.MaxValue);
        Assert.InRange(block.MaxHealth, (byte)1, byte.MaxValue);
    }

    [Fact]
    public void DirtWallIsBackgroundOnly()
    {
        Assert.True(BlockCatalog.TryGet(BlockId.DirtWall, out BlockDefinition wall));
        Assert.Equal(TileLayer.Background, wall.Layer);
        Assert.False(wall.Solid);
        Assert.True(BlockCatalog.TryGet(BlockId.Dirt, out BlockDefinition dirt));
        Assert.Equal(TileLayer.Foreground, dirt.Layer);
    }

    [Fact]
    public void UnknownItemCannotBeAdded()
    {
        var store = new PlayerStore();
        store.Ensure(1);
        int leftover = store.TryAddItem(1, 65000, 1, out SlotDelta[] changes);
        Assert.Equal(1, leftover);
        Assert.Empty(changes);
    }

    [Fact]
    public void ValidatorRejectsDuplicateIdsReservedZeroAndBadPlacement()
    {
        ContentPack pack = ContentPackReader.Read(ContentPaths.Resolve());
        pack.Items.Add(new ItemDto
        {
            Id = ItemId.DirtBlock,
            Key = "duplicate_dirt",
            DisplayName = "Dup",
            MaxStack = 1,
            Identity = "stackable"
        });
        Assert.Contains(ContentValidator.Validate(pack), error => error.Contains("Duplicate item id", StringComparison.Ordinal));

        pack = ContentPackReader.Read(ContentPaths.Resolve());
        pack.Items[0].Id = 0;
        Assert.Contains(ContentValidator.Validate(pack), error => error.Contains("0 is reserved", StringComparison.Ordinal));

        pack = ContentPackReader.Read(ContentPaths.Resolve());
        pack.Items.Add(new ItemDto
        {
            Id = 40,
            Key = "bedrock_block",
            DisplayName = "Bedrock Block",
            MaxStack = 1,
            Identity = "stackable",
            Placement = new PlacementDto { BlockId = BlockId.Bedrock }
        });
        Assert.Contains(ContentValidator.Validate(pack), error => error.Contains("non-placeable", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsRetiredReferencesAndPermanentDrops()
    {
        ContentPack pack = ContentPackReader.Read(ContentPaths.Resolve());
        pack.Items.Add(new ItemDto
        {
            Id = 41,
            Key = "sky_block",
            DisplayName = "Sky Block",
            MaxStack = 1,
            Identity = "stackable",
            Placement = new PlacementDto { BlockId = BlockId.Sky }
        });
        Assert.Contains(ContentValidator.Validate(pack), error => error.Contains("missing or retired", StringComparison.Ordinal));

        pack = ContentPackReader.Read(ContentPaths.Resolve());
        pack.Drops.Add(new DropTableDto
        {
            BlockId = BlockId.Dirt,
            Rolls = [new DropRollDto { Kind = "item", TypeId = ItemId.Fist, Chance = 1f, MinQuantity = 1, MaxQuantity = 1 }]
        });
        Assert.Contains(
            ContentValidator.Validate(pack),
            error => error.Contains("cannot appear in drop tables", StringComparison.Ordinal)
                || error.Contains("Duplicate drop table", StringComparison.Ordinal));

        pack = ContentPackReader.Read(ContentPaths.Resolve());
        DropTableDto dirt = pack.Drops.First(table => table.BlockId == BlockId.Dirt);
        dirt.Rolls.Add(new DropRollDto { Kind = "item", TypeId = ItemId.Fist, Chance = 1f, MinQuantity = 1, MaxQuantity = 1 });
        Assert.Contains(ContentValidator.Validate(pack), error => error.Contains("cannot appear in drop tables", StringComparison.Ordinal));
    }

    [Fact]
    public void RealPackValidates()
    {
        ContentPack pack = ContentPackReader.Read(ContentPaths.Resolve());
        Assert.Empty(ContentValidator.Validate(pack));
    }
}
