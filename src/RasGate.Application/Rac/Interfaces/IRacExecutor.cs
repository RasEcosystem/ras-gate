namespace RasGate.Application.Rac.Interfaces;

public interface IRacExecutor
{
    Task<RacStatus> GetStatusAsync(
        CancellationToken cancellationToken);

    Task<RacExecutionResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}