namespace SandboxServer;

internal enum TileLayer
{
    Background = 0,
    Foreground = 1,
    HyperForeground = 2
}

/// <summary>
/// Authoritative 3-layer tile grid. Storage is private so later chunking
/// does not leak into World, protocol, or clients.
/// Block ids are definition keys. Remaining health is per-cell, per-layer mutable state.
/// </summary>
internal sealed class WorldGrid
{
    private readonly ushort[] _background;
    private readonly ushort[] _foreground;
    private readonly ushort[] _hyperForeground;
    private readonly byte[] _backgroundHealth;
    private readonly byte[] _foregroundHealth;
    private readonly byte[] _hyperForegroundHealth;

    public WorldGrid(WorldSize size)
    {
        Size = size;
        int cells = size.Width * size.Height;
        _background = new ushort[cells];
        _foreground = new ushort[cells];
        _hyperForeground = new ushort[cells];
        _backgroundHealth = new byte[cells];
        _foregroundHealth = new byte[cells];
        _hyperForegroundHealth = new byte[cells];
    }

    public WorldSize Size { get; }

    public ushort Get(int x, int y, TileLayer layer)
    {
        if (!Size.Contains(x, y))
        {
            return BlockId.Empty;
        }

        return LayerArray(layer)[Index(x, y)];
    }

    public byte GetHealth(int x, int y, TileLayer layer)
    {
        if (!Size.Contains(x, y))
        {
            return 0;
        }

        return HealthArray(layer)[Index(x, y)];
    }

    public void Set(int x, int y, TileLayer layer, ushort blockId)
    {
        if (!Size.Contains(x, y))
        {
            return;
        }

        int i = Index(x, y);
        LayerArray(layer)[i] = blockId;
        HealthArray(layer)[i] = blockId == BlockId.Empty ? (byte)0 : BlockCatalog.MaxHealth(blockId);
    }

    public byte ApplyPunchDamage(int x, int y, TileLayer layer)
    {
        if (!Size.Contains(x, y))
        {
            return 0;
        }

        int i = Index(x, y);
        ushort current = LayerArray(layer)[i];
        if (current == BlockId.Empty)
        {
            return 0;
        }

        int remaining = HealthArray(layer)[i] - 1;
        if (remaining <= 0)
        {
            LayerArray(layer)[i] = BlockId.Empty;
            HealthArray(layer)[i] = 0;
            return 0;
        }

        HealthArray(layer)[i] = (byte)remaining;
        return HealthArray(layer)[i];
    }

    public void RestoreToMaxHealth(int x, int y, TileLayer layer)
    {
        if (!Size.Contains(x, y))
        {
            return;
        }

        int i = Index(x, y);
        ushort current = LayerArray(layer)[i];
        if (current == BlockId.Empty)
        {
            return;
        }

        HealthArray(layer)[i] = BlockCatalog.MaxHealth(current);
    }

    public GridSnapshot CopySnapshot()
    {
        var cells = new List<GridCellSnapshot>();
        WorldSize size = Size;
        for (int y = size.OriginY; y <= size.MaxY; y++)
        {
            for (int x = size.OriginX; x <= size.MaxX; x++)
            {
                GridCellSnapshot cell = CopyCell(x, y);
                if (cell.Background == BlockId.Empty
                    && cell.Foreground == BlockId.Empty
                    && cell.HyperForeground == BlockId.Empty)
                {
                    continue;
                }

                cells.Add(cell);
            }
        }

        return new GridSnapshot(size, cells.ToArray());
    }

    public GridCellSnapshot CopyCell(int x, int y)
    {
        return new GridCellSnapshot(
            x,
            y,
            Get(x, y, TileLayer.Background),
            Get(x, y, TileLayer.Foreground),
            Get(x, y, TileLayer.HyperForeground),
            GetHealth(x, y, TileLayer.Background),
            GetHealth(x, y, TileLayer.Foreground),
            GetHealth(x, y, TileLayer.HyperForeground));
    }

    private int Index(int x, int y)
    {
        return (x - Size.OriginX) + (y - Size.OriginY) * Size.Width;
    }

    private ushort[] LayerArray(TileLayer layer)
    {
        return layer switch
        {
            TileLayer.Foreground => _foreground,
            TileLayer.HyperForeground => _hyperForeground,
            _ => _background
        };
    }

    private byte[] HealthArray(TileLayer layer)
    {
        return layer switch
        {
            TileLayer.Foreground => _foregroundHealth,
            TileLayer.HyperForeground => _hyperForegroundHealth,
            _ => _backgroundHealth
        };
    }
}

internal readonly struct GridCellSnapshot
{
    public GridCellSnapshot(
        int x,
        int y,
        ushort background,
        ushort foreground,
        ushort hyperForeground,
        byte backgroundHealth,
        byte foregroundHealth,
        byte hyperForegroundHealth)
    {
        X = x;
        Y = y;
        Background = background;
        Foreground = foreground;
        HyperForeground = hyperForeground;
        BackgroundHealth = backgroundHealth;
        ForegroundHealth = foregroundHealth;
        HyperForegroundHealth = hyperForegroundHealth;
    }

    public int X { get; }
    public int Y { get; }
    public ushort Background { get; }
    public ushort Foreground { get; }
    public ushort HyperForeground { get; }
    public byte BackgroundHealth { get; }
    public byte ForegroundHealth { get; }
    public byte HyperForegroundHealth { get; }
}

internal readonly struct GridSnapshot
{
    public GridSnapshot(WorldSize size, GridCellSnapshot[] cells)
    {
        Size = size;
        Cells = cells;
    }

    public WorldSize Size { get; }
    public GridCellSnapshot[] Cells { get; }
}
