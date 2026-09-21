using System.Text.RegularExpressions;

namespace SandboxServer;

internal static class NameRules
{
    public const int MaxLength = 16;

    private static readonly Regex SafeName = new(
        "^[A-Za-z0-9_]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValid(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.Length <= MaxLength
            && SafeName.IsMatch(name);
    }
}
