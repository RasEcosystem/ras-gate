namespace RasGate.Core.Rac.Exceptions;

public sealed class RacExecutionOutcomeUnknownException : Exception
{
    public RacExecutionOutcomeUnknownException(
        string message,
        Exception innerException,
        bool processTerminationConfirmed = true)
        : base(message, innerException)
    {
        ProcessTerminationConfirmed = processTerminationConfirmed;
    }

    public bool ProcessTerminationConfirmed { get; }
}