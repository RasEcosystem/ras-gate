namespace RasGate.Contracts.RasGate;

public sealed record RasGateStatusResponse
{
    public string InstanceName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;
}