namespace RasGate.Infrastructure.Rac;

public sealed class RacOptions
{
    public const string SectionName = "Rac";

    public const int MaximumTimeoutSeconds = 3600;

    public const int MaximumStatusCacheSeconds = 300;

    public const int MaximumConcurrentProcesses = 32;

    public const int MaximumOutputBytes = 16 * 1024 * 1024;

    public const int MaximumArgumentCount = 128;

    public const int MaximumArgumentBytes = 8 * 1024;

    public const int MaximumTotalArgumentBytes = 24 * 1024;

    public string ExecutablePath { get; init; } = "rac";

    public int TimeoutSeconds { get; init; } = 30;

    public int StatusCacheSeconds { get; init; } = 30;

    public int MaxConcurrentProcesses { get; init; } = 4;

    public int MaxOutputBytes { get; init; } = 4 * 1024 * 1024;

    public int MaxArgumentCount { get; init; } = MaximumArgumentCount;

    public int MaxArgumentBytes { get; init; } = MaximumArgumentBytes;

    public int MaxTotalArgumentBytes { get; init; } = MaximumTotalArgumentBytes;
}