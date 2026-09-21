namespace SandboxServer;

/// <summary>
/// In-memory named worlds. Get-or-create on enter; unload when empty.
/// Not persistence and not multi-process routing.
/// </summary>
internal sealed class WorldDirectory
{
    private readonly object _gate = new();
    private readonly Dictionary<string, World> _worlds = new(StringComparer.OrdinalIgnoreCase);

    public WorldGetResult TryGetOrCreate(string worldName)
    {
        if (!NameRules.IsValid(worldName))
        {
            return WorldGetResult.InvalidName();
        }

        lock (_gate)
        {
            if (!_worlds.TryGetValue(worldName, out World? world))
            {
                world = new World(worldName);
                _worlds.Add(worldName, world);
            }

            return WorldGetResult.Ok(world);
        }
    }

    public World? TryGet(string worldName)
    {
        lock (_gate)
        {
            _worlds.TryGetValue(worldName, out World? world);
            return world;
        }
    }

    public World[] CopyOccupied()
    {
        lock (_gate)
        {
            return _worlds.Values.Where(world => !world.IsEmpty).ToArray();
        }
    }

    public void RemoveIfEmpty(string worldName)
    {
        lock (_gate)
        {
            if (!_worlds.TryGetValue(worldName, out World? world))
            {
                return;
            }

            if (world.IsEmpty)
            {
                _worlds.Remove(worldName);
            }
        }
    }
}

internal readonly struct WorldGetResult
{
    public bool IsInvalidName { get; }
    public World? World { get; }

    private WorldGetResult(bool isInvalidName, World? world)
    {
        IsInvalidName = isInvalidName;
        World = world;
    }

    public static WorldGetResult Ok(World world) => new(false, world);

    public static WorldGetResult InvalidName() => new(true, null);
}
