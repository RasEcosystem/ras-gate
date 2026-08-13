namespace RasGate.Core.Rac;

public sealed record RacStatus
{
    public required bool Available { get; init; }

    public string? Version { get; init; }

    public required string ExecutablePath { get; init; }

    public string Message { get; init; } = string.Empty;
}