using System.Text.Json.Serialization;

namespace TwitchCraftBot_V1;

internal sealed class StructuredLogEvent
{
    public required string Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Event { get; init; }
    public required string ApplicationVersion { get; init; }
    public required string SessionId { get; init; }
    public string? ExceptionType { get; init; }
    public string? Message { get; init; }
    public string? Details { get; init; }
}
