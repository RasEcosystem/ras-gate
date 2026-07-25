namespace RasGate.Contracts.Rac;

public sealed record ExecuteRacRequest
{
    public required IReadOnlyList<string> Arguments { get; init; }
}