namespace RasGate.Core.Common;

public sealed record ApiError(
    string Code,
    string Message,
    string? Target = null
);