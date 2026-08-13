using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RasGate.Web.Controllers;

namespace RasGate.IntegrationTests.Api;

public sealed class RacOperationTelemetryTests
{
    [Fact]
    public async Task Execute_LogsSafeStructuredOperationMetadata()
    {
        const string apiKey = "rasgate-operation-telemetry-test-key";
        const string secret = "super-secret-cluster-password";
        const string firstArgumentSecret = "first-position-secret";
        const string endpointShapedSecret = "endpoint-secret:1234";
        var logger = new CollectingLogger<RacController>();

        await using var factory = new RasGateWebApplicationFactory(
            GetFakeRacPath(),
            apiKey: apiKey,
            configureServices: services =>
                services.AddSingleton<ILogger<RacController>>(logger));

        using var client = factory.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "__test",
                    "stdout",
                    $"--password={secret}",
                    "localhost:1545"
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var credentialOptionResponse = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "--cluster-pwd",
                    secret
                }
            });

        using var firstArgumentResponse = await client.PostAsJsonAsync(
            "/rac/execute",
            new { arguments = new[] { firstArgumentSecret } });

        using var endpointSecretResponse = await client.PostAsJsonAsync(
            "/rac/execute",
            new
            {
                arguments = new[]
                {
                    "--cluster-pwd",
                    endpointShapedSecret
                }
            });

        Assert.Equal(HttpStatusCode.OK, credentialOptionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstArgumentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, endpointSecretResponse.StatusCode);
        Assert.Contains(
            logger.Events,
            entry => entry.Message.Contains(
                "Starting RAC command",
                StringComparison.Ordinal));
        Assert.Contains(
            logger.Events,
            entry => entry.Message.Contains(
                "RAC command completed",
                StringComparison.Ordinal));

        var loggedText = string.Join(
            '\n',
            logger.Events.Select(entry => entry.Message)
                .Concat(logger.Scopes.SelectMany(scope =>
                    scope.Select(property =>
                        $"{property.Key}={property.Value}"))));

        Assert.DoesNotContain(secret, loggedText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            firstArgumentSecret,
            loggedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            endpointShapedSecret,
            loggedText,
            StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, loggedText, StringComparison.Ordinal);

        var scope = Assert.Single(
            logger.Scopes,
            candidate => Equals(
                candidate["RacCommand"],
                "__test"));

        Assert.Equal("RAC", scope["Phase"]);
        Assert.Equal("__test", scope["RacCommand"]);
        Assert.Equal("stdout", scope["RacSubcommand"]);
        Assert.Equal("dns", scope["RacTarget"]);
        Assert.Equal(4, scope["RacArgumentCount"]);
        Assert.Equal("api-key", scope["RacClientId"]);

        Assert.All(
            logger.Scopes.Where(candidate =>
                !ReferenceEquals(candidate, scope)),
            candidate =>
                Assert.Equal("<redacted>", candidate["RacCommand"]));
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
            Events.Enqueue(new LogEntry(
                logLevel,
                formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}