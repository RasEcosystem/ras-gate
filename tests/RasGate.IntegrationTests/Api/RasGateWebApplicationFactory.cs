using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RasGate.Web;

namespace RasGate.IntegrationTests.Api;

public sealed class RasGateWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly string _executablePath;
    private readonly int _maxConcurrentProcesses;
    private readonly int _timeoutSeconds;

    public RasGateWebApplicationFactory(
        string executablePath,
        int timeoutSeconds = 5,
        int maxConcurrentProcesses = 2)
    {
        _executablePath = executablePath;
        _timeoutSeconds = timeoutSeconds;
        _maxConcurrentProcesses = maxConcurrentProcesses;
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Rac:ExecutablePath"] = _executablePath,
                    ["Rac:TimeoutSeconds"] =
                        _timeoutSeconds.ToString(),
                    ["Rac:MaxConcurrentProcesses"] =
                        _maxConcurrentProcesses.ToString()
                });
        });
    }
}