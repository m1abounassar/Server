namespace SandboxServer;

internal readonly struct WorldSize
{
    public WorldSize(int originX, int originY, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "World size must be positive.");
        }

        OriginX = originX;
        OriginY = originY;
        Width = width;
        Height = height;
    }

    public int OriginX { get; }
    public int OriginY { get; }
    public int Width { get; }
    public int Height { get; }

    public int MaxX => OriginX + Width - 1;
    public int MaxY => OriginY + Height - 1;

    public static WorldSize Default { get; } = new(originX: 0, originY: -8, width: 100, height: 68);

    public bool Contains(int x, int y)
    {
        return x >= OriginX && x <= MaxX && y >= OriginY && y <= MaxY;
    }
}
