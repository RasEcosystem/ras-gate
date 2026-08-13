using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RasGate.Web;

namespace RasGate.IntegrationTests.Api;

public sealed class ApplicationLifecycleTelemetryTests
{
    [Fact]
    public async Task InvalidConfiguration_LogsUnexpectedTermination()
    {
        var logger = new CollectingLogger<Program>();
        var factory = new RasGateWebApplicationFactory(
            GetFakeRacPath(),
            apiKey: "",
            configureServices: services =>
                services.AddSingleton<ILogger<Program>>(logger));

        try
        {
            Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        }
        finally
        {
            await factory.DisposeAsync();
        }

        Assert.Contains(logger.Events, entry => entry.EventId.Id == 1000);

        var terminationEvent = Assert.Single(
            logger.Events,
            entry => entry.EventId.Id == 1005);

        Assert.Equal(LogLevel.Critical, terminationEvent.Level);
        Assert.DoesNotContain(
            logger.Events,
            entry => entry.EventId.Id is 1001 or 1002);
    }

    [Fact]
    public async Task HostLifecycle_EmitsSafeStructuredEvents()
    {
        const string apiKey = "rasgate-lifecycle-telemetry-secret";
        const string instanceName = "RasGate Lifecycle Test";
        var logger = new CollectingLogger<Program>();
        var factory = new RasGateWebApplicationFactory(
            GetFakeRacPath(),
            instanceName: instanceName,
            apiKey: apiKey,
            configureServices: services =>
                services.AddSingleton<ILogger<Program>>(logger));

        try
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/rasgate/status");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Contains(logger.Events, entry => entry.EventId.Id == 1000);
            Assert.Contains(logger.Events, entry => entry.EventId.Id == 1001);
            Assert.Contains(logger.Events, entry => entry.EventId.Id == 1002);
            Assert.DoesNotContain(
                logger.Events,
                entry => entry.EventId.Id == 1005);
        }
        finally
        {
            await factory.DisposeAsync();
        }

        Assert.Contains(logger.Events, entry => entry.EventId.Id == 1003);
        Assert.Contains(logger.Events, entry => entry.EventId.Id == 1004);

        var startingEvent = Assert.Single(
            logger.Events,
            entry => entry.EventId.Id == 1000);

        Assert.Equal(LogLevel.Information, startingEvent.Level);
        Assert.Equal("Testing", startingEvent.Properties["EnvironmentName"]);
        Assert.True((int)startingEvent.Properties["ProcessId"]! > 0);

        var configurationEvent = Assert.Single(
            logger.Events,
            entry => entry.EventId.Id == 1001);

        Assert.Equal(instanceName, configurationEvent.Properties["InstanceName"]);
        Assert.Equal(
            GetFakeRacPath(),
            configurationEvent.Properties["RacExecutablePath"]);
        Assert.Equal(2, configurationEvent.Properties["RacMaxConcurrentProcesses"]);
        Assert.Equal(4194304, configurationEvent.Properties["RacMaxOutputBytes"]);

        Assert.All(
            logger.Scopes,
            scope => Assert.Equal("Application", scope["Phase"]));

        var loggedText = string.Join(
            '\n',
            logger.Events.Select(entry =>
                    entry.Message + " " +
                    string.Join(
                        ' ',
                        entry.Properties.Select(property =>
                            $"{property.Key}={property.Value}")))
                .Concat(logger.Scopes.Select(scope =>
                    string.Join(
                        ' ',
                        scope.Select(property =>
                            $"{property.Key}={property.Value}")))));

        Assert.DoesNotContain(apiKey, loggedText, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", loggedText, StringComparison.Ordinal);
    }

    private static string GetFakeRacPath()
    {
        var fileName = OperatingSystem.IsWindows()
            ? "RasGate.FakeRac.exe"
            : "RasGate.FakeRac";

        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name;

        if (string.IsNullOrWhiteSpace(configuration))
            throw new InvalidOperationException(
                "Could not determine build configuration.");

        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "RasGate.FakeRac",
                "bin",
                configuration,
                "net10.0",
                fileName));
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Events { get; } = new();

        public ConcurrentQueue<IReadOnlyDictionary<string, object?>> Scopes { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IReadOnlyDictionary<string, object?> properties)
                Scopes.Enqueue(properties);

            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is
                IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value)
                : new Dictionary<string, object?>();

            Events.Enqueue(
                new LogEntry(
                    logLevel,
                    eventId,
                    formatter(state, exception),
                    properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}