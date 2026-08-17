using Microsoft.Extensions.Options;
using RasGate.Infrastructure.Rac;
using RasGate.Infrastructure.RasGate;

namespace RasGate.Web;

internal static class ConfigurationValidator
{
    internal const string Switch = "--validate-config";

    public static bool IsRequested(IEnumerable<string> args)
    {
        return args.Any(arg =>
            string.Equals(
                arg,
                Switch,
                StringComparison.OrdinalIgnoreCase));
    }

    public static string[] RemoveSwitch(IEnumerable<string> args)
    {
        return args
            .Where(arg =>
                !string.Equals(
                    arg,
                    Switch,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static int Validate(
        IServiceProvider services,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            _ = services
                .GetRequiredService<IOptions<RasGateOptions>>()
                .Value;

            _ = services
                .GetRequiredService<IOptions<RacOptions>>()
                .Value;

            output.WriteLine("RasGate configuration is valid.");

            return 0;
        }
        catch (Exception exception)
        {
            return ReportStartupFailure(exception, error);
        }
    }

    public static int ReportStartupFailure(
        Exception exception,
        TextWriter error)
    {
        error.WriteLine("RasGate configuration is invalid.");

        if (exception is OptionsValidationException validationException)
        {
            foreach (var failure in validationException
                         .Failures
                         .Distinct(StringComparer.Ordinal))
                error.WriteLine($"- {failure}");
        }
        else
        {
            error.WriteLine(
                $"Configuration could not be loaded " +
                $"({exception.GetType().Name}).");
        }

        return 1;
    }
}
