namespace RasGate.Core.Rac.Exceptions;

public sealed class RacArgumentValidationException(string message)
    : ArgumentException(message);