namespace SandboxServer;

/// <summary>
/// Drops extra chat lines in a short window. Does not queue.
/// </summary>
internal sealed class ChatRateLimiter
{
    private readonly Dictionary<int, Queue<long>> _windows = new();

    public bool TryAdmit(int playerId)
    {
        long now = Environment.TickCount64;
        if (!_windows.TryGetValue(playerId, out Queue<long>? times))
        {
            times = new Queue<long>();
            _windows[playerId] = times;
        }

        while (times.Count > 0 && now - times.Peek() >= ChatConfig.RateLimitWindowMs)
        {
            times.Dequeue();
        }

        if (times.Count >= ChatConfig.RateLimitCount)
        {
            return false;
        }

        times.Enqueue(now);
        return true;
    }

    public void Remove(int playerId)
    {
        _windows.Remove(playerId);
    }
}
