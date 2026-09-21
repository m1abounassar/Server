using System.Text.Json.Serialization;

namespace SandboxServer;

internal sealed class PackHeaderDto
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("contentRevision")]
    public int ContentRevision { get; set; }
}

internal sealed class BlockFileDto
{
    [JsonPropertyName("blocks")]
    public List<BlockDto> Blocks { get; set; } = new();
}

internal sealed class BlockDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("retired")]
    public bool Retired { get; set; }

    [JsonPropertyName("layer")]
    public string Layer { get; set; } = string.Empty;

    [JsonPropertyName("breakable")]
    public bool Breakable { get; set; }

    [JsonPropertyName("placeable")]
    public bool Placeable { get; set; }

    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; }

    [JsonPropertyName("solid")]
    public bool Solid { get; set; }

    [JsonPropertyName("regenDelayTicks")]
    public int RegenDelayTicks { get; set; }
}

internal sealed class ItemFileDto
{
    [JsonPropertyName("items")]
    public List<ItemDto> Items { get; set; } = new();
}

internal sealed class ItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("maxStack")]
    public int MaxStack { get; set; }

    [JsonPropertyName("identity")]
    public string Identity { get; set; } = "stackable";

    [JsonPropertyName("retired")]
    public bool Retired { get; set; }

    [JsonPropertyName("placement")]
    public PlacementDto? Placement { get; set; }

    [JsonPropertyName("loadout")]
    public LoadoutDto? Loadout { get; set; }
}

internal sealed class PlacementDto
{
    [JsonPropertyName("blockId")]
    public int BlockId { get; set; }
}

internal sealed class LoadoutDto
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("permanent")]
    public bool Permanent { get; set; }
}

internal sealed class CurrencyFileDto
{
    [JsonPropertyName("currencies")]
    public List<CurrencyDto> Currencies { get; set; } = new();
}

internal sealed class CurrencyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("worldMaxStack")]
    public int WorldMaxStack { get; set; }
}

internal sealed class DropFileDto
{
    [JsonPropertyName("drops")]
    public List<DropTableDto> Drops { get; set; } = new();
}

internal sealed class DropTableDto
{
    [JsonPropertyName("blockId")]
    public int BlockId { get; set; }

    [JsonPropertyName("rolls")]
    public List<DropRollDto> Rolls { get; set; } = new();
}

internal sealed class DropRollDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("typeId")]
    public int TypeId { get; set; }

    [JsonPropertyName("chance")]
    public float Chance { get; set; }

    [JsonPropertyName("minQuantity")]
    public int MinQuantity { get; set; }

    [JsonPropertyName("maxQuantity")]
    public int MaxQuantity { get; set; }
}

internal sealed class ContentPack
{
    public int SchemaVersion { get; init; }
    public int ContentRevision { get; init; }
    public List<BlockDto> Blocks { get; init; } = new();
    public List<ItemDto> Items { get; init; } = new();
    public List<CurrencyDto> Currencies { get; init; } = new();
    public List<DropTableDto> Drops { get; init; } = new();
}
