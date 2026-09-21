namespace SandboxServer;

internal static class ChatRules
{
    public static bool TryNormalize(string? raw, out string? text)
    {
        text = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Length > ChatConfig.MaxMessageLength)
        {
            return false;
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (char.IsControl(trimmed[i]))
            {
                return false;
            }
        }

        text = trimmed;
        return true;
    }
}
