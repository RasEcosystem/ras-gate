using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RasGate.Application.Rac.Exceptions;
using RasGate.Infrastructure.Rac;

namespace RasGate.IntegrationTests.Rac;

public sealed class RacExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Version_ReturnsVersion()
    {
        using var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(
            ["--version"],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(
            "Remote Administrative Client",
            result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    private static RacExecutor CreateExecutor(
        int timeoutSeconds = 5,
        int maxConcurrentProcesses = 2,
        string? executablePath = null)
    {
        var options = Options.Create(
            new RacOptions
            {
                ExecutablePath =
                    executablePath ?? GetFakeRacPath(),
                TimeoutSeconds = timeoutSeconds,
                MaxConcurrentProcesses =
                    maxConcurrentProcesses
            });

        return new RacExecutor(
            options,
            NullLogger<RacExecutor>.Instance);
    }

    private static string GetFakeRacPath()
    {
        var fileName = OperatingSystem.IsWindows()
            ? "RasGate.FakeRac.exe"
            : "RasGate.FakeRac";

        var configuration = new DirectoryInfo(
                AppContext.BaseDirectory)
            .Parent?
            .Name;

        if (string.IsNullOrWhiteSpace(configuration))
            throw new InvalidOperationException(
                $"Could not determine build configuration from " +
                $"'{AppContext.BaseDirectory}'.");

        var path = Path.GetFullPath(
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

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Fake RAC executable was not found: {path}",
                path);

        return path;
    }

    [Fact]
    public async Task ExecuteAsync_Stdout_ReturnsStandardOutput()
    {
        using var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(
            ["__test", "stdout", "hello", "RasGate"],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            "hello RasGate",
            result.StandardOutput.Trim());
        Assert.Empty(result.StandardError);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_Stderr_ReturnsStandardError()
    {
        using var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(
            ["__test", "stderr", "simulated", "failure"],
            CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.Equal(
            "simulated failure",
            result.StandardError.Trim());
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_Exit_ReturnsSpecifiedExitCode()
    {
        using var executor = CreateExecutor();

        var result = await executor.ExecuteAsync(
            ["__test", "exit", "17"],
            CancellationToken.None);

        Assert.Equal(17, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_KillsProcessAndReturnsTimedOut()
    {
        using var executor = CreateExecutor(
            1);

        var result = await executor.ExecuteAsync(
            ["__test", "delay", "10000"],
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.True(result.DurationMilliseconds >= 900);
        Assert.True(result.DurationMilliseconds < 5000);
    }

    [Fact]
    public async Task ExecuteAsync_MissingExecutable_ThrowsRacUnavailableException()
    {
        using var executor = CreateExecutor(
            executablePath: Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString(),
                "missing-rac"));

        var exception =
            await Assert.ThrowsAsync<RacUnavailableException>(() => executor.ExecuteAsync(
                ["--version"],
                CancellationToken.None));

        Assert.Equal(
            "RAC executable could not be started.",
            exception.Message);

        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task GetStatusAsync_AvailableExecutable_ReturnsAvailable()
    {
        using var executor = CreateExecutor();

        var status = await executor.GetStatusAsync(
            CancellationToken.None);

        Assert.True(status.Available);
        Assert.Contains(
            "Remote Administrative Client",
            status.Version);

        Assert.Equal("", status.Message);
    }

    [Fact]
    public async Task GetStatusAsync_MissingExecutable_ReturnsUnavailable()
    {
        using var executor = CreateExecutor(
            executablePath: Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString(),
                "missing-rac"));

        var status = await executor.GetStatusAsync(
            CancellationToken.None);

        Assert.False(status.Available);
        Assert.Null(status.Version);

        Assert.Equal(
            "RAC executable could not be started.",
            status.Message);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyArguments_ThrowsArgumentException()
    {
        using var executor = CreateExecutor();

        await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync(
            [],
            CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_AllSlotsOccupied_ThrowsCapacityException()
    {
        using var executor = CreateExecutor(
            5,
            1);

        var runningTask = executor.ExecuteAsync(
            ["__test", "delay", "1500"],
            CancellationToken.None);

        await Task.Delay(200);

        await Assert.ThrowsAsync<RacCapacityExceededException>(() => executor.ExecuteAsync(
            ["--version"],
            CancellationToken.None));

        var result = await runningTask;

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var executor = CreateExecutor(
            10);

        using var cancellationTokenSource =
            new CancellationTokenSource(
                TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            ["__test", "delay", "10000"],
            cancellationTokenSource.Token));
    }
}