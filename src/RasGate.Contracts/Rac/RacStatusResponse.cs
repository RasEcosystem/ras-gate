namespace RasGate.Contracts.Rac;

public sealed record RacStatusResponse
{
    public required bool Available { get; init; }

    public string? Version { get; init; }

    public string Message { get; init; } = string.Empty;
}