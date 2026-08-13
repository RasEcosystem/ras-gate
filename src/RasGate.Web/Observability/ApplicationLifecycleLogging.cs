using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using RasGate.Infrastructure.Rac;
using RasGate.Infrastructure.RasGate;

namespace RasGate.Web.Observability;

internal static class ApplicationLifecycleLogging
{
    private const string ApplicationPhase = "Application";

    private static readonly EventId StartingEvent = new(
        1000,
        "ApplicationStarting");

    private static readonly EventId ConfigurationEvent = new(
        1001,
        "ApplicationConfigurationLoaded");

    private static readonly EventId StartedEvent = new(
        1002,
        "ApplicationStarted");

    private static readonly EventId StoppingEvent = new(
        1003,
        "ApplicationStopping");

    private static readonly EventId StoppedEvent = new(
        1004,
        "ApplicationStopped");

    private static readonly EventId UnexpectedTerminationEvent = new(
        1005,
        "ApplicationTerminatedUnexpectedly");

    public static void ConfigureApplicationLifecycleLogging(
        this WebApplication app,
        ILogger<Program> logger)
    {
        using (BeginApplicationScope(logger))
        {
            logger.Log(
                LogLevel.Information,
                StartingEvent,
                "Starting RasGate {ApplicationVersion} in " +
                "{EnvironmentName}. Process {ProcessId}; runtime " +
                "{RuntimeDescription}; OS {OperatingSystem}; " +
                "architecture {ProcessArchitecture}.",
                GetApplicationVersion(),
                app.Environment.EnvironmentName,
                Environment.ProcessId,
                RuntimeInformation.FrameworkDescription,
                RuntimeInformation.OSDescription.Trim(),
                RuntimeInformation.ProcessArchitecture);
        }

        var rasGateOptions = app.Services
            .GetRequiredService<IOptions<RasGateOptions>>()
            .Value;

        var racOptions = app.Services
            .GetRequiredService<IOptions<RacOptions>>()
            .Value;

        using (BeginApplicationScope(logger))
        {
            logger.Log(
                LogLevel.Information,
                ConfigurationEvent,
                "Configuration loaded for instance {InstanceName}: RAC " +
                "executable {RacExecutablePath}; " +
                "timeout {RacTimeoutSeconds} s; status cache " +
                "{RacStatusCacheSeconds} s; maximum concurrent processes " +
                "{RacMaxConcurrentProcesses}; output limit " +
                "{RacMaxOutputBytes} bytes per stream; argument limits " +
                "{RacMaxArgumentCount} items, {RacMaxArgumentBytes} bytes " +
                "per item and {RacMaxTotalArgumentBytes} bytes total. " +
                "API-key authentication is enabled.",
                rasGateOptions.InstanceName,
                racOptions.ExecutablePath,
                racOptions.TimeoutSeconds,
                racOptions.StatusCacheSeconds,
                racOptions.MaxConcurrentProcesses,
                racOptions.MaxOutputBytes,
                racOptions.MaxArgumentCount,
                racOptions.MaxArgumentBytes,
                racOptions.MaxTotalArgumentBytes);
        }

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            using var scope = BeginApplicationScope(logger);

            var addresses = app.Urls.Count == 0
                ? "<managed by server>"
                : string.Join(
                    ", ",
                    app.Urls.Order(StringComparer.Ordinal));

            logger.Log(
                LogLevel.Information,
                StartedEvent,
                "RasGate started and is listening on {ListeningAddresses}.",
                addresses);
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            using var scope = BeginApplicationScope(logger);

            logger.Log(
                LogLevel.Information,
                StoppingEvent,
                "RasGate shutdown requested.");
        });

        app.Lifetime.ApplicationStopped.Register(() =>
        {
            using var scope = BeginApplicationScope(logger);

            logger.Log(
                LogLevel.Information,
                StoppedEvent,
                "RasGate stopped.");
        });
    }

    public static void LogUnexpectedTermination(
        this ILogger<Program> logger,
        Exception exception)
    {
        using var scope = BeginApplicationScope(logger);

        logger.Log(
            LogLevel.Critical,
            UnexpectedTerminationEvent,
            exception,
            "RasGate terminated unexpectedly during startup or execution.");
    }

    private static IDisposable? BeginApplicationScope(ILogger logger)
    {
        return logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["Phase"] = ApplicationPhase
            });
    }

    private static string GetApplicationVersion()
    {
        var assembly = typeof(Program).Assembly;

        return assembly
                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                   ?.InformationalVersion ??
               assembly.GetName().Version?.ToString() ??
               "unknown";
    }
}