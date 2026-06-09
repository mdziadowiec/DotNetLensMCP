namespace DotNetLensMcp;

internal sealed record ToolDescriptor
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required object InputSchema { get; init; }
}
