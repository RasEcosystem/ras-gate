using System.Text;
using RasGate.Core.Rac.Exceptions;

namespace RasGate.Infrastructure.Rac;

internal static class RacArgumentValidator
{
    public static void Validate(
        IReadOnlyList<string> arguments,
        RacOptions options)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(options);

        if (arguments.Count == 0)
            throw new RacArgumentValidationException(
                "At least one RAC argument is required.");

        if (arguments.Count > options.MaxArgumentCount)
            throw new RacArgumentValidationException(
                $"RAC accepts at most {options.MaxArgumentCount} " +
                "arguments per request.");

        long totalBytes = Math.Max(0, arguments.Count - 1);

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (argument is null)
                throw new RacArgumentValidationException(
                    $"RAC argument at index {index} must not be null.");

            if (argument.AsSpan().Contains('\0'))
                throw new RacArgumentValidationException(
                    $"RAC argument at index {index} must not contain " +
                    "a null character.");

            var argumentBytes = Encoding.UTF8.GetByteCount(argument);

            if (argumentBytes > options.MaxArgumentBytes)
                throw new RacArgumentValidationException(
                    $"RAC argument at index {index} exceeds the " +
                    $"configured limit of {options.MaxArgumentBytes} bytes.");

            totalBytes += argumentBytes;

            if (totalBytes > options.MaxTotalArgumentBytes)
                throw new RacArgumentValidationException(
                    "The encoded RAC argument list exceeds the configured " +
                    $"limit of {options.MaxTotalArgumentBytes} bytes.");
        }
    }
}