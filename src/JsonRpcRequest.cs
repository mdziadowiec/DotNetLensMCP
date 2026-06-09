using System.Text.Json.Nodes;

namespace DotNetLensMcp;

internal sealed record JsonRpcRequest
{
    public RequestId? Id { get; init; }

    public string? Method { get; init; }

    public JsonNode? Params { get; init; }
}
