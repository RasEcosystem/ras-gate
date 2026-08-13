namespace RasGate.Core.Rac;

public sealed record ExecuteRacRequest
{
    public required IReadOnlyList<string> Arguments { get; init; }
}