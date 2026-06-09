using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetLensMcp;

internal sealed class ToolArguments
{
    private readonly JsonObject? _arguments;

    internal ToolArguments(JsonObject? arguments)
    {
        _arguments = arguments;
    }

    internal string RequiredString(string name) => RequiredValue<string>(name);

    internal int RequiredInt32(string name) => RequiredValue<int>(name);

    internal List<string> RequiredStringList(string name) => GetStringList(name, RequiredNode(name));

    internal JsonNode RequiredNode(string name)
    {
        var node = _arguments?[name];
        if (node is null)
        {
            throw ToolArgumentException.Missing(name);
        }

        return node;
    }

    internal JsonObject? OptionalObject(string name)
    {
        var node = _arguments?[name];
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.AsObject();
        }
        catch (InvalidOperationException ex)
        {
            throw ToolArgumentException.Invalid(name, typeof(JsonObject), ex);
        }
    }

    internal string? OptionalString(string name)
    {
        var node = _arguments?[name];
        return node is null ? null : GetValue<string>(name, node);
    }

    internal int? OptionalInt32(string name)
    {
        var node = _arguments?[name];
        return node is null ? null : GetValue<int>(name, node);
    }

    internal List<string>? OptionalStringList(string name)
    {
        var node = _arguments?[name];
        return node is null ? null : GetStringList(name, node);
    }

    internal bool? OptionalBoolean(string name)
    {
        var node = _arguments?[name];
        return node is null ? null : GetValue<bool>(name, node);
    }

    internal bool OptionalBoolean(string name, bool defaultValue)
    {
        var node = _arguments?[name];
        return node is null ? defaultValue : GetValue<bool>(name, node);
    }

    private T RequiredValue<T>(string name)
    {
        var node = _arguments?[name];
        if (node is null)
        {
            throw ToolArgumentException.Missing(name);
        }

        return GetValue<T>(name, node);
    }

    private static T GetValue<T>(string name, JsonNode node)
    {
        try
        {
            return node.GetValue<T>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            throw ToolArgumentException.Invalid(name, typeof(T), ex);
        }
    }

    private static List<string> GetStringList(string name, JsonNode node)
    {
        try
        {
            return node.AsArray()
                .Select(item => item?.GetValue<string>() ?? string.Empty)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            throw ToolArgumentException.Invalid(name, typeof(List<string>), ex);
        }
    }
}

internal sealed class ToolArgumentException : Exception
{
    private ToolArgumentException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    internal static ToolArgumentException Missing(string name) =>
        new($"Invalid params: {name} required");

    internal static ToolArgumentException Invalid(string name, Type expectedType, Exception innerException) =>
        new($"Invalid params: {name} must be {GetTypeName(expectedType)}", innerException);

    internal static ToolArgumentException UnknownTool(string name) =>
        new($"Invalid params: unknown tool: {name}");

    private static string GetTypeName(Type type) =>
        type == typeof(string) ? "a string" :
        type == typeof(int) ? "an integer" :
        type == typeof(bool) ? "a boolean" :
        type == typeof(JsonObject) ? "an object" :
        type == typeof(List<string>) ? "an array of strings" :
        type.Name;
}
