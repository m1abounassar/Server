namespace SandboxServer;

internal readonly struct MovementInput
{
    public MovementInput(bool left, bool right, bool jumpDown)
    {
        Left = left;
        Right = right;
        JumpDown = jumpDown;
    }

    public bool Left { get; }
    public bool Right { get; }
    public bool JumpDown { get; }
}

internal struct MovementState
{
    public float X;
    public float Y;
    public float Vx;
    public float Vy;
    public bool Grounded;
}

/// <summary>
/// Fixed-step AABB integration. Authority lives on the server; the Unity client
/// duplicates this function for prediction. Collision uses foreground solids only.
/// </summary>
internal static class PlayerMovement
{
    private const float EdgeEpsilon = 1e-4f;

    public delegate bool SolidQuery(int x, int y);

    public static void Integrate(
        ref MovementState state,
        in MovementInput input,
        float dt,
        WorldSize size,
        SolidQuery isForegroundSolid)
    {
        float axis = 0f;
        if (input.Right)
        {
            axis += 1f;
        }

        if (input.Left)
        {
            axis -= 1f;
        }

        state.Vx = axis * MovementConfig.MoveSpeed;

        if (input.JumpDown && state.Grounded)
        {
            state.Vy = MovementConfig.JumpVelocity;
            state.Grounded = false;
        }

        state.Vy -= MovementConfig.Gravity * dt;
        if (state.Vy < -MovementConfig.MaxFallSpeed)
        {
            state.Vy = -MovementConfig.MaxFallSpeed;
        }

        state.X += state.Vx * dt;
        ClampHorizontal(ref state, size);
        ResolveHorizontal(ref state, size, isForegroundSolid);

        state.Y += state.Vy * dt;
        ClampVertical(ref state, size, out bool hitWorldFloor);
        bool hitTileFloor = ResolveVertical(ref state, size, isForegroundSolid);
        state.Grounded = (hitWorldFloor || hitTileFloor) && state.Vy <= 0f;
    }

    private static void ClampHorizontal(ref MovementState state, WorldSize size)
    {
        float halfW = MovementConfig.BodyWidth * 0.5f;
        float minX = size.OriginX + halfW;
        float maxX = size.OriginX + size.Width - halfW;
        if (state.X < minX)
        {
            state.X = minX;
            state.Vx = 0f;
        }
        else if (state.X > maxX)
        {
            state.X = maxX;
            state.Vx = 0f;
        }
    }

    private static void ClampVertical(ref MovementState state, WorldSize size, out bool hitWorldFloor)
    {
        hitWorldFloor = false;
        float minY = size.OriginY;
        float maxY = size.OriginY + size.Height - MovementConfig.BodyHeight;
        if (state.Y < minY)
        {
            state.Y = minY;
            if (state.Vy < 0f)
            {
                state.Vy = 0f;
            }

            hitWorldFloor = true;
        }
        else if (state.Y > maxY)
        {
            state.Y = maxY;
            if (state.Vy > 0f)
            {
                state.Vy = 0f;
            }
        }
    }

    private static void ResolveHorizontal(ref MovementState state, WorldSize size, SolidQuery isForegroundSolid)
    {
        float halfW = MovementConfig.BodyWidth * 0.5f;
        OverlapRange(state.X - halfW, state.X + halfW, size.OriginX, size.MaxX, out int minTx, out int maxTx);
        OverlapRange(state.Y, state.Y + MovementConfig.BodyHeight, size.OriginY, size.MaxY, out int minTy, out int maxTy);

        int txStart = state.Vx >= 0f ? minTx : maxTx;
        int txEnd = state.Vx >= 0f ? maxTx : minTx;
        int txStep = state.Vx >= 0f ? 1 : -1;

        for (int tx = txStart; tx != txEnd + txStep; tx += txStep)
        {
            for (int ty = minTy; ty <= maxTy; ty++)
            {
                if (!isForegroundSolid(tx, ty) || !Overlaps(state, tx, ty, halfW))
                {
                    continue;
                }

                if (state.Vx > 0f)
                {
                    state.X = tx - halfW;
                    state.Vx = 0f;
                }
                else if (state.Vx < 0f)
                {
                    state.X = tx + 1f + halfW;
                    state.Vx = 0f;
                }
                else
                {
                    float distLeft = (state.X + halfW) - tx;
                    float distRight = (tx + 1f) - (state.X - halfW);
                    state.X = distLeft < distRight ? tx - halfW : tx + 1f + halfW;
                }

                return;
            }
        }
    }

    private static bool ResolveVertical(ref MovementState state, WorldSize size, SolidQuery isForegroundSolid)
    {
        float halfW = MovementConfig.BodyWidth * 0.5f;
        OverlapRange(state.X - halfW, state.X + halfW, size.OriginX, size.MaxX, out int minTx, out int maxTx);
        OverlapRange(state.Y, state.Y + MovementConfig.BodyHeight, size.OriginY, size.MaxY, out int minTy, out int maxTy);

        int tyStart = state.Vy >= 0f ? minTy : maxTy;
        int tyEnd = state.Vy >= 0f ? maxTy : minTy;
        int tyStep = state.Vy >= 0f ? 1 : -1;

        for (int ty = tyStart; ty != tyEnd + tyStep; ty += tyStep)
        {
            for (int tx = minTx; tx <= maxTx; tx++)
            {
                if (!isForegroundSolid(tx, ty) || !Overlaps(state, tx, ty, halfW))
                {
                    continue;
                }

                if (state.Vy > 0f)
                {
                    state.Y = ty - MovementConfig.BodyHeight;
                    state.Vy = 0f;
                    return false;
                }

                state.Y = ty + 1f;
                state.Vy = 0f;
                return true;
            }
        }

        return false;
    }

    private static bool Overlaps(in MovementState state, int tx, int ty, float halfW)
    {
        float left = state.X - halfW;
        float right = state.X + halfW;
        float bottom = state.Y;
        float top = state.Y + MovementConfig.BodyHeight;
        return left < tx + 1f && right > tx && bottom < ty + 1f && top > ty;
    }

    private static void OverlapRange(float min, float max, int worldMin, int worldMax, out int i0, out int i1)
    {
        i0 = (int)MathF.Floor(min);
        i1 = (int)MathF.Floor(max - EdgeEpsilon);
        if (i0 < worldMin)
        {
            i0 = worldMin;
        }

        if (i1 > worldMax)
        {
            i1 = worldMax;
        }

        if (i1 < i0)
        {
            i1 = i0;
        }
    }
}
