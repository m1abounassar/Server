namespace SandboxServer;

internal static class ContentValidator
{
    public static List<string> Validate(ContentPack pack)
    {
        var errors = new List<string>();
        if (pack.SchemaVersion < 1)
        {
            errors.Add("schemaVersion must be >= 1.");
        }

        if (pack.ContentRevision < 1)
        {
            errors.Add("contentRevision must be >= 1.");
        }

        var blockIds = new HashSet<ushort>();
        var blockKeys = new HashSet<string>(StringComparer.Ordinal);
        var retiredBlocks = new HashSet<ushort>();
        foreach (BlockDto block in pack.Blocks)
        {
            ValidateId(block.Id, "block", errors);
            ValidateKey(block.Key, "block", errors);
            if (!blockIds.Add((ushort)block.Id))
            {
                errors.Add("Duplicate block id " + block.Id + ".");
            }

            if (!blockKeys.Add(block.Key))
            {
                errors.Add("Duplicate block key '" + block.Key + "'.");
            }

            if (block.Retired)
            {
                retiredBlocks.Add((ushort)block.Id);
                continue;
            }

            if (block.MaxHealth < 1 || block.MaxHealth > byte.MaxValue)
            {
                errors.Add("Block '" + block.Key + "' maxHealth out of range.");
            }

            if (block.RegenDelayTicks < 0)
            {
                errors.Add("Block '" + block.Key + "' regenDelayTicks cannot be negative.");
            }

            if (!TryParseLayer(block.Layer, out _))
            {
                errors.Add("Block '" + block.Key + "' has invalid layer. Use Background, Foreground, or HyperForeground.");
            }
        }

        var itemIds = new HashSet<ushort>();
        var itemKeys = new HashSet<string>(StringComparer.Ordinal);
        var retiredItems = new HashSet<ushort>();
        var loadoutSlots = new HashSet<int>();
        var permanentIds = new HashSet<ushort>();
        foreach (ItemDto item in pack.Items)
        {
            ValidateId(item.Id, "item", errors);
            ValidateKey(item.Key, "item", errors);
            if (!itemIds.Add((ushort)item.Id))
            {
                errors.Add("Duplicate item id " + item.Id + ".");
            }

            if (!itemKeys.Add(item.Key))
            {
                errors.Add("Duplicate item key '" + item.Key + "'.");
            }

            if (item.Retired)
            {
                retiredItems.Add((ushort)item.Id);
                continue;
            }

            if (item.MaxStack < 1)
            {
                errors.Add("Item '" + item.Key + "' maxStack must be >= 1.");
            }

            if (item.Identity is not ("stackable" or "uniqueInstance"))
            {
                errors.Add("Item '" + item.Key + "' identity must be stackable or uniqueInstance.");
            }

            if (item.Placement is PlacementDto placement)
            {
                if (placement.BlockId <= 0 || placement.BlockId > ushort.MaxValue
                    || !blockIds.Contains((ushort)placement.BlockId)
                    || retiredBlocks.Contains((ushort)placement.BlockId))
                {
                    errors.Add("Item '" + item.Key + "' placement.blockId is missing or retired.");
                }
                else
                {
                    BlockDto? target = pack.Blocks.Find(candidate => candidate.Id == placement.BlockId);
                    if (target is null || !target.Placeable)
                    {
                        errors.Add("Item '" + item.Key + "' places a non-placeable block.");
                    }
                    else if (!TryParseLayer(target.Layer, out _))
                    {
                        errors.Add("Item '" + item.Key + "' places a block with an invalid layer.");
                    }
                }
            }

            if (item.Loadout is LoadoutDto loadout)
            {
                if (loadout.Slot < 0)
                {
                    errors.Add("Item '" + item.Key + "' loadout.slot is invalid.");
                }
                else if (!loadoutSlots.Add(loadout.Slot))
                {
                    errors.Add("Duplicate loadout slot " + loadout.Slot + ".");
                }

                if (loadout.Permanent)
                {
                    permanentIds.Add((ushort)item.Id);
                }
            }
        }

        var currencyIds = new HashSet<ushort>();
        var currencyKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (CurrencyDto currency in pack.Currencies)
        {
            ValidateId(currency.Id, "currency", errors);
            ValidateKey(currency.Key, "currency", errors);
            if (!currencyIds.Add((ushort)currency.Id))
            {
                errors.Add("Duplicate currency id " + currency.Id + ".");
            }

            if (!currencyKeys.Add(currency.Key))
            {
                errors.Add("Duplicate currency key '" + currency.Key + "'.");
            }

            if (currency.WorldMaxStack < 1)
            {
                errors.Add("Currency '" + currency.Key + "' worldMaxStack must be >= 1.");
            }
        }

        var dropBlocks = new HashSet<ushort>();
        foreach (DropTableDto table in pack.Drops)
        {
            if (table.BlockId <= 0 || table.BlockId > ushort.MaxValue || !blockIds.Contains((ushort)table.BlockId))
            {
                errors.Add("Drop table references unknown block id " + table.BlockId + ".");
                continue;
            }

            if (retiredBlocks.Contains((ushort)table.BlockId))
            {
                errors.Add("Drop table references retired block id " + table.BlockId + ".");
            }

            if (!dropBlocks.Add((ushort)table.BlockId))
            {
                errors.Add("Duplicate drop table for block id " + table.BlockId + ".");
            }

            foreach (DropRollDto roll in table.Rolls)
            {
                if (roll.Chance < 0f || roll.Chance > 1f || roll.MinQuantity < 1 || roll.MaxQuantity < roll.MinQuantity)
                {
                    errors.Add("Invalid drop roll on block " + table.BlockId + ".");
                }

                if (roll.Kind == "item")
                {
                    if (!itemIds.Contains((ushort)roll.TypeId) || retiredItems.Contains((ushort)roll.TypeId))
                    {
                        errors.Add("Drop roll references unknown or retired item " + roll.TypeId + ".");
                    }
                    else if (permanentIds.Contains((ushort)roll.TypeId))
                    {
                        errors.Add("Permanent loadout item " + roll.TypeId + " cannot appear in drop tables.");
                    }
                }
                else if (roll.Kind == "currency")
                {
                    if (!currencyIds.Contains((ushort)roll.TypeId))
                    {
                        errors.Add("Drop roll references unknown currency " + roll.TypeId + ".");
                    }
                }
                else
                {
                    errors.Add("Drop roll has invalid kind '" + roll.Kind + "'.");
                }
            }
        }

        return errors;
    }

    private static void ValidateId(int id, string kind, List<string> errors)
    {
        if (id <= 0 || id > ushort.MaxValue)
        {
            errors.Add("Invalid " + kind + " id " + id + " (0 is reserved).");
        }
    }

    private static void ValidateKey(string key, string kind, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            errors.Add("Empty " + kind + " key.");
        }
    }

    internal static bool TryParseLayer(string token, out TileLayer layer)
    {
        switch (token)
        {
            case "Background":
                layer = TileLayer.Background;
                return true;
            case "Foreground":
                layer = TileLayer.Foreground;
                return true;
            case "HyperForeground":
                layer = TileLayer.HyperForeground;
                return true;
            default:
                layer = TileLayer.Background;
                return false;
        }
    }
}
