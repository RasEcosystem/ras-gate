namespace RasGate.Infrastructure.Rac;

public sealed class RacOptions
{
    public const string SectionName = "Rac";

    public string ExecutablePath { get; init; } = "rac";

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxConcurrentProcesses { get; init; } = 4;
}