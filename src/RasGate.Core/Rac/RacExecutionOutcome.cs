using System.Text.Json.Serialization;

namespace RasGate.Core.Rac;

/// <summary>
///     Describes what RasGate can prove about a RAC command.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RacExecutionOutcome>))]
public enum RacExecutionOutcome
{
    /// <summary>
    ///     RAC completed and returned exit code zero.
    /// </summary>
    [JsonStringEnumMemberName("succeeded")]
    Succeeded,

    /// <summary>
    ///     RAC completed and returned a non-zero exit code.
    /// </summary>
    [JsonStringEnumMemberName("failed")] Failed,

    /// <summary>
    ///     RasGate cannot confirm the result. The command may already have changed
    ///     external state.
    /// </summary>
    [JsonStringEnumMemberName("unknown")] Unknown
}