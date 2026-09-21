namespace SandboxServer;

/// <summary>
/// Tiny deterministic default layout. Not a procedural-generation system.
/// Sky is not a block: air is empty on all three layers.
/// Dirt is foreground-only. Dirt Wall is background-only.
/// </summary>
internal static class WorldGenerator
{
    public const int SurfaceY = 40;
    public const int BedrockRows = 8;

    public static void FillDefault(WorldGrid grid, string worldName, out int spawnX, out int spawnY)
    {
        _ = worldName;
        ushort dirt = BlockCatalog.RequireKey("dirt");
        ushort dirtWall = BlockCatalog.RequireKey("dirt_wall");
        ushort bedrock = BlockCatalog.RequireKey("bedrock");
        WorldSize size = grid.Size;
        int bedrockMinY = size.OriginY;
        int bedrockMaxY = size.OriginY + BedrockRows - 1;

        for (int y = size.OriginY; y <= size.MaxY; y++)
        {
            for (int x = size.OriginX; x <= size.MaxX; x++)
            {
                if (y >= bedrockMinY && y <= bedrockMaxY)
                {
                    grid.Set(x, y, TileLayer.Background, dirtWall);
                    grid.Set(x, y, TileLayer.Foreground, bedrock);
                    continue;
                }

                if (y <= SurfaceY)
                {
                    grid.Set(x, y, TileLayer.Background, dirtWall);
                    grid.Set(x, y, TileLayer.Foreground, dirt);
                }
            }
        }

        spawnX = size.OriginX + size.Width / 2;
        spawnY = SurfaceY + 1;
        if (spawnY > size.MaxY)
        {
            spawnY = size.MaxY;
        }
    }
}
