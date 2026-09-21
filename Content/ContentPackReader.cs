using System.Text.Json;

namespace SandboxServer;

internal static class ContentPackReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ContentPack Read(string directory)
    {
        PackHeaderDto header = ReadFile<PackHeaderDto>(directory, "pack.json");
        BlockFileDto blocks = ReadFile<BlockFileDto>(directory, "blocks.json");
        ItemFileDto items = ReadFile<ItemFileDto>(directory, "items.json");
        CurrencyFileDto currencies = ReadFile<CurrencyFileDto>(directory, "currencies.json");
        DropFileDto drops = ReadFile<DropFileDto>(directory, "drops.json");
        return new ContentPack
        {
            SchemaVersion = header.SchemaVersion,
            ContentRevision = header.ContentRevision,
            Blocks = blocks.Blocks,
            Items = items.Items,
            Currencies = currencies.Currencies,
            Drops = drops.Drops
        };
    }

    private static T ReadFile<T>(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Missing content file: " + path);
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("Empty content file: " + path);
    }
}
