namespace RasGate.Infrastructure.RasGate;

public sealed class RasGateOptions
{
    public const string SectionName = "RasGate";

    public string InstanceName { get; init; } = "";

    public string ApiKey { get; init; } = "";
}