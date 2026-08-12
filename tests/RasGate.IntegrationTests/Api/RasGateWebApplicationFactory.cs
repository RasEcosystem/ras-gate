using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RasGate.Web;
using RasGate.Web.Authentication;

namespace RasGate.IntegrationTests.Api;

public sealed class RasGateWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly string _apiKey;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly string _environment;
    private readonly string _executablePath;
    private readonly string _instanceName;
    private readonly int _maxArgumentBytes;
    private readonly int _maxArgumentCount;
    private readonly int _maxConcurrentProcesses;
    private readonly int _maxOutputBytes;
    private readonly int _maxTotalArgumentBytes;
    private readonly int _statusCacheSeconds;
    private readonly int _timeoutSeconds;

    public RasGateWebApplicationFactory(
        string executablePath,
        int timeoutSeconds = 5,
        int maxConcurrentProcesses = 2,
        int maxOutputBytes = 4194304,
        string instanceName = "RasGate Test Application",
        string apiKey = "rasgate-integration-test-api-key",
        Action<IServiceCollection>? configureServices = null,
        int maxArgumentCount = 128,
        int maxArgumentBytes = 8192,
        int maxTotalArgumentBytes = 24576,
        int statusCacheSeconds = 30,
        string environment = "Testing")
    {
        _apiKey = apiKey;
        _executablePath = executablePath;
        _timeoutSeconds = timeoutSeconds;
        _maxConcurrentProcesses = maxConcurrentProcesses;
        _maxOutputBytes = maxOutputBytes;
        _instanceName = instanceName;
        _configureServices = configureServices;
        _maxArgumentCount = maxArgumentCount;
        _maxArgumentBytes = maxArgumentBytes;
        _maxTotalArgumentBytes = maxTotalArgumentBytes;
        _statusCacheSeconds = statusCacheSeconds;
        _environment = environment;
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();

        client.DefaultRequestHeaders.Add(
            ApiKeyAuthenticationDefaults.HeaderName,
            _apiKey);

        return client;
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Rac:ExecutablePath"] = _executablePath,
                    ["Rac:TimeoutSeconds"] = _timeoutSeconds.ToString(),
                    ["Rac:StatusCacheSeconds"] = _statusCacheSeconds.ToString(),
                    ["Rac:MaxConcurrentProcesses"] = _maxConcurrentProcesses.ToString(),
                    ["Rac:MaxOutputBytes"] = _maxOutputBytes.ToString(),
                    ["Rac:MaxArgumentCount"] = _maxArgumentCount.ToString(),
                    ["Rac:MaxArgumentBytes"] = _maxArgumentBytes.ToString(),
                    ["Rac:MaxTotalArgumentBytes"] = _maxTotalArgumentBytes.ToString(),
                    ["RasGate:InstanceName"] = _instanceName,
                    ["RasGate:ApiKey"] = _apiKey
                });
        });

        if (_configureServices is not null)
            builder.ConfigureServices(_configureServices);
    }
}