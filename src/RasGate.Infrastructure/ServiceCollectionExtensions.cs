using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RasGate.Core.Rac;
using RasGate.Infrastructure.Rac;
using RasGate.Infrastructure.RasGate;

namespace RasGate.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRac(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RacOptions>()
            .Bind(configuration.GetSection(RacOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ExecutablePath),
                "Rac:ExecutablePath is required.")
            .Validate(
                options => options.TimeoutSeconds is > 0 and
                    <= RacOptions.MaximumTimeoutSeconds,
                $"Rac:TimeoutSeconds must be between 1 and " +
                $"{RacOptions.MaximumTimeoutSeconds}.")
            .Validate(
                options => options.StatusCacheSeconds is > 0 and
                    <= RacOptions.MaximumStatusCacheSeconds,
                $"Rac:StatusCacheSeconds must be between 1 and " +
                $"{RacOptions.MaximumStatusCacheSeconds}.")
            .Validate(
                options => options.MaxConcurrentProcesses is > 0 and
                    <= RacOptions.MaximumConcurrentProcesses,
                $"Rac:MaxConcurrentProcesses must be between 1 and " +
                $"{RacOptions.MaximumConcurrentProcesses}.")
            .Validate(
                options => options.MaxOutputBytes is > 0 and
                    <= RacOptions.MaximumOutputBytes,
                $"Rac:MaxOutputBytes must be between 1 and " +
                $"{RacOptions.MaximumOutputBytes}.")
            .Validate(
                options => options.MaxArgumentCount is > 0 and
                    <= RacOptions.MaximumArgumentCount,
                $"Rac:MaxArgumentCount must be between 1 and " +
                $"{RacOptions.MaximumArgumentCount}.")
            .Validate(
                options => options.MaxArgumentBytes is > 0 and
                    <= RacOptions.MaximumArgumentBytes,
                $"Rac:MaxArgumentBytes must be between 1 and " +
                $"{RacOptions.MaximumArgumentBytes}.")
            .Validate(
                options => options.MaxTotalArgumentBytes is > 0 and
                    <= RacOptions.MaximumTotalArgumentBytes,
                $"Rac:MaxTotalArgumentBytes must be between 1 and " +
                $"{RacOptions.MaximumTotalArgumentBytes}.")
            .Validate(
                options => options.MaxArgumentBytes <=
                           options.MaxTotalArgumentBytes,
                "Rac:MaxArgumentBytes must not exceed " +
                "Rac:MaxTotalArgumentBytes.")
            .ValidateOnStart();

        services.AddSingleton<IRacExecutor, RacExecutor>();

        return services;
    }

    public static IServiceCollection AddRasGateOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RasGateOptions>()
            .Bind(configuration.GetSection(RasGateOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.InstanceName),
                "RasGate:InstanceName is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "RasGate:ApiKey is required.")
            .Validate(
                options => options.ApiKey == options.ApiKey?.Trim(),
                "RasGate:ApiKey must not contain leading or trailing whitespace.")
            .Validate(
                options => options.ApiKey is
                {
                    Length: >= RasGateOptions.MinimumApiKeyLength and
                    <= RasGateOptions.MaximumApiKeyLength
                },
                $"RasGate:ApiKey must contain between " +
                $"{RasGateOptions.MinimumApiKeyLength} and " +
                $"{RasGateOptions.MaximumApiKeyLength} characters.")
            .Validate(
                options => !IsKnownApiKeyPlaceholder(options.ApiKey),
                "RasGate:ApiKey must not use a documented placeholder value.")
            .ValidateOnStart();

        return services;
    }

    private static bool IsKnownApiKeyPlaceholder(string? apiKey)
    {
        var normalizedApiKey = apiKey?.Trim();

        return string.Equals(
                   normalizedApiKey,
                   "replace-with-a-secret-api-key",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   normalizedApiKey,
                   "replace-with-your-secret-key",
                   StringComparison.OrdinalIgnoreCase);
    }
}