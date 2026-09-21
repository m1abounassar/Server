using System.Net;
using System.Net.Sockets;

namespace SandboxServer;

internal sealed class ServerHost
{
    private readonly TcpListener _listener;
    private readonly IPAddress _bindAddress;
    private readonly int _port;
    private readonly PlayerRegistry _players = new();
    private readonly PlayerStore _playerStore = new();
    private readonly WorldDirectory _worlds = new();
    private readonly List<ClientConnection> _connections = new();
    private readonly List<Task> _sessionTasks = new();
    private readonly object _gate = new();
    private readonly ChatRateLimiter _chatRate = new();
    private int _stopping;
    private SimulationScheduler? _scheduler;
    public int ListenPort { get; private set; }

    public ServerHost(IPAddress address, int port)
    {
        _bindAddress = address;
        _port = port;
        _listener = new TcpListener(address, port);
        _scheduler = new SimulationScheduler(_worlds, _playerStore, BroadcastTickAsync);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Log($"Listening on {_bindAddress}:{ListenPort}");
        using var schedulerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task schedulerTask = _scheduler!.RunAsync(schedulerCts.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient tcpClient;
                try
                {
                    tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                var connection = new ClientConnection(tcpClient);
                Task sessionTask = RunConnectionAsync(connection, cancellationToken);

                lock (_gate)
                {
                    _connections.Add(connection);
                    _sessionTasks.RemoveAll(task => task.IsCompleted);
                    _sessionTasks.Add(sessionTask);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _stopping, 1);
            StopListener();
            CloseAllConnections();
            await WaitForSessionsAsync();
            schedulerCts.Cancel();
            try
            {
                await schedulerTask;
            }
            catch (OperationCanceledException)
            {
            }

            Log("Server stopped.");
        }
    }

    private async Task RunConnectionAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await connection.RunAsync(
                (line, ct) => HandleCommandAsync(connection, line, ct),
                cancellationToken);
        }
        finally
        {
            await FinishConnectionAsync(connection);
        }
    }

    private async Task HandleCommandAsync(
        ClientConnection connection,
        string line,
        CancellationToken cancellationToken)
    {
        if (connection.IsCleanupStarted)
        {
            return;
        }

        if (!Protocol.TryParseClientLine(line, out ClientCommand? command, out string? error))
        {
            await TrySendAsync(connection, Protocol.Error(error!), cancellationToken);
            return;
        }

        switch (command)
        {
            case JoinCommand join:
                await HandleJoinAsync(connection, join.Name, cancellationToken);
                break;
            case EnterCommand enter:
                await HandleEnterAsync(connection, enter.WorldName, cancellationToken);
                break;
            case LeaveCommand:
                await HandleLeaveAsync(connection, notifyLeaver: true, cancellationToken);
                break;
            case InputCommand input:
                await HandleInputAsync(connection, input, cancellationToken);
                break;
            case BreakCommand breakCommand:
                await HandleBreakAsync(connection, breakCommand, cancellationToken);
                break;
            case PlaceCommand placeCommand:
                await HandlePlaceAsync(connection, placeCommand, cancellationToken);
                break;
            case SelectCommand selectCommand:
                await HandleSelectAsync(connection, selectCommand, cancellationToken);
                break;
            case ChatCommand chatCommand:
                await HandleChatAsync(connection, chatCommand, cancellationToken);
                break;
        }
    }

    private async Task HandleJoinAsync(
        ClientConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        if (connection.PlayerId is not null)
        {
            await TrySendAsync(connection, Protocol.Error("already_joined"), cancellationToken);
            return;
        }

        RegisterResult result = _players.TryRegister(name);
        if (result.Status == RegisterStatus.InvalidName)
        {
            await TrySendAsync(connection, Protocol.Error("invalid_name"), cancellationToken);
            return;
        }

        if (result.Status == RegisterStatus.DuplicateName)
        {
            await TrySendAsync(connection, Protocol.Error("duplicate_name"), cancellationToken);
            return;
        }

        connection.AssignPlayer(result.Identity.Id);
        _playerStore.Ensure(result.Identity.Id);
        Log($"Player {result.Identity.Id} '{result.Identity.Name}' identified from {connection.Remote}");
        await TrySendAsync(connection, Protocol.Welcome(result.Identity), cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        await TrySendAsync(connection, Protocol.ContentRev(ContentRuntime.ContentRevision), cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        await SendEconomySnapshotAsync(connection, result.Identity.Id, cancellationToken);
    }

    private async Task HandleEnterAsync(
        ClientConnection connection,
        string worldName,
        CancellationToken cancellationToken)
    {
        if (connection.PlayerId is not int playerId)
        {
            await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            return;
        }

        if (connection.CurrentWorldName is not null)
        {
            await TrySendAsync(connection, Protocol.Error("already_in_world"), cancellationToken);
            return;
        }

        PlayerIdentity? identity = _players.TryGet(playerId);
        if (identity is null)
        {
            await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            return;
        }

        WorldGetResult worldResult = _worlds.TryGetOrCreate(worldName);
        if (worldResult.IsInvalidName || worldResult.World is null)
        {
            await TrySendAsync(connection, Protocol.Error("invalid_world_name"), cancellationToken);
            return;
        }

        World world = worldResult.World;
        EnterResult enter = world.TryAddMember(identity.Value.Id, identity.Value.Name);
        if (enter.Status != EnterStatus.Ok)
        {
            await TrySendAsync(connection, Protocol.Error("already_in_world"), cancellationToken);
            return;
        }

        connection.SetCurrentWorldName(world.Name);
        Log($"Player {identity.Value.Id} '{identity.Value.Name}' entered '{world.Name}'");

        await TrySendAsync(connection, Protocol.WorldEntered(world.Name, enter.Self), cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        await SendGridSnapshotAsync(connection, world.Name, enter.Grid, cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        await SendItemSnapshotAsync(connection, enter.Items, cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        foreach (PlayerSnapshot other in enter.Others)
        {
            await TrySendAsync(connection, Protocol.PlayerJoined(other), cancellationToken);
            if (connection.IsCleanupStarted)
            {
                return;
            }
        }

        await BroadcastToWorldAsync(
            world.Name,
            Protocol.PlayerJoined(enter.Self),
            except: connection,
            cancellationToken);
    }

    private async Task HandleLeaveAsync(
        ClientConnection connection,
        bool notifyLeaver,
        CancellationToken cancellationToken)
    {
        if (connection.PlayerId is null)
        {
            if (notifyLeaver)
            {
                await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            }

            return;
        }

        string? worldName = connection.ClearCurrentWorldName();
        if (worldName is null)
        {
            if (notifyLeaver)
            {
                await TrySendAsync(connection, Protocol.Error("not_in_world"), cancellationToken);
            }

            return;
        }

        World? world = _worlds.TryGet(worldName);
        PlayerSnapshot? left = null;
        if (world is not null && connection.PlayerId is int playerId)
        {
            left = world.TryRemoveMember(playerId);
            _worlds.RemoveIfEmpty(worldName);
        }

        if (left is not null)
        {
            Log($"Player {left.Value.Id} '{left.Value.Name}' left '{worldName}'");
        }

        if (notifyLeaver && !connection.IsCleanupStarted)
        {
            await TrySendAsync(connection, Protocol.WorldLeft(worldName), cancellationToken);
        }

        if (left is not null && Volatile.Read(ref _stopping) == 0)
        {
            await BroadcastToWorldAsync(
                worldName,
                Protocol.PlayerLeft(left.Value.Id),
                except: connection,
                cancellationToken);
        }
    }

    private async Task HandleInputAsync(
        ClientConnection connection,
        InputCommand command,
        CancellationToken cancellationToken)
    {
        if (connection.PlayerId is not int playerId)
        {
            await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            return;
        }

        string? worldName = connection.CurrentWorldName;
        if (worldName is null)
        {
            await TrySendAsync(connection, Protocol.Error("not_in_world"), cancellationToken);
            return;
        }

        World? world = _worlds.TryGet(worldName);
        if (world is null)
        {
            connection.ClearCurrentWorldName();
            await TrySendAsync(connection, Protocol.Error("not_in_world"), cancellationToken);
            return;
        }

        InputAcceptStatus status = world.TryMergeInput(playerId, command.Seq, command.Left, command.Right, command.Jump);
        if (status == InputAcceptStatus.NotInWorld)
        {
            await TrySendAsync(connection, Protocol.Error("not_in_world"), cancellationToken);
        }
    }

    private async Task HandleBreakAsync(
        ClientConnection connection,
        BreakCommand command,
        CancellationToken cancellationToken)
    {
        World? world = await TryGetWorldForEditAsync(connection, cancellationToken);
        if (world is null || connection.PlayerId is not int playerId)
        {
            return;
        }

        EditResult result = world.TryBreak(playerId, command.X, command.Y);
        await FinishEditAsync(connection, world.Name, result, cancellationToken);
    }

    private async Task HandlePlaceAsync(
        ClientConnection connection,
        PlaceCommand command,
        CancellationToken cancellationToken)
    {
        World? world = await TryGetWorldForEditAsync(connection, cancellationToken);
        if (world is null || connection.PlayerId is not int playerId)
        {
            return;
        }

        EditResult result = world.TryPlaceSelected(playerId, command.X, command.Y, _playerStore);
        await FinishEditAsync(connection, world.Name, result, cancellationToken);
    }

    private async Task HandleSelectAsync(
        ClientConnection connection,
        SelectCommand command,
        CancellationToken cancellationToken)
    {
        if (connection.PlayerId is not int playerId)
        {
            await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            return;
        }

        if (!_playerStore.TrySelect(playerId, command.Slot, out string? error))
        {
            await TrySendAsync(connection, Protocol.Error(error ?? "invalid_slot"), cancellationToken);
            return;
        }

        await TrySendAsync(connection, Protocol.Selected(command.Slot), cancellationToken);
    }

    private async Task HandleChatAsync(
        ClientConnection connection,
        ChatCommand command,
        CancellationToken cancellationToken)
    {
        if (connection.PlayerId is not int playerId)
        {
            await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            return;
        }

        string? worldName = connection.CurrentWorldName;
        if (worldName is null)
        {
            await TrySendAsync(connection, Protocol.Error("not_in_world"), cancellationToken);
            return;
        }

        PlayerIdentity? identity = _players.TryGet(playerId);
        if (identity is null)
        {
            await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            return;
        }

        if (!_chatRate.TryAdmit(playerId))
        {
            return;
        }

        await BroadcastToWorldAsync(
            worldName,
            Protocol.Chat(identity.Value.Id, identity.Value.Name, command.Text),
            except: null,
            cancellationToken);
    }

    private async Task<World?> TryGetWorldForEditAsync(
        ClientConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.PlayerId is null)
        {
            await TrySendAsync(connection, Protocol.Error("not_joined"), cancellationToken);
            return null;
        }

        string? worldName = connection.CurrentWorldName;
        if (worldName is null)
        {
            await TrySendAsync(connection, Protocol.Error("not_in_world"), cancellationToken);
            return null;
        }

        World? world = _worlds.TryGet(worldName);
        if (world is null)
        {
            connection.ClearCurrentWorldName();
            await TrySendAsync(connection, Protocol.Error("not_in_world"), cancellationToken);
            return null;
        }

        return world;
    }

    private async Task FinishEditAsync(
        ClientConnection connection,
        string worldName,
        EditResult result,
        CancellationToken cancellationToken)
    {
        if (result.Status != EditStatus.Ok)
        {
            await TrySendAsync(connection, Protocol.Error(EditErrorReason(result.Status)), cancellationToken);
            return;
        }

        await BroadcastToWorldAsync(
            worldName,
            Protocol.Tile(result.Cell),
            except: null,
            cancellationToken);

        for (int i = 0; i < result.ItemChanges.Length; i++)
        {
            WorldItemChange change = result.ItemChanges[i];
            string line = change.Removed ? Protocol.ItemRemoved(change.Item.Id) : Protocol.Item(change.Item);
            await BroadcastToWorldAsync(worldName, line, except: null, cancellationToken);
        }

        for (int i = 0; i < result.OwnerSlots.Length; i++)
        {
            await TrySendAsync(connection, Protocol.InventorySlot(result.OwnerSlots[i]), cancellationToken);
        }

        if (result.OwnerSlots.Length > 0 && connection.PlayerId is int ownerId)
        {
            await TrySendAsync(connection, Protocol.Selected(_playerStore.SelectedSlot(ownerId)), cancellationToken);
        }
    }

    private static string EditErrorReason(EditStatus status)
    {
        return status switch
        {
            EditStatus.NotInWorld => "not_in_world",
            EditStatus.OutOfBounds => "out_of_bounds",
            EditStatus.OutOfRange => "out_of_range",
            EditStatus.EmptyCell => "empty_cell",
            EditStatus.Occupied => "occupied",
            EditStatus.InvalidBlock => "invalid_block",
            EditStatus.LayerNotAllowed => "layer_not_allowed",
            EditStatus.Unbreakable => "unbreakable",
            EditStatus.Unplaceable => "unplaceable",
            EditStatus.EmptySlot => "empty_slot",
            _ => "invalid_command"
        };
    }

    private async Task FinishConnectionAsync(ClientConnection connection)
    {
        if (!connection.TryBeginCleanup())
        {
            return;
        }

        connection.Close();

        lock (_gate)
        {
            _connections.Remove(connection);
        }

        await HandleLeaveAsync(connection, notifyLeaver: false, CancellationToken.None);

        if (connection.PlayerId is int playerId)
        {
            _playerStore.Remove(playerId);
            _chatRate.Remove(playerId);
            PlayerIdentity? removed = _players.TryUnregister(playerId);
            if (removed is not null)
            {
                Log($"Player {removed.Value.Id} '{removed.Value.Name}' unregistered");
            }
        }
    }

    private async Task SendGridSnapshotAsync(
        ClientConnection connection,
        string worldName,
        GridSnapshot grid,
        CancellationToken cancellationToken)
    {
        await TrySendAsync(connection, Protocol.WorldGridHeader(worldName, grid.Size), cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        foreach (GridCellSnapshot cell in grid.Cells)
        {
            await TrySendAsync(connection, Protocol.Tile(cell), cancellationToken);
            if (connection.IsCleanupStarted)
            {
                return;
            }
        }

        await TrySendAsync(connection, Protocol.WorldGridEnd(), cancellationToken);
    }

    private async Task SendItemSnapshotAsync(
        ClientConnection connection,
        WorldItemSnapshot[] items,
        CancellationToken cancellationToken)
    {
        await TrySendAsync(connection, Protocol.WorldItemsBegin(), cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        for (int i = 0; i < items.Length; i++)
        {
            await TrySendAsync(connection, Protocol.Item(items[i]), cancellationToken);
            if (connection.IsCleanupStarted)
            {
                return;
            }
        }

        await TrySendAsync(connection, Protocol.WorldItemsEnd(), cancellationToken);
    }

    private async Task SendEconomySnapshotAsync(
        ClientConnection connection,
        int playerId,
        CancellationToken cancellationToken)
    {
        await TrySendAsync(connection, Protocol.Gems(_playerStore.GemBalance(playerId)), cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        await TrySendAsync(connection, Protocol.InventoryBegin(ItemConfig.InventoryCapacity), cancellationToken);
        SlotDelta[] slots = _playerStore.OccupiedSlots(playerId);
        for (int i = 0; i < slots.Length; i++)
        {
            await TrySendAsync(connection, Protocol.InventorySlot(slots[i]), cancellationToken);
            if (connection.IsCleanupStarted)
            {
                return;
            }
        }

        await TrySendAsync(connection, Protocol.InventoryEnd(), cancellationToken);
        if (connection.IsCleanupStarted)
        {
            return;
        }

        await TrySendAsync(connection, Protocol.Selected(_playerStore.SelectedSlot(playerId)), cancellationToken);
    }

    private async Task BroadcastTickAsync(World world, WorldTickResult result, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            return;
        }

        var lines = new List<string>(result.Players.Length + result.Regenerated.Length + result.ItemChanges.Length);
        for (int i = 0; i < result.Players.Length; i++)
        {
            lines.Add(Protocol.PlayerState(result.Players[i]));
        }

        for (int i = 0; i < result.Regenerated.Length; i++)
        {
            lines.Add(Protocol.Tile(result.Regenerated[i]));
        }

        for (int i = 0; i < result.ItemChanges.Length; i++)
        {
            WorldItemChange change = result.ItemChanges[i];
            lines.Add(change.Removed ? Protocol.ItemRemoved(change.Item.Id) : Protocol.Item(change.Item));
        }

        for (int i = 0; i < lines.Count; i++)
        {
            await BroadcastToWorldAsync(world.Name, lines[i], except: null, cancellationToken);
        }

        for (int i = 0; i < result.Credits.Length; i++)
        {
            OwnerCredit credit = result.Credits[i];
            ClientConnection? owner = FindConnection(credit.PlayerId);
            if (owner is null)
            {
                continue;
            }

            for (int s = 0; s < credit.Slots.Length; s++)
            {
                await TrySendAsync(owner, Protocol.InventorySlot(credit.Slots[s]), cancellationToken);
            }

            if (credit.GemBalance is int gems)
            {
                await TrySendAsync(owner, Protocol.Gems(gems), cancellationToken);
            }

            for (int p = 0; p < credit.Pickups.Length; p++)
            {
                PickupNotice pickup = credit.Pickups[p];
                string line = pickup.Kind == RewardKind.Currency
                    ? Protocol.CollectedGem(pickup.Quantity)
                    : Protocol.CollectedItem(pickup.TypeId, pickup.Quantity);
                await TrySendAsync(owner, line, cancellationToken);
            }
        }
    }

    private ClientConnection? FindConnection(int playerId)
    {
        lock (_gate)
        {
            return _connections.FirstOrDefault(candidate => candidate.PlayerId == playerId);
        }
    }

    private async Task BroadcastToWorldAsync(
        string worldName,
        string line,
        ClientConnection? except,
        CancellationToken cancellationToken)
    {
        ClientConnection[] targets;
        lock (_gate)
        {
            targets = _connections
                .Where(candidate =>
                    candidate != except
                    && candidate.CurrentWorldName is string memberWorld
                    && string.Equals(memberWorld, worldName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        Task[] sends = targets
            .Select(candidate => TrySendAsync(candidate, line, cancellationToken))
            .ToArray();

        if (sends.Length > 0)
        {
            await Task.WhenAll(sends);
        }
    }

    private async Task TrySendAsync(
        ClientConnection connection,
        string line,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendLineAsync(line, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await FinishConnectionAsync(connection);
        }
        catch (IOException)
        {
            await FinishConnectionAsync(connection);
        }
        catch (ObjectDisposedException)
        {
            await FinishConnectionAsync(connection);
        }
    }

    private void StopListener()
    {
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CloseAllConnections()
    {
        ClientConnection[] live;
        lock (_gate)
        {
            live = _connections.ToArray();
        }

        foreach (ClientConnection connection in live)
        {
            connection.Close();
        }
    }

    private async Task WaitForSessionsAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            pending = _sessionTasks.ToArray();
        }

        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending);
        }
        catch (Exception)
        {
        }
    }

    internal static void Log(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
