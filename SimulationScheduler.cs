using System.Diagnostics;

namespace SandboxServer;

/// <summary>
/// Process-wide fixed-step loop. Ticks occupied worlds in sequence on one
/// worker; does not own sockets or per-connection tasks.
/// </summary>
internal sealed class SimulationScheduler
{
    private readonly WorldDirectory _worlds;
    private readonly PlayerStore _playerStore;
    private readonly Func<World, WorldTickResult, CancellationToken, Task> _onTicked;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public SimulationScheduler(
        WorldDirectory worlds,
        PlayerStore playerStore,
        Func<World, WorldTickResult, CancellationToken, Task> onTicked)
    {
        _worlds = worlds;
        _playerStore = playerStore;
        _onTicked = onTicked;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        long intervalTicks = (long)(MovementConfig.Dt * Stopwatch.Frequency);
        long next = watch.ElapsedTicks;
        const int maxCatchUp = 5;

        while (!cancellationToken.IsCancellationRequested)
        {
            int steps = 0;
            while (watch.ElapsedTicks >= next && steps < maxCatchUp)
            {
                TickOccupied(cancellationToken);
                next += intervalTicks;
                steps++;
            }

            if (steps == maxCatchUp && watch.ElapsedTicks > next)
            {
                next = watch.ElapsedTicks;
            }

            long remaining = next - watch.ElapsedTicks;
            if (remaining > 0)
            {
                int delayMs = (int)(remaining * 1000 / Stopwatch.Frequency);
                if (delayMs < 1)
                {
                    delayMs = 1;
                }

                try
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void TickOccupied(CancellationToken cancellationToken)
    {
        World[] occupied = _worlds.CopyOccupied();
        for (int i = 0; i < occupied.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            WorldTickResult result = occupied[i].Tick(MovementConfig.Dt, _playerStore);
            if (result.HasBroadcasts)
            {
                World world = occupied[i];
                _ = BroadcastSafelyAsync(world, result, cancellationToken);
            }
        }
    }

    private async Task BroadcastSafelyAsync(World world, WorldTickResult result, CancellationToken cancellationToken)
    {
        try
        {
            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                await _onTicked(world, result, cancellationToken);
            }
            finally
            {
                _sendGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ServerHost.Log($"Snapshot send failed for '{world.Name}': {ex.Message}");
        }
    }
}
