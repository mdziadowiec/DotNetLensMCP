namespace DotNetLensMcp;

internal static class McpToolNames
{
    private const string LegacyPrefix = "roslyn:";
    private const string CurrentPrefix = "roslyn_";

    internal static string? Normalize(string? name) =>
        name?.StartsWith(LegacyPrefix, StringComparison.Ordinal) == true
            ? $"{CurrentPrefix}{name[LegacyPrefix.Length..]}"
            : name;
}
