using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RasGate.Application.Rac.Interfaces;
using RasGate.Infrastructure.Rac;

namespace RasGate.Infrastructure;

public static class RacServiceCollectionExtensions
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
                options => options.TimeoutSeconds > 0,
                "Rac:TimeoutSeconds must be greater than zero.")
            .Validate(
                options => options.MaxConcurrentProcesses > 0,
                "Rac:MaxConcurrentProcesses must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton<IRacExecutor, RacExecutor>();

        return services;
    }
}