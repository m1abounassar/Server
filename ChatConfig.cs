namespace SandboxServer;

/// <summary>
/// Temporary chat knobs. Not a moderation or persistence system.
/// </summary>
internal static class ChatConfig
{
    public const int MaxMessageLength = 120;
    public const int RateLimitCount = 4;
    public const int RateLimitWindowMs = 1000;
}
