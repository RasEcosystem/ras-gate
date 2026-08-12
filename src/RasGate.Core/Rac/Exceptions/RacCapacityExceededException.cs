namespace RasGate.Core.Rac.Exceptions;

public sealed class RacCapacityExceededException(
    string message) : Exception(message);