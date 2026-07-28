namespace RasGate.Application.Rac.Exceptions;

public sealed class RacOutputLimitExceededException(int maxOutputBytes)
    : Exception(
        $"RAC output exceeded the configured limit of {maxOutputBytes} bytes.");