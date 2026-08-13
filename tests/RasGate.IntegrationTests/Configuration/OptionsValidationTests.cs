using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RasGate.Infrastructure;
using RasGate.Infrastructure.Rac;
using RasGate.Infrastructure.RasGate;

namespace RasGate.IntegrationTests.Configuration;

public sealed class OptionsValidationTests
{
    [Fact]
    public void ShippedAppSettings_DoesNotContainApiKeyCredential()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "appsettings.json");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var rasGate = document.RootElement.GetProperty("RasGate");

        Assert.False(rasGate.TryGetProperty("ApiKey", out _));
    }

    [Fact]
    public async Task RasGateOptions_MissingApiKey_FailsHostStartup()
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["RasGate:InstanceName"] = "RasGate Test"
            });

        using var host = new HostBuilder()
            .ConfigureServices(services =>
                services.AddRasGateOptions(configuration))
            .Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "RasGate:ApiKey",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("                               ")]
    [InlineData("replace-with-a-secret-api-key")]
    [InlineData("replace-with-your-secret-key")]
    [InlineData("    replace-with-a-secret-api-key    ")]
    [InlineData("    replace-with-your-secret-key    ")]
    [InlineData(" 0123456789abcdef0123456789abcdef ")]
    public void RasGateOptions_UnsafeApiKey_IsRejected(string? apiKey)
    {
        var services = new ServiceCollection();
        services.AddRasGateOptions(CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["RasGate:InstanceName"] = "RasGate Test",
                ["RasGate:ApiKey"] = apiKey
            }));

        AssertInvalid<RasGateOptions>(services, "RasGate:ApiKey");
    }

    [Theory]
    [InlineData(31)]
    [InlineData(513)]
    public void RasGateOptions_ApiKeyOutsideLengthBudget_IsRejected(
        int length)
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["RasGate:InstanceName"] = "RasGate Test",
                ["RasGate:ApiKey"] = new('k', length)
            });

        var services = new ServiceCollection();
        services.AddRasGateOptions(configuration);

        AssertInvalid<RasGateOptions>(services, "RasGate:ApiKey");
    }

    [Theory]
    [InlineData(32)]
    [InlineData(512)]
    public void RasGateOptions_ApiKeyAtLengthBoundary_IsAccepted(
        int length)
    {
        var configuration = CreateConfiguration(
            new Dictionary<string, string?>
            {
                ["RasGate:InstanceName"] = "RasGate Test",
                ["RasGate:ApiKey"] = new('k', length)
            });

        var services = new ServiceCollection();
        services.AddRasGateOptions(configuration);

        AssertValid<RasGateOptions>(services);
    }

    [Theory]
    [InlineData("Rac:TimeoutSeconds", "0")]
    [InlineData("Rac:TimeoutSeconds", "3601")]
    [InlineData("Rac:StatusCacheSeconds", "0")]
    [InlineData("Rac:StatusCacheSeconds", "301")]
    [InlineData("Rac:MaxConcurrentProcesses", "0")]
    [InlineData("Rac:MaxConcurrentProcesses", "33")]
    [InlineData("Rac:MaxOutputBytes", "0")]
    [InlineData("Rac:MaxOutputBytes", "16777217")]
    [InlineData("Rac:MaxArgumentCount", "0")]
    [InlineData("Rac:MaxArgumentCount", "129")]
    [InlineData("Rac:MaxArgumentBytes", "0")]
    [InlineData("Rac:MaxArgumentBytes", "8193")]
    [InlineData("Rac:MaxTotalArgumentBytes", "0")]
    [InlineData("Rac:MaxTotalArgumentBytes", "24577")]
    public void RacOptions_ValueOutsideResourceBudget_IsRejected(
        string key,
        string value)
    {
        var values = ValidRacConfiguration();
        values[key] = value;

        var services = new ServiceCollection();
        services.AddRac(CreateConfiguration(values));

        AssertInvalid<RacOptions>(services, key[4..]);
    }

    [Fact]
    public void RacOptions_PerArgumentBudgetAboveTotalBudget_IsRejected()
    {
        var values = ValidRacConfiguration();
        values["Rac:MaxArgumentBytes"] = "8192";
        values["Rac:MaxTotalArgumentBytes"] = "4096";

        var services = new ServiceCollection();
        services.AddRac(CreateConfiguration(values));

        AssertInvalid<RacOptions>(services, "MaxArgumentBytes");
    }

    [Fact]
    public void RacOptions_LowerResourceBoundaries_AreAccepted()
    {
        var values = ValidRacConfiguration();
        values["Rac:TimeoutSeconds"] = "1";
        values["Rac:StatusCacheSeconds"] = "1";
        values["Rac:MaxConcurrentProcesses"] = "1";
        values["Rac:MaxOutputBytes"] = "1";
        values["Rac:MaxArgumentCount"] = "1";
        values["Rac:MaxArgumentBytes"] = "1";
        values["Rac:MaxTotalArgumentBytes"] = "1";

        var services = new ServiceCollection();
        services.AddRac(CreateConfiguration(values));

        AssertValid<RacOptions>(services);
    }

    [Fact]
    public void RacOptions_UpperResourceBoundaries_AreAccepted()
    {
        var services = new ServiceCollection();
        services.AddRac(CreateConfiguration(ValidRacConfiguration()));

        AssertValid<RacOptions>(services);
    }

    private static Dictionary<string, string?> ValidRacConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["Rac:ExecutablePath"] = "rac",
            ["Rac:TimeoutSeconds"] = "3600",
            ["Rac:StatusCacheSeconds"] = "300",
            ["Rac:MaxConcurrentProcesses"] = "32",
            ["Rac:MaxOutputBytes"] = "16777216",
            ["Rac:MaxArgumentCount"] = "128",
            ["Rac:MaxArgumentBytes"] = "8192",
            ["Rac:MaxTotalArgumentBytes"] = "24576"
        };
    }

    private static IConfiguration CreateConfiguration(
        Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static void AssertInvalid<TOptions>(
        IServiceCollection services,
        string expectedMessageFragment)
        where TOptions : class
    {
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider
            .GetRequiredService<IOptions<TOptions>>()
            .Value);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                expectedMessageFragment,
                StringComparison.Ordinal));
    }

    private static void AssertValid<TOptions>(
        IServiceCollection services)
        where TOptions : class
    {
        using var provider = services.BuildServiceProvider();

        _ = provider
            .GetRequiredService<IOptions<TOptions>>()
            .Value;
    }
}