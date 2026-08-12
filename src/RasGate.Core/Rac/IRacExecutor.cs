namespace RasGate.Core.Rac;

public interface IRacExecutor
{
    Task<RacStatus> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<RacExecutionResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}