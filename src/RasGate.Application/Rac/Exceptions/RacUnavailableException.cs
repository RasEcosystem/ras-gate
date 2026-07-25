namespace RasGate.Application.Rac.Exceptions;

public sealed class RacUnavailableException : Exception
{
    public RacUnavailableException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}