namespace SandboxServer;

/// <summary>
/// Process-wide player identity. Not world membership and not sockets.
/// </summary>
internal sealed class PlayerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _namesById = new();
    private readonly Dictionary<string, int> _idsByName = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId = 1;

    public RegisterResult TryRegister(string name)
    {
        lock (_gate)
        {
            if (!NameRules.IsValid(name))
            {
                return RegisterResult.Fail(RegisterStatus.InvalidName);
            }

            if (_idsByName.ContainsKey(name))
            {
                return RegisterResult.Fail(RegisterStatus.DuplicateName);
            }

            int id = _nextId++;
            _namesById.Add(id, name);
            _idsByName.Add(name, id);
            return RegisterResult.Ok(new PlayerIdentity(id, name));
        }
    }

    public PlayerIdentity? TryUnregister(int playerId)
    {
        lock (_gate)
        {
            if (!_namesById.Remove(playerId, out string? name))
            {
                return null;
            }

            _idsByName.Remove(name);
            return new PlayerIdentity(playerId, name);
        }
    }

    public PlayerIdentity? TryGet(int playerId)
    {
        lock (_gate)
        {
            if (!_namesById.TryGetValue(playerId, out string? name))
            {
                return null;
            }

            return new PlayerIdentity(playerId, name);
        }
    }
}

internal enum RegisterStatus
{
    Ok,
    InvalidName,
    DuplicateName
}

internal readonly struct PlayerIdentity
{
    public PlayerIdentity(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }
    public string Name { get; }
}

internal readonly struct RegisterResult
{
    public RegisterStatus Status { get; }
    public PlayerIdentity Identity { get; }

    private RegisterResult(RegisterStatus status, PlayerIdentity identity)
    {
        Status = status;
        Identity = identity;
    }

    public static RegisterResult Ok(PlayerIdentity identity)
        => new(RegisterStatus.Ok, identity);

    public static RegisterResult Fail(RegisterStatus status)
        => new(status, default);
}
