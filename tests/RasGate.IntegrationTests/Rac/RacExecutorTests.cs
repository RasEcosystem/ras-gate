using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RasGate.Core.Rac.Exceptions;
using RasGate.Infrastructure.Rac;

namespace RasGate.IntegrationTests.Rac;

[Collection("RAC process lifecycle")]
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
        string? executablePath = null,
        int maxOutputBytes = 4194304,
        int maxArgumentCount =
            RacOptions.MaximumArgumentCount,
        int maxArgumentBytes =
            RacOptions.MaximumArgumentBytes,
        int maxTotalArgumentBytes =
            RacOptions.MaximumTotalArgumentBytes,
        ILogger<RacExecutor>? logger = null)
    {
        var options = Options.Create(
            new RacOptions
            {
                ExecutablePath =
                    executablePath ?? GetFakeRacPath(),
                TimeoutSeconds = timeoutSeconds,
                MaxConcurrentProcesses =
                    maxConcurrentProcesses,
                MaxOutputBytes = maxOutputBytes,
                MaxArgumentCount = maxArgumentCount,
                MaxArgumentBytes = maxArgumentBytes,
                MaxTotalArgumentBytes =
                    maxTotalArgumentBytes
            });

        return new RacExecutor(
            options,
            logger ?? NullLogger<RacExecutor>.Instance);
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
    public async Task ExecuteAsync_OutputAtLimit_ReturnsFullOutput()
    {
        const int maxOutputBytes = 1024;

        using var executor = CreateExecutor(
            maxOutputBytes: maxOutputBytes);

        var result = await executor.ExecuteAsync(
            ["__test", "large-output", maxOutputBytes.ToString()],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            new string('X', maxOutputBytes),
            result.StandardOutput);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task ExecuteAsync_OutputExceedsLimit_ThrowsImmediately()
    {
        const int maxOutputBytes = 1024;

        using var executor = CreateExecutor(
            10,
            maxOutputBytes: maxOutputBytes);

        var stopwatch = Stopwatch.StartNew();

        var exception =
            await Assert.ThrowsAsync<RacOutputLimitExceededException>(() => executor.ExecuteAsync(
                ["__test", "large-output", "1048576"],
                CancellationToken.None));

        stopwatch.Stop();

        Assert.Equal(
            $"RAC output exceeded the configured limit of " +
            $"{maxOutputBytes} bytes.",
            exception.Message);

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Output limit was detected after {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ExecuteAsync_StandardErrorExceedsLimit_Throws()
    {
        const int maxOutputBytes = 1024;

        using var executor = CreateExecutor(
            maxOutputBytes: maxOutputBytes);

        var exception =
            await Assert.ThrowsAsync<RacOutputLimitExceededException>(() => executor.ExecuteAsync(
                [
                    "__test",
                    "stderr",
                    new string('X', maxOutputBytes + 1)
                ],
                CancellationToken.None));

        Assert.Equal(
            $"RAC output exceeded the configured limit of " +
            $"{maxOutputBytes} bytes.",
            exception.Message);
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
        var pidFile = CreatePidFilePath();

        using var executor = CreateExecutor(
            1);

        try
        {
            var executionTask = executor.ExecuteAsync(
                ["__test", "pid-delay", "10000", pidFile],
                CancellationToken.None);

            var processId = await ReadProcessIdAsync(pidFile);
            var result = await executionTask;

            Assert.True(result.TimedOut);
            Assert.Equal(-1, result.ExitCode);
            Assert.True(result.DurationMilliseconds >= 900);
            Assert.True(result.DurationMilliseconds < 5000);

            await AssertProcessExitedAsync(processId);
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ParentExitsWhileDescendantHoldsPipes_TimeoutStillAppliesAndSlotIsReleased()
    {
        var childPidFile = CreatePidFilePath();
        int? childProcessId = null;

        using var executor = CreateExecutor(
            1,
            1);

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var executionTask = executor.ExecuteAsync(
                [
                    "__test",
                    "spawn-pipe-holder",
                    "30000",
                    childPidFile
                ],
                CancellationToken.None);

            childProcessId = await ReadProcessIdAsync(
                childPidFile);

            var result = await executionTask;

            stopwatch.Stop();

            Assert.True(result.TimedOut);
            Assert.Equal(-1, result.ExitCode);
            Assert.Contains(
                "Started pipe holder",
                result.StandardOutput);
            Assert.InRange(
                stopwatch.Elapsed,
                TimeSpan.FromMilliseconds(900),
                TimeSpan.FromSeconds(5));

            var nextResult = await executor.ExecuteAsync(
                ["--version"],
                CancellationToken.None);

            Assert.Equal(0, nextResult.ExitCode);
        }
        finally
        {
            if (childProcessId is not null)
                TryKillProcess(childProcessId.Value);

            File.Delete(childPidFile);
        }
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
    public async Task GetStatusAsync_CommandSlotOccupied_UsesIndependentSingleFlightBudget()
    {
        var pidFile = CreatePidFilePath();

        using var executor = CreateExecutor(
            5,
            1);

        try
        {
            var executionTask = executor.ExecuteAsync(
                ["__test", "pid-delay", "1500", pidFile],
                CancellationToken.None);

            await ReadProcessIdAsync(pidFile);

            var statusTasks = Enumerable.Range(0, 10)
                .Select(_ => executor.GetStatusAsync(
                    CancellationToken.None))
                .ToArray();

            var statuses = await Task.WhenAll(statusTasks);

            Assert.All(statuses, status =>
                Assert.True(status.Available));
            Assert.All(statuses, status =>
                Assert.Same(statuses[0], status));

            var result = await executionTask;
            Assert.Equal(0, result.ExitCode);

            var cachedStatus = await executor.GetStatusAsync(
                CancellationToken.None);

            Assert.Same(statuses[0], cachedStatus);
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_EmptyArguments_ThrowsArgumentValidationException()
    {
        using var executor = CreateExecutor();

        await Assert.ThrowsAsync<RacArgumentValidationException>(() =>
            executor.ExecuteAsync(
                [],
                CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_TooManyArguments_ThrowsBeforeAcquiringSlot()
    {
        using var executor = CreateExecutor(
            maxConcurrentProcesses: 1,
            maxArgumentCount: 2);

        var runningTask = executor.ExecuteAsync(
            ["__test", "delay"],
            CancellationToken.None);

        await Assert.ThrowsAsync<RacArgumentValidationException>(() =>
            executor.ExecuteAsync(
                ["one", "two", "three"],
                CancellationToken.None));

        await runningTask;
    }

    [Fact]
    public async Task ExecuteAsync_ArgumentTooLarge_ThrowsArgumentValidationException()
    {
        using var executor = CreateExecutor(
            maxArgumentBytes: 4);

        await Assert.ThrowsAsync<RacArgumentValidationException>(() =>
            executor.ExecuteAsync(
                ["12345"],
                CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_NullCharacterInArgument_IsRejectedBeforeStart()
    {
        using var executor = CreateExecutor(
            executablePath: Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString(),
                "missing-rac"));

        await Assert.ThrowsAsync<RacArgumentValidationException>(() =>
            executor.ExecuteAsync(
                ["invalid\0argument"],
                CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_TotalArgumentsTooLarge_ThrowsArgumentValidationException()
    {
        using var executor = CreateExecutor(
            maxArgumentBytes: 3,
            maxTotalArgumentBytes: 5);

        await Assert.ThrowsAsync<RacArgumentValidationException>(() =>
            executor.ExecuteAsync(
                ["aa", "bb", "cc"],
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
        var pidFile = CreatePidFilePath();

        using var executor = CreateExecutor(
            10);

        using var cancellationTokenSource =
            new CancellationTokenSource();

        try
        {
            var executionTask = executor.ExecuteAsync(
                ["__test", "pid-delay", "10000", pidFile],
                cancellationTokenSource.Token);

            var processId = await ReadProcessIdAsync(pidFile);

            cancellationTokenSource.Cancel();

            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executionTask);

            Assert.Equal(
                cancellationTokenSource.Token,
                exception.CancellationToken);

            await AssertProcessExitedAsync(processId);

            var nextResult = await executor.ExecuteAsync(
                ["--version"],
                CancellationToken.None);

            Assert.Equal(0, nextResult.ExitCode);
        }
        finally
        {
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task DisposeAsync_InFlightExecution_CancelsAndDrainsProcess()
    {
        var pidFile = CreatePidFilePath();
        var executor = CreateExecutor(30);

        try
        {
            var executionTask = executor.ExecuteAsync(
                ["__test", "pid-delay", "30000", pidFile],
                CancellationToken.None);

            var processId = await ReadProcessIdAsync(pidFile);

            await executor.DisposeAsync().AsTask().WaitAsync(
                TimeSpan.FromSeconds(5));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executionTask);

            await AssertProcessExitedAsync(processId);

            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                executor.ExecuteAsync(
                    ["--version"],
                    CancellationToken.None));
        }
        finally
        {
            await executor.DisposeAsync();
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task Dispose_InFlightExecution_CancelsAndDrainsProcess()
    {
        var pidFile = CreatePidFilePath();
        var executor = CreateExecutor(30);

        try
        {
            var executionTask = executor.ExecuteAsync(
                ["__test", "pid-delay", "30000", pidFile],
                CancellationToken.None);

            var processId = await ReadProcessIdAsync(pidFile);

            await Task.Run(executor.Dispose).WaitAsync(
                TimeSpan.FromSeconds(5));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executionTask);

            await AssertProcessExitedAsync(processId);
        }
        finally
        {
            executor.Dispose();
            File.Delete(pidFile);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RepeatedCalls_DisposeRedirectedPipeHandles()
    {
        if (!OperatingSystem.IsLinux())
            return;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var executor = CreateExecutor();

        var handlesBefore = CountLinuxFileDescriptors();

        for (var index = 0; index < 64; index++)
        {
            var result = await executor.ExecuteAsync(
                ["--version"],
                CancellationToken.None);

            Assert.Equal(0, result.ExitCode);
        }

        var handlesAfter = CountLinuxFileDescriptors();

        Assert.True(
            handlesAfter <= handlesBefore + 8,
            $"File descriptors grew from {handlesBefore} to " +
            $"{handlesAfter} without a garbage collection.");
    }

    [Fact]
    public async Task GetStatusAsync_LogsInitialAvailabilityOnlyOnce()
    {
        var logger = new CollectingLogger<RacExecutor>();
        using var executor = CreateExecutor(logger: logger);

        var firstStatus = await executor.GetStatusAsync(
            CancellationToken.None);
        var cachedStatus = await executor.GetStatusAsync(
            CancellationToken.None);

        Assert.True(firstStatus.Available);
        Assert.Same(firstStatus, cachedStatus);

        var statusEvent = Assert.Single(
            logger.Events,
            entry => entry.EventId.Id == 2000);

        Assert.Equal(LogLevel.Information, statusEvent.Level);
        Assert.Equal(true, statusEvent.Properties["RacAvailable"]);
        Assert.DoesNotContain(
            logger.Events,
            entry => entry.EventId.Id == 2001);

        var statusScope = Assert.Single(logger.Scopes);
        Assert.Equal("RAC", statusScope["Phase"]);
        Assert.Equal("status-probe", statusScope["RacOperation"]);
    }

    private static string CreatePidFilePath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            $"rasgate-fake-rac-{Guid.NewGuid():N}.pid");
    }

    private static async Task<int> ReadProcessIdAsync(
        string pidFile)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(5));

        while (!timeout.IsCancellationRequested)
        {
            if (File.Exists(pidFile))
            {
                var value = await File.ReadAllTextAsync(
                    pidFile,
                    timeout.Token);

                if (int.TryParse(
                        value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var processId))
                    return processId;
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(20),
                timeout.Token);
        }

        throw new TimeoutException(
            $"PID file was not created: {pidFile}");
    }

    private static async Task AssertProcessExitedAsync(
        int processId)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (!IsProcessRunning(processId))
                return;

            await Task.Delay(20);
        }

        Assert.Fail(
            $"Process {processId} was still running after cleanup.");
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryKillProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static int CountLinuxFileDescriptors()
    {
        return Directory.EnumerateFileSystemEntries(
                "/proc/self/fd")
            .Count();
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Events { get; } = [];

        public List<IReadOnlyDictionary<string, object?>> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IReadOnlyDictionary<string, object?> properties)
                Scopes.Add(properties);

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

            Events.Add(new LogEntry(logLevel, eventId, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

[CollectionDefinition(
    "RAC process lifecycle",
    DisableParallelization = true)]
public sealed class RacProcessLifecycleCollection;