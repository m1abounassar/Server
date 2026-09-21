using Xunit;

namespace SandboxServer.Tests;

public sealed class WorldGenerationTests
{
    [Fact]
    public void DefaultWorldHasEmptyAirDirtDirtWallAndBedrock()
    {
        var world = new World("START");
        Assert.Equal(0, world.Size.OriginX);
        Assert.Equal(-8, world.Size.OriginY);
        Assert.Equal(100, world.Size.Width);
        Assert.Equal(68, world.Size.Height);
        Assert.Equal(59, world.Size.MaxY);

        GridSnapshot grid = world.CopyGridSnapshot();
        Assert.DoesNotContain(grid.Cells, cell => cell.Background == BlockId.Sky || cell.Foreground == BlockId.Sky);
        Assert.DoesNotContain(grid.Cells, cell => cell.Background == BlockId.Dirt);
        Assert.DoesNotContain(grid.Cells, cell => cell.Foreground == BlockId.DirtWall);
        Assert.DoesNotContain(grid.Cells, cell => cell.Foreground == BlockId.Grass);

        GridCellSnapshot air = Cell(grid, 50, 50);
        Assert.Equal(BlockId.Empty, air.Background);
        Assert.Equal(BlockId.Empty, air.Foreground);
        Assert.Equal(BlockId.Empty, air.HyperForeground);

        GridCellSnapshot surface = Cell(grid, 50, WorldGenerator.SurfaceY);
        Assert.Equal(BlockId.DirtWall, surface.Background);
        Assert.Equal(BlockId.Dirt, surface.Foreground);

        GridCellSnapshot dirt = Cell(grid, 50, 0);
        Assert.Equal(BlockId.DirtWall, dirt.Background);
        Assert.Equal(BlockId.Dirt, dirt.Foreground);

        GridCellSnapshot bedrockTop = Cell(grid, 50, -1);
        Assert.Equal(BlockId.DirtWall, bedrockTop.Background);
        Assert.Equal(BlockId.Bedrock, bedrockTop.Foreground);

        GridCellSnapshot bedrockBottom = Cell(grid, 50, -8);
        Assert.Equal(BlockId.DirtWall, bedrockBottom.Background);
        Assert.Equal(BlockId.Bedrock, bedrockBottom.Foreground);
        Assert.True(BlockCatalog.IsSolid(BlockId.Bedrock));
        Assert.False(BlockCatalog.IsSolid(BlockId.DirtWall));
    }

    [Fact]
    public void EmptyAirDoesNotAppearInSparseSnapshot()
    {
        var world = new World("START");
        GridSnapshot grid = world.CopyGridSnapshot();
        Assert.DoesNotContain(grid.Cells, cell => cell.Y > WorldGenerator.SurfaceY);
    }

    [Fact]
    public void OpenAirHasNoDirtWall()
    {
        var world = new World("START");
        GridSnapshot grid = world.CopyGridSnapshot();
        Assert.DoesNotContain(grid.Cells, cell => cell.Y > WorldGenerator.SurfaceY && cell.Background == BlockId.DirtWall);
    }

    [Fact]
    public void PunchingAirAtSpawnIsEmptyCell()
    {
        var world = new World("START");
        EnterResult enter = world.TryAddMember(1, "Alice");
        Assert.Equal(EnterStatus.Ok, enter.Status);
        (int x, int y) spawn = ((int)MathF.Floor(enter.Self.X), (int)MathF.Floor(enter.Self.Y));
        EditResult air = world.TryBreak(1, spawn.x, spawn.y);
        Assert.Equal(EditStatus.EmptyCell, air.Status);
    }

    [Fact]
    public void BedrockHasNoDropsAndCannotBeDamagedFromSpawnRange()
    {
        Assert.Empty(DropCatalog.Resolve(BlockId.Bedrock, new ScriptedChance()));
        Assert.True(BlockCatalog.TryGet(BlockId.Bedrock, out BlockDefinition definition));
        Assert.False(definition.Breakable);
        Assert.True(definition.Solid);
        Assert.False(BlockCatalog.TryGet(BlockId.Sky, out _));
    }

    [Fact]
    public void WorldsShareLayoutButRemainIsolated()
    {
        var start = new World("START");
        var test = new World("TEST");
        Assert.Equal(start.Size.OriginY, test.Size.OriginY);
        Assert.Equal(Cell(start.CopyGridSnapshot(), 10, 40).Foreground, Cell(test.CopyGridSnapshot(), 10, 40).Foreground);
        start.InsertRewards(new[] { Reward.Item(ItemId.DirtBlock) }, 3, 41);
        Assert.Single(start.SnapshotItems());
        Assert.Empty(test.SnapshotItems());
    }

    [Fact]
    public void MovementClampUsesOriginYNotZero()
    {
        var size = WorldSize.Default;
        var state = new MovementState { X = 50.5f, Y = -20f, Vy = -4f };
        PlayerMovement.Integrate(ref state, new MovementInput(false, false, false), MovementConfig.Dt, size, (_, _) => false);
        Assert.Equal(size.OriginY, state.Y, 3);
    }

    [Fact]
    public void SpawnStandsOnSurface()
    {
        var world = new World("START");
        EnterResult enter = world.TryAddMember(1, "Alice");
        Assert.Equal(50, (int)MathF.Floor(enter.Self.X));
        Assert.Equal(WorldGenerator.SurfaceY + 1, (int)MathF.Floor(enter.Self.Y));
    }

    private static GridCellSnapshot Cell(GridSnapshot grid, int x, int y)
    {
        GridCellSnapshot match = Array.Find(grid.Cells, cell => cell.X == x && cell.Y == y);
        if (match.X != x || match.Y != y)
        {
            return new GridCellSnapshot(x, y, 0, 0, 0, 0, 0, 0);
        }

        return match;
    }
}
