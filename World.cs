namespace SandboxServer;

/// <summary>
/// One named in-memory world. Owns occupants, the authoritative tile grid,
/// world-item entities, and the fixed-step simulation for those occupants.
/// Does not own sockets, framing, or process-wide player identity.
/// </summary>
internal sealed class World
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Occupant> _occupants = new();
    private readonly Dictionary<(int x, int y, TileLayer layer), int> _damaged = new();
    private readonly Dictionary<int, WorldItem> _items = new();
    private readonly WorldGrid _grid;
    private readonly IChance _chance;
    private readonly int _spawnCellX;
    private readonly int _spawnCellY;
    private int _nextItemId = 1;

    public World(string name, WorldSize? size = null, IChance? chance = null)
    {
        Name = name;
        Size = size ?? WorldSize.Default;
        _chance = chance ?? new SystemChance();
        _grid = new WorldGrid(Size);
        WorldGenerator.FillDefault(_grid, name, out _spawnCellX, out _spawnCellY);
    }

    public string Name { get; }

    public WorldSize Size { get; }

    public int TickIndex { get; private set; }

    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _occupants.Count == 0;
            }
        }
    }

    public EnterResult TryAddMember(int playerId, string playerName)
    {
        lock (_gate)
        {
            if (_occupants.ContainsKey(playerId))
            {
                return EnterResult.Fail(EnterStatus.AlreadyInWorld);
            }

            var occupant = new Occupant(playerId, playerName, _spawnCellX, _spawnCellY);
            _occupants.Add(playerId, occupant);

            PlayerSnapshot[] others = _occupants.Values
                .Where(existing => existing.Id != playerId)
                .Select(existing => existing.Snapshot())
                .ToArray();

            return EnterResult.Ok(occupant.Snapshot(), others, _grid.CopySnapshot(), CopyItemSnapshots());
        }
    }

    public InputAcceptStatus TryMergeInput(int playerId, uint seq, bool left, bool right, bool jumpDown)
    {
        lock (_gate)
        {
            if (!_occupants.TryGetValue(playerId, out Occupant? occupant))
            {
                return InputAcceptStatus.NotInWorld;
            }

            return occupant.MergeInput(seq, left, right, jumpDown);
        }
    }

    public PlayerSnapshot? TryRemoveMember(int playerId)
    {
        lock (_gate)
        {
            if (!_occupants.Remove(playerId, out Occupant? occupant))
            {
                return null;
            }

            return occupant.Snapshot();
        }
    }

    public GridSnapshot CopyGridSnapshot()
    {
        lock (_gate)
        {
            return _grid.CopySnapshot();
        }
    }

    public WorldTickResult Tick(float dt, PlayerStore players)
    {
        lock (_gate)
        {
            if (_occupants.Count == 0)
            {
                return WorldTickResult.Empty;
            }

            TickIndex++;
            foreach (Occupant occupant in _occupants.Values)
            {
                occupant.Simulate(dt, Size, IsForegroundSolid);
            }

            CollectPickups(players, out WorldItemChange[] itemChanges, out OwnerCredit[] credits);
            GridCellSnapshot[] regenerated = RestoreDamagedBlocks();
            PlayerSimSnapshot[] simPlayers = Array.Empty<PlayerSimSnapshot>();
            if (TickIndex % MovementConfig.SnapshotIntervalTicks == 0)
            {
                simPlayers = _occupants.Values
                    .Select(occupant => occupant.SimSnapshot(TickIndex))
                    .ToArray();
            }

            return new WorldTickResult(TickIndex, simPlayers, regenerated, itemChanges, credits);
        }
    }

    public EditResult TryBreak(int playerId, int x, int y)
    {
        lock (_gate)
        {
            if (!_occupants.TryGetValue(playerId, out Occupant? occupant))
            {
                return EditResult.Fail(EditStatus.NotInWorld);
            }

            if (!Size.Contains(x, y))
            {
                return EditResult.Fail(EditStatus.OutOfBounds);
            }

            if (!InteractionRange.ContainsFeet(occupant.X, occupant.Y, x, y))
            {
                return EditResult.Fail(EditStatus.OutOfRange);
            }

            if (!TryResolvePunchLayer(x, y, out TileLayer layer, out ushort current))
            {
                return EditResult.Fail(EditStatus.EmptyCell);
            }

            if (!BlockCatalog.TryGet(current, out BlockDefinition definition) || !definition.Breakable)
            {
                return EditResult.Fail(EditStatus.Unbreakable);
            }

            byte remaining = _grid.ApplyPunchDamage(x, y, layer);
            var key = (x, y, layer);
            WorldItemChange[] itemChanges = Array.Empty<WorldItemChange>();
            if (remaining == 0)
            {
                _damaged.Remove(key);
                Reward[] rewards = DropCatalog.Resolve(current, _chance);
                itemChanges = InsertRewards(rewards, x, y);
            }
            else
            {
                _damaged[key] = TickIndex;
            }

            return EditResult.Ok(_grid.CopyCell(x, y), itemChanges);
        }
    }

    public EditResult TryPlaceSelected(int playerId, int x, int y, PlayerStore store)
    {
        lock (_gate)
        {
            if (!_occupants.TryGetValue(playerId, out Occupant? occupant))
            {
                return EditResult.Fail(EditStatus.NotInWorld);
            }

            if (!store.TryPeekSelected(playerId, out ItemDefinition item, out int slotIndex))
            {
                return EditResult.Fail(EditStatus.EmptySlot);
            }

            if (!item.Placeable || item.PlacesBlockId == 0)
            {
                return EditResult.Fail(EditStatus.Unplaceable);
            }

            if (!BlockCatalog.TryGet(item.PlacesBlockId, out BlockDefinition definition))
            {
                return EditResult.Fail(EditStatus.InvalidBlock);
            }

            TileLayer layer = definition.Layer;
            if (layer == TileLayer.HyperForeground)
            {
                return EditResult.Fail(EditStatus.LayerNotAllowed);
            }

            if (!Size.Contains(x, y))
            {
                return EditResult.Fail(EditStatus.OutOfBounds);
            }

            if (!InteractionRange.ContainsFeet(occupant.X, occupant.Y, x, y))
            {
                return EditResult.Fail(EditStatus.OutOfRange);
            }

            if (_grid.Get(x, y, layer) != BlockId.Empty)
            {
                return EditResult.Fail(EditStatus.Occupied);
            }

            if (!definition.Placeable)
            {
                return EditResult.Fail(EditStatus.LayerNotAllowed);
            }

            if (!store.TryConsumeSlot(playerId, slotIndex, out SlotDelta consumed))
            {
                return EditResult.Fail(EditStatus.EmptySlot);
            }

            _grid.Set(x, y, layer, item.PlacesBlockId);
            _damaged.Remove((x, y, layer));
            return EditResult.Ok(_grid.CopyCell(x, y), Array.Empty<WorldItemChange>(), new[] { consumed });
        }
    }

    internal WorldItemSnapshot[] SnapshotItems()
    {
        lock (_gate)
        {
            return CopyItemSnapshots();
        }
    }

    internal WorldItemChange[] InsertRewards(Reward[] rewards, int cellX, int cellY)
    {
        lock (_gate)
        {
            if (rewards.Length == 0)
            {
                return Array.Empty<WorldItemChange>();
            }

            var changes = new List<WorldItemChange>();
            for (int i = 0; i < rewards.Length; i++)
            {
                InsertReward(rewards[i], cellX, cellY, changes);
            }

            return DedupUpserts(changes);
        }
    }

    private bool IsForegroundSolid(int x, int y)
    {
        return BlockCatalog.IsSolid(_grid.Get(x, y, TileLayer.Foreground));
    }

    private WorldItemSnapshot[] CopyItemSnapshots()
    {
        if (_items.Count == 0)
        {
            return Array.Empty<WorldItemSnapshot>();
        }

        var snapshots = new WorldItemSnapshot[_items.Count];
        int i = 0;
        foreach (WorldItem item in _items.Values.OrderBy(existing => existing.Id))
        {
            snapshots[i++] = item.Snapshot();
        }

        return snapshots;
    }

    private void InsertReward(Reward reward, int cellX, int cellY, List<WorldItemChange> changes)
    {
        float x = cellX + _chance.NextRange(ItemConfig.DropInsetMin, ItemConfig.DropInsetMax);
        float y = cellY + _chance.NextRange(ItemConfig.DropInsetMin, ItemConfig.DropInsetMax);
        int remaining = reward.Quantity;
        int maxStack = WorldMaxStack(reward.Kind, reward.TypeId);
        float radius = ItemConfig.WorldItemMergeRadius;
        float radiusSq = radius * radius;

        List<WorldItem> candidates = _items.Values
            .Where(existing =>
                existing.Kind == reward.Kind
                && existing.TypeId == reward.TypeId
                && DistanceSquared(existing.X, existing.Y, x, y) <= radiusSq)
            .OrderBy(existing => existing.Id)
            .ToList();

        for (int i = 0; i < candidates.Count && remaining > 0; i++)
        {
            WorldItem candidate = candidates[i];
            int space = maxStack - candidate.Quantity;
            if (space <= 0)
            {
                continue;
            }

            int take = Math.Min(space, remaining);
            candidate.Quantity += take;
            remaining -= take;
            changes.Add(WorldItemChange.Upsert(candidate.Snapshot()));
        }

        while (remaining > 0)
        {
            int take = Math.Min(maxStack, remaining);
            var item = new WorldItem(_nextItemId++, reward.Kind, reward.TypeId, take, x, y);
            _items.Add(item.Id, item);
            remaining -= take;
            changes.Add(WorldItemChange.Upsert(item.Snapshot()));
        }
    }

    private static WorldItemChange[] DedupUpserts(List<WorldItemChange> changes)
    {
        if (changes.Count == 0)
        {
            return Array.Empty<WorldItemChange>();
        }

        var latest = new Dictionary<int, WorldItemChange>();
        for (int i = 0; i < changes.Count; i++)
        {
            latest[changes[i].Item.Id] = changes[i];
        }

        return latest.Values.OrderBy(change => change.Item.Id).ToArray();
    }

    private static int WorldMaxStack(RewardKind kind, ushort typeId)
    {
        return kind == RewardKind.Currency
            ? CurrencyCatalog.WorldMaxStack(typeId)
            : ItemCatalog.MaxStack(typeId);
    }

    private static float DistanceSquared(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return dx * dx + dy * dy;
    }

    private void CollectPickups(PlayerStore players, out WorldItemChange[] itemChanges, out OwnerCredit[] credits)
    {
        if (_items.Count == 0)
        {
            itemChanges = Array.Empty<WorldItemChange>();
            credits = Array.Empty<OwnerCredit>();
            return;
        }

        var changes = new List<WorldItemChange>();
        var creditList = new List<OwnerCredit>();
        Occupant[] occupants = _occupants.Values.OrderBy(occupant => occupant.Id).ToArray();
        WorldItem[] items = _items.Values.OrderBy(item => item.Id).ToArray();

        for (int i = 0; i < items.Length; i++)
        {
            WorldItem item = items[i];
            Occupant? winner = null;
            for (int o = 0; o < occupants.Length; o++)
            {
                if (OverlapsPickup(occupants[o], item))
                {
                    winner = occupants[o];
                    break;
                }
            }

            if (winner is null)
            {
                continue;
            }

            if (item.Kind == RewardKind.Currency)
            {
                int taken = item.Quantity;
                int balance = players.AddGems(winner.Id, taken);
                creditList.Add(new OwnerCredit(
                    winner.Id,
                    Array.Empty<SlotDelta>(),
                    balance,
                    new[] { new PickupNotice(RewardKind.Currency, item.TypeId, taken) }));
                _items.Remove(item.Id);
                changes.Add(WorldItemChange.Remove(item.Id));
                continue;
            }

            int original = item.Quantity;
            int leftover = players.TryAddItem(winner.Id, item.TypeId, original, out SlotDelta[] slots);
            int takenItems = original - leftover;
            if (takenItems <= 0)
            {
                continue;
            }

            creditList.Add(new OwnerCredit(
                winner.Id,
                slots,
                null,
                new[] { new PickupNotice(RewardKind.Item, item.TypeId, takenItems) }));
            if (leftover <= 0)
            {
                _items.Remove(item.Id);
                changes.Add(WorldItemChange.Remove(item.Id));
            }
            else
            {
                item.Quantity = leftover;
                changes.Add(WorldItemChange.Upsert(item.Snapshot()));
            }
        }

        itemChanges = changes.ToArray();
        credits = creditList.ToArray();
    }

    private static bool OverlapsPickup(Occupant occupant, WorldItem item)
    {
        float pad = ItemConfig.PickupInflate;
        float halfW = (MovementConfig.BodyWidth * 0.5f) + pad;
        float left = occupant.X - halfW;
        float right = occupant.X + halfW;
        float bottom = occupant.Y - pad;
        float top = occupant.Y + MovementConfig.BodyHeight + pad;
        return item.X >= left && item.X <= right && item.Y >= bottom && item.Y <= top;
    }

    private GridCellSnapshot[] RestoreDamagedBlocks()
    {
        if (_damaged.Count == 0)
        {
            return Array.Empty<GridCellSnapshot>();
        }

        List<(int x, int y, TileLayer layer)>? restored = null;
        foreach (KeyValuePair<(int x, int y, TileLayer layer), int> pair in _damaged)
        {
            ushort blockId = _grid.Get(pair.Key.x, pair.Key.y, pair.Key.layer);
            if (blockId == BlockId.Empty || !BlockCatalog.TryGet(blockId, out BlockDefinition definition))
            {
                restored ??= new List<(int, int, TileLayer)>();
                restored.Add(pair.Key);
                continue;
            }

            if (definition.RegenDelayTicks <= 0)
            {
                continue;
            }

            if (TickIndex - pair.Value < definition.RegenDelayTicks)
            {
                continue;
            }

            _grid.RestoreToMaxHealth(pair.Key.x, pair.Key.y, pair.Key.layer);
            restored ??= new List<(int, int, TileLayer)>();
            restored.Add(pair.Key);
        }

        if (restored is null)
        {
            return Array.Empty<GridCellSnapshot>();
        }

        var cells = new GridCellSnapshot[restored.Count];
        for (int i = 0; i < restored.Count; i++)
        {
            (int x, int y, TileLayer layer) = restored[i];
            _damaged.Remove((x, y, layer));
            cells[i] = _grid.CopyCell(x, y);
        }

        return cells;
    }

    private bool TryResolvePunchLayer(int x, int y, out TileLayer layer, out ushort blockId)
    {
        ushort foreground = _grid.Get(x, y, TileLayer.Foreground);
        if (foreground != BlockId.Empty)
        {
            layer = TileLayer.Foreground;
            blockId = foreground;
            return true;
        }

        ushort background = _grid.Get(x, y, TileLayer.Background);
        if (background != BlockId.Empty)
        {
            layer = TileLayer.Background;
            blockId = background;
            return true;
        }

        layer = TileLayer.Background;
        blockId = BlockId.Empty;
        return false;
    }

    private sealed class Occupant
    {
        private long _inputWindowStartMs;
        private int _inputsInWindow;
        private int _ticksSinceInput = MovementConfig.InputTimeoutTicks;

        public Occupant(int id, string name, int spawnCellX, int spawnCellY)
        {
            Id = id;
            Name = name;
            X = spawnCellX + 0.5f;
            Y = spawnCellY;
        }

        public int Id { get; }
        public string Name { get; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Vx { get; private set; }
        public float Vy { get; private set; }
        public bool Grounded { get; private set; }
        public uint LastProcessedSeq { get; private set; }

        public bool HeldLeft { get; private set; }
        public bool HeldRight { get; private set; }
        public bool JumpDownLatched { get; private set; }
        public uint HighestAcceptedSeq { get; private set; }
        public bool HasUnconsumedSample { get; private set; }

        public InputAcceptStatus MergeInput(uint seq, bool left, bool right, bool jumpDown)
        {
            if (!TryAcceptRate())
            {
                return InputAcceptStatus.Dropped;
            }

            if (seq <= LastProcessedSeq)
            {
                return InputAcceptStatus.Dropped;
            }

            HeldLeft = left;
            HeldRight = right;
            JumpDownLatched |= jumpDown;
            if (seq > HighestAcceptedSeq)
            {
                HighestAcceptedSeq = seq;
            }

            HasUnconsumedSample = true;
            return InputAcceptStatus.Accepted;
        }

        public void Simulate(float dt, WorldSize size, PlayerMovement.SolidQuery isForegroundSolid)
        {
            bool jumpDown = false;
            if (HasUnconsumedSample)
            {
                LastProcessedSeq = HighestAcceptedSeq;
                jumpDown = JumpDownLatched;
                JumpDownLatched = false;
                HasUnconsumedSample = false;
                _ticksSinceInput = 0;
            }
            else
            {
                _ticksSinceInput++;
                if (_ticksSinceInput >= MovementConfig.InputTimeoutTicks)
                {
                    HeldLeft = false;
                    HeldRight = false;
                }
            }

            var movement = new MovementState
            {
                X = X,
                Y = Y,
                Vx = Vx,
                Vy = Vy,
                Grounded = Grounded
            };
            PlayerMovement.Integrate(
                ref movement,
                new MovementInput(HeldLeft, HeldRight, jumpDown),
                dt,
                size,
                isForegroundSolid);
            X = movement.X;
            Y = movement.Y;
            Vx = movement.Vx;
            Vy = movement.Vy;
            Grounded = movement.Grounded;
        }

        public PlayerSnapshot Snapshot()
            => new(Id, Name, X, Y, Vx, Vy, Grounded, LastProcessedSeq);

        public PlayerSimSnapshot SimSnapshot(int tick)
            => new(tick, Id, X, Y, Vx, Vy, Grounded, LastProcessedSeq);

        private bool TryAcceptRate()
        {
            long now = Environment.TickCount64;
            if (now - _inputWindowStartMs >= 1000)
            {
                _inputWindowStartMs = now;
                _inputsInWindow = 0;
            }

            if (_inputsInWindow >= MovementConfig.MaxInputsPerSecond)
            {
                return false;
            }

            _inputsInWindow++;
            return true;
        }
    }
}

internal enum EnterStatus
{
    Ok,
    AlreadyInWorld
}

internal readonly struct EnterResult
{
    public EnterStatus Status { get; }
    public PlayerSnapshot Self { get; }
    public PlayerSnapshot[] Others { get; }
    public GridSnapshot Grid { get; }
    public WorldItemSnapshot[] Items { get; }

    private EnterResult(
        EnterStatus status,
        PlayerSnapshot self,
        PlayerSnapshot[] others,
        GridSnapshot grid,
        WorldItemSnapshot[] items)
    {
        Status = status;
        Self = self;
        Others = others;
        Grid = grid;
        Items = items;
    }

    public static EnterResult Ok(PlayerSnapshot self, PlayerSnapshot[] others, GridSnapshot grid, WorldItemSnapshot[] items)
        => new(EnterStatus.Ok, self, others, grid, items);

    public static EnterResult Fail(EnterStatus status)
        => new(status, default, Array.Empty<PlayerSnapshot>(), new GridSnapshot(WorldSize.Default, Array.Empty<GridCellSnapshot>()), Array.Empty<WorldItemSnapshot>());
}

internal enum InputAcceptStatus
{
    Accepted,
    Dropped,
    NotInWorld
}

internal enum EditStatus
{
    Ok,
    NotInWorld,
    OutOfBounds,
    OutOfRange,
    EmptyCell,
    Occupied,
    InvalidBlock,
    LayerNotAllowed,
    Unbreakable,
    Unplaceable,
    EmptySlot
}

internal readonly struct EditResult
{
    public EditStatus Status { get; }
    public GridCellSnapshot Cell { get; }
    public WorldItemChange[] ItemChanges { get; }
    public SlotDelta[] OwnerSlots { get; }

    private EditResult(EditStatus status, GridCellSnapshot cell, WorldItemChange[] itemChanges, SlotDelta[] ownerSlots)
    {
        Status = status;
        Cell = cell;
        ItemChanges = itemChanges;
        OwnerSlots = ownerSlots;
    }

    public static EditResult Ok(
        GridCellSnapshot cell,
        WorldItemChange[]? itemChanges = null,
        SlotDelta[]? ownerSlots = null)
        => new(EditStatus.Ok, cell, itemChanges ?? Array.Empty<WorldItemChange>(), ownerSlots ?? Array.Empty<SlotDelta>());

    public static EditResult Fail(EditStatus status)
        => new(status, default, Array.Empty<WorldItemChange>(), Array.Empty<SlotDelta>());
}

internal readonly struct PlayerSnapshot
{
    public PlayerSnapshot(int id, string name, float x, float y, float vx, float vy, bool grounded, uint lastProcessedSeq)
    {
        Id = id;
        Name = name;
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Grounded = grounded;
        LastProcessedSeq = lastProcessedSeq;
    }

    public int Id { get; }
    public string Name { get; }
    public float X { get; }
    public float Y { get; }
    public float Vx { get; }
    public float Vy { get; }
    public bool Grounded { get; }
    public uint LastProcessedSeq { get; }
}

internal readonly struct PlayerSimSnapshot
{
    public PlayerSimSnapshot(int tick, int id, float x, float y, float vx, float vy, bool grounded, uint ackSeq)
    {
        Tick = tick;
        Id = id;
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Grounded = grounded;
        AckSeq = ackSeq;
    }

    public int Tick { get; }
    public int Id { get; }
    public float X { get; }
    public float Y { get; }
    public float Vx { get; }
    public float Vy { get; }
    public bool Grounded { get; }
    public uint AckSeq { get; }
}

internal readonly struct WorldTickResult
{
    public static WorldTickResult Empty { get; } = new(
        0,
        Array.Empty<PlayerSimSnapshot>(),
        Array.Empty<GridCellSnapshot>(),
        Array.Empty<WorldItemChange>(),
        Array.Empty<OwnerCredit>());

    public WorldTickResult(
        int tick,
        PlayerSimSnapshot[] players,
        GridCellSnapshot[] regenerated,
        WorldItemChange[] itemChanges,
        OwnerCredit[] credits)
    {
        Tick = tick;
        Players = players;
        Regenerated = regenerated;
        ItemChanges = itemChanges;
        Credits = credits;
    }

    public int Tick { get; }
    public PlayerSimSnapshot[] Players { get; }
    public GridCellSnapshot[] Regenerated { get; }
    public WorldItemChange[] ItemChanges { get; }
    public OwnerCredit[] Credits { get; }

    public bool HasBroadcasts =>
        Players.Length > 0
        || Regenerated.Length > 0
        || ItemChanges.Length > 0
        || Credits.Length > 0;
}
