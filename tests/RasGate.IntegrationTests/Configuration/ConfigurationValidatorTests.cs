using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RasGate.Infrastructure;
using RasGate.Web;

namespace RasGate.IntegrationTests.Configuration;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void CreateWebApplicationBuilder_UsesApplicationDirectory()
    {
        var builder = Program.CreateWebApplicationBuilder([]);

        Assert.Equal(
            AppContext.BaseDirectory,
            builder.Environment.ContentRootPath);
    }

    [Fact]
    public void IsRequested_MatchesSwitchIgnoringCase()
    {
        Assert.True(
            ConfigurationValidator.IsRequested(
                ["--VALIDATE-CONFIG"]));
    }

    [Fact]
    public void RemoveSwitch_PreservesOtherArguments()
    {
        var result = ConfigurationValidator.RemoveSwitch(
            [
                "--urls",
                "http://127.0.0.1:5051",
                "--validate-config"
            ]);

        Assert.Equal(
            [
                "--urls",
                "http://127.0.0.1:5051"
            ],
            result);
    }

    [Fact]
    public void Validate_WithValidConfiguration_ReturnsSuccess()
    {
        using var services = CreateServices(
            "0123456789abcdef0123456789abcdef");

        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ConfigurationValidator.Validate(
            services,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "configuration is valid",
            output.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Validate_WithInvalidConfiguration_DoesNotExposeSecret()
    {
        const string invalidSecret = "secret-value";

        using var services = CreateServices(invalidSecret);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = ConfigurationValidator.Validate(
            services,
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains(
            "configuration is invalid",
            error.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            invalidSecret,
            error.ToString(),
            StringComparison.Ordinal);
    }

    private static ServiceProvider CreateServices(string apiKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RasGate:InstanceName"] = "RasGate Test",
                    ["RasGate:ApiKey"] = apiKey,
                    ["Rac:ExecutablePath"] = "rac",
                    ["Rac:TimeoutSeconds"] = "30",
                    ["Rac:StatusCacheSeconds"] = "30",
                    ["Rac:MaxConcurrentProcesses"] = "4",
                    ["Rac:MaxOutputBytes"] = "4194304",
                    ["Rac:MaxArgumentCount"] = "128",
                    ["Rac:MaxArgumentBytes"] = "8192",
                    ["Rac:MaxTotalArgumentBytes"] = "24576"
                })
            .Build();

        var services = new ServiceCollection();

        services.AddRasGateOptions(configuration);
        services.AddRac(configuration);

        return services.BuildServiceProvider();
    }
}
