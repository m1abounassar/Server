namespace SandboxServer;

/// <summary>
/// Session inventory and Gem balance. Not world membership.
/// Lock after World._gate when both are needed.
/// </summary>
internal sealed class PlayerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<int, PlayerEconomy> _players = new();

    public void Ensure(int playerId)
    {
        lock (_gate)
        {
            if (!_players.ContainsKey(playerId))
            {
                _players.Add(playerId, new PlayerEconomy());
            }
        }
    }

    public void Remove(int playerId)
    {
        lock (_gate)
        {
            _players.Remove(playerId);
        }
    }

    public int GemBalance(int playerId)
    {
        lock (_gate)
        {
            return _players.TryGetValue(playerId, out PlayerEconomy? economy) ? economy.GemBalance : 0;
        }
    }

    public int SelectedSlot(int playerId)
    {
        lock (_gate)
        {
            return _players.TryGetValue(playerId, out PlayerEconomy? economy) ? economy.SelectedSlot : 0;
        }
    }

    public SlotDelta[] OccupiedSlots(int playerId)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out PlayerEconomy? economy))
            {
                return Array.Empty<SlotDelta>();
            }

            var list = new List<SlotDelta>();
            for (int i = 0; i < economy.Slots.Length; i++)
            {
                InventorySlot slot = economy.Slots[i];
                if (slot.Quantity > 0)
                {
                    list.Add(new SlotDelta(i, slot.ItemId, slot.Quantity));
                }
            }

            return list.ToArray();
        }
    }

    public bool TrySelect(int playerId, int slot)
    {
        return TrySelect(playerId, slot, out _);
    }

    public bool TrySelect(int playerId, int slot, out string? error)
    {
        error = "invalid_slot";
        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out PlayerEconomy? economy))
            {
                return false;
            }

            if (slot < 0 || slot >= economy.Slots.Length)
            {
                return false;
            }

            if (slot >= ItemConfig.FirstCollectibleSlot && economy.Slots[slot].Quantity <= 0)
            {
                error = "empty_slot";
                return false;
            }

            economy.SelectedSlot = slot;
            error = null;
            return true;
        }
    }

    public int AddGems(int playerId, int quantity)
    {
        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out PlayerEconomy? economy) || quantity <= 0)
            {
                return 0;
            }

            economy.GemBalance += quantity;
            return economy.GemBalance;
        }
    }

    public int TryAddItem(int playerId, ushort itemId, int quantity, out SlotDelta[] changes)
    {
        changes = Array.Empty<SlotDelta>();
        if (quantity <= 0 || !ItemCatalog.TryGet(itemId, out ItemDefinition definition) || definition.Permanent)
        {
            return quantity;
        }

        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out PlayerEconomy? economy))
            {
                return quantity;
            }

            int remaining = quantity;
            int maxStack = definition.MaxStack;
            var deltas = new List<SlotDelta>();

            for (int i = ItemConfig.FirstCollectibleSlot; i < economy.Slots.Length && remaining > 0; i++)
            {
                InventorySlot slot = economy.Slots[i];
                if (slot.Quantity <= 0 || slot.ItemId != itemId)
                {
                    continue;
                }

                int space = maxStack - slot.Quantity;
                if (space <= 0)
                {
                    continue;
                }

                int take = Math.Min(space, remaining);
                slot.Quantity += take;
                economy.Slots[i] = slot;
                remaining -= take;
                deltas.Add(new SlotDelta(i, itemId, slot.Quantity));
            }

            for (int i = ItemConfig.FirstCollectibleSlot; i < economy.Slots.Length && remaining > 0; i++)
            {
                if (economy.Slots[i].Quantity > 0)
                {
                    continue;
                }

                int take = Math.Min(maxStack, remaining);
                economy.Slots[i] = new InventorySlot(itemId, take);
                remaining -= take;
                deltas.Add(new SlotDelta(i, itemId, take));
            }

            changes = deltas.ToArray();
            return remaining;
        }
    }

    public bool TryPeekSelected(int playerId, out ItemDefinition item, out int slotIndex)
    {
        item = default;
        slotIndex = 0;
        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out PlayerEconomy? economy))
            {
                return false;
            }

            slotIndex = economy.SelectedSlot;
            InventorySlot slot = economy.Slots[slotIndex];
            if (slot.Quantity <= 0 || !ItemCatalog.TryGet(slot.ItemId, out item))
            {
                return false;
            }

            return true;
        }
    }

    public bool TryConsumeSlot(int playerId, int slotIndex, out SlotDelta slotChange)
    {
        slotChange = default;
        lock (_gate)
        {
            if (!_players.TryGetValue(playerId, out PlayerEconomy? economy))
            {
                return false;
            }

            if (slotIndex < ItemConfig.FirstCollectibleSlot || slotIndex >= economy.Slots.Length)
            {
                return false;
            }

            InventorySlot slot = economy.Slots[slotIndex];
            if (slot.Quantity <= 0)
            {
                return false;
            }

            slot.Quantity--;
            if (slot.Quantity <= 0)
            {
                slot = default;
                economy.SelectedSlot = ItemConfig.FistSlot;
            }

            economy.Slots[slotIndex] = slot;
            slotChange = new SlotDelta(slotIndex, slot.ItemId, slot.Quantity);
            return true;
        }
    }

    private sealed class PlayerEconomy
    {
        public PlayerEconomy()
        {
            foreach (ItemDefinition item in ItemCatalog.LoadoutItems())
            {
                if (item.LoadoutSlot is int slot && slot >= 0 && slot < Slots.Length)
                {
                    Slots[slot] = new InventorySlot(item.Id, 1);
                }
            }

            SelectedSlot = ItemConfig.FistSlot;
        }

        public InventorySlot[] Slots { get; } = new InventorySlot[ItemConfig.InventoryCapacity];
        public int SelectedSlot { get; set; }
        public int GemBalance { get; set; }
    }

    private struct InventorySlot
    {
        public InventorySlot(ushort itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public ushort ItemId;
        public int Quantity;
    }
}
