namespace RasGate.Application.Rac.Exceptions;

public sealed class RacCapacityExceededException(
    string message) : Exception(message);