namespace SandboxServer;

/// <summary>
/// Initial 60 Hz sim / 20 Hz snapshot configuration. 60 Hz is a convenient
/// starting rate, not a collision-correctness requirement. Collision safety
/// comes from the fixed-step resolver, bounded speeds, and tile AABB tests.
/// </summary>
internal static class MovementConfig
{
    public const float TickRate = 60f;
    public const float Dt = 1f / 60f;
    public const int SnapshotIntervalTicks = 3;
    public const int InputTimeoutTicks = 10;
    public const int MaxInputsPerSecond = 90;

    public const float MoveSpeed = 4.5f;
    public const float Gravity = 48f;
    public const float JumpVelocity = 11f;
    public const float MaxFallSpeed = 18f;
    public const float BodyWidth = 0.8f;
    public const float BodyHeight = 0.95f;
}
