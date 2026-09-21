namespace SandboxServer;

/// <summary>
/// Authoritative edit reach. Chebyshev (chess-king) distance in tile steps:
/// max(|cellX - playerCellX|, |cellY - playerCellY|) &lt;= 3.
/// Player cell is Floor of feet position; target cell is an integer tile.
/// </summary>
internal static class InteractionRange
{
    public const int MaxChebyshevTiles = 3;

    public static bool Contains(int playerCellX, int playerCellY, int cellX, int cellY)
    {
        int dx = Math.Abs(cellX - playerCellX);
        int dy = Math.Abs(cellY - playerCellY);
        return Math.Max(dx, dy) <= MaxChebyshevTiles;
    }

    public static bool ContainsFeet(float feetX, float feetY, int cellX, int cellY)
    {
        int playerCellX = (int)MathF.Floor(feetX);
        int playerCellY = (int)MathF.Floor(feetY);
        return Contains(playerCellX, playerCellY, cellX, cellY);
    }
}
