using System.Globalization;
using System.Text;

namespace SandboxServer;

/// <summary>
/// Temporary newline-delimited UTF-8 protocol.
/// Framing and text verbs are scaffolding; not a permanent serialization choice.
/// JOIN is identity. ENTER/LEAVE are world membership.
/// INPUT is sequenced movement intent. PLAYER_STATE is a periodic replica snapshot.
/// CHAT is rest-of-line text. COLLECTED is an owner-only pickup fact.
/// </summary>
internal static class Protocol
{
    public const int MaxLineBytes = 4096;

    internal static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] EncodeLine(string line) => Utf8.GetBytes(line + "\n");

    public static bool TryParseClientLine(string line, out ClientCommand? command, out string? error)
    {
        command = null;
        error = null;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "empty_command";
            return false;
        }

        switch (parts[0].ToUpperInvariant())
        {
            case "JOIN":
                if (parts.Length != 2)
                {
                    error = "invalid_command";
                    return false;
                }

                command = new JoinCommand(parts[1]);
                return true;

            case "ENTER":
                if (parts.Length != 2)
                {
                    error = "invalid_command";
                    return false;
                }

                command = new EnterCommand(parts[1]);
                return true;

            case "LEAVE":
                if (parts.Length != 1)
                {
                    error = "invalid_command";
                    return false;
                }

                command = new LeaveCommand();
                return true;

            case "INPUT":
                if (parts.Length != 5
                    || !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint seq)
                    || !TryParseFlag(parts[2], out bool left)
                    || !TryParseFlag(parts[3], out bool right)
                    || !TryParseFlag(parts[4], out bool jump))
                {
                    error = "invalid_command";
                    return false;
                }

                command = new InputCommand(seq, left, right, jump);
                return true;

            case "BREAK":
                if (parts.Length != 3
                    || !int.TryParse(parts[1], out int breakX)
                    || !int.TryParse(parts[2], out int breakY))
                {
                    error = "invalid_command";
                    return false;
                }

                command = new BreakCommand(breakX, breakY);
                return true;

            case "PLACE":
                if (parts.Length != 3
                    || !int.TryParse(parts[1], out int placeX)
                    || !int.TryParse(parts[2], out int placeY))
                {
                    error = "invalid_command";
                    return false;
                }

                command = new PlaceCommand(placeX, placeY);
                return true;

            case "SELECT":
                if (parts.Length != 2 || !int.TryParse(parts[1], out int slot) || slot < 0)
                {
                    error = "invalid_command";
                    return false;
                }

                command = new SelectCommand(slot);
                return true;

            case "CHAT":
            {
                int firstSpace = line.IndexOf(' ');
                if (firstSpace < 0 || firstSpace + 1 >= line.Length)
                {
                    error = "invalid_command";
                    return false;
                }

                if (!ChatRules.TryNormalize(line[(firstSpace + 1)..], out string? text) || text is null)
                {
                    error = "invalid_chat";
                    return false;
                }

                command = new ChatCommand(text);
                return true;
            }

            default:
                error = "unknown_command";
                return false;
        }
    }

    public static bool TryParseFlag(string token, out bool value)
    {
        if (token == "0")
        {
            value = false;
            return true;
        }

        if (token == "1")
        {
            value = true;
            return true;
        }

        value = false;
        return false;
    }

    public static bool TryParseLayer(string token, out TileLayer layer)
    {
        switch (token.ToLowerInvariant())
        {
            case "bg":
                layer = TileLayer.Background;
                return true;
            case "fg":
                layer = TileLayer.Foreground;
                return true;
            case "hg":
                layer = TileLayer.HyperForeground;
                return true;
            default:
                layer = TileLayer.Background;
                return false;
        }
    }

    public static string FormatLayer(TileLayer layer)
    {
        return layer switch
        {
            TileLayer.Foreground => "fg",
            TileLayer.HyperForeground => "hg",
            _ => "bg"
        };
    }

    public static string Welcome(PlayerIdentity identity)
        => $"WELCOME {identity.Id} {identity.Name}";

    public static string ContentRev(int revision)
        => $"CONTENT_REV {revision}";

    public static string WorldEntered(string worldName, PlayerSnapshot player)
        => $"WORLD_ENTERED {worldName} {player.Id} {player.Name} {FormatFloat(player.X)} {FormatFloat(player.Y)}";

    public static string WorldLeft(string worldName)
        => $"WORLD_LEFT {worldName}";

    public static string PlayerJoined(PlayerSnapshot player)
        => $"PLAYER_JOINED {player.Id} {player.Name} {FormatFloat(player.X)} {FormatFloat(player.Y)}";

    public static string PlayerState(PlayerSimSnapshot player)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"PLAYER_STATE {player.Tick} {player.Id} {player.X:0.####} {player.Y:0.####} {player.Vx:0.####} {player.Vy:0.####} {(player.Grounded ? 1 : 0)} {player.AckSeq}");

    public static string PlayerLeft(int playerId)
        => $"PLAYER_LEFT {playerId}";

    public static string Error(string reason)
        => $"ERROR {reason}";

    public static string WorldGridHeader(string worldName, WorldSize size)
        => $"WORLD_GRID {worldName} {size.OriginX} {size.OriginY} {size.Width} {size.Height}";

    public static string Tile(GridCellSnapshot cell)
        => $"TILE {cell.X} {cell.Y} {cell.Background} {cell.Foreground} {cell.HyperForeground} {cell.BackgroundHealth} {cell.ForegroundHealth} {cell.HyperForegroundHealth}";

    public static string WorldGridEnd()
        => "WORLD_GRID_END";

    public static string WorldItemsBegin() => "WORLD_ITEMS";

    public static string WorldItemsEnd() => "WORLD_ITEMS_END";

    public static string Item(WorldItemSnapshot item)
    {
        string kind = item.Kind == RewardKind.Currency ? "gem" : "item";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"ITEM {item.Id} {kind} {item.TypeId} {item.Quantity} {item.X:0.####} {item.Y:0.####}");
    }

    public static string ItemRemoved(int id) => $"ITEM_REMOVED {id}";

    public static string Gems(int balance) => $"GEMS {balance}";

    public static string InventoryBegin(int capacity) => $"INVENTORY_BEGIN {capacity}";

    public static string InventoryEnd() => "INVENTORY_END";

    public static string InventorySlot(SlotDelta slot) => $"INV {slot.Slot} {slot.ItemId} {slot.Quantity}";

    public static string Selected(int slot) => $"SELECTED {slot}";

    public static string Chat(int playerId, string name, string text)
        => $"CHAT {playerId} {name} {text}";

    public static string CollectedGem(int quantity)
        => $"COLLECTED gem {quantity}";

    public static string CollectedItem(ushort itemId, int quantity)
        => $"COLLECTED item {itemId} {quantity}";

    private static string FormatFloat(float value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);
}

internal abstract record ClientCommand;

internal sealed record JoinCommand(string Name) : ClientCommand;

internal sealed record EnterCommand(string WorldName) : ClientCommand;

internal sealed record LeaveCommand : ClientCommand;

internal sealed record InputCommand(uint Seq, bool Left, bool Right, bool Jump) : ClientCommand;

internal sealed record BreakCommand(int X, int Y) : ClientCommand;

internal sealed record PlaceCommand(int X, int Y) : ClientCommand;

internal sealed record SelectCommand(int Slot) : ClientCommand;

internal sealed record ChatCommand(string Text) : ClientCommand;
