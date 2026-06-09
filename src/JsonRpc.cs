using System.Text.Json.Serialization;

namespace DotNetLensMcp;

internal sealed record JsonRpcSuccessResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    RequestId? Id,
    object Result);

internal sealed record JsonRpcErrorResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    RequestId? Id,
    JsonRpcError Error);

internal sealed record JsonRpcError(int Code, string Message);

internal static class JsonRpc
{
    internal const string Version = "2.0";

    internal static class ErrorCodes
    {
        internal const int ParseError = -32700;
        internal const int InvalidRequest = -32600;
        internal const int MethodNotFound = -32601;
        internal const int InvalidParams = -32602;
        internal const int InternalError = -32603;
    }

    internal static JsonRpcSuccessResponse CreateSuccessResponse(RequestId? id, object result) =>
        new(Version, id, result);

    internal static JsonRpcErrorResponse CreateErrorResponse(RequestId? id, int code, string message) =>
        new(Version, id, new JsonRpcError(code, message));
}
