namespace RasGate.Infrastructure.RasGate;

public sealed class RasGateOptions
{
    public const string SectionName = "RasGate";

    public const int MinimumApiKeyLength = 32;

    public const int MaximumApiKeyLength = 512;

    public string InstanceName { get; init; } = "";

    public string ApiKey { get; init; } = "";
}