using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RasGate.Core.Rac;
using RasGate.Core.Rac.Exceptions;

namespace RasGate.Infrastructure.Rac;

public sealed class RacExecutor : IRacExecutor, IDisposable, IAsyncDisposable
{
    private static readonly EventId InitialStatusEvent = new(
        2000,
        "RacInitialStatusObserved");

    private static readonly EventId StatusChangedEvent = new(
        2001,
        "RacAvailabilityChanged");

    private readonly object _lifetimeSync = new();
    private readonly ILogger<RacExecutor> _logger;

    private readonly TaskCompletionSource _operationsDrained = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly RacOptions _options;
    private readonly CancellationTokenSource _shutdownSource = new();
    private readonly SemaphoreSlim _slots;
    private readonly object _statusSync = new();

    private int _activeOperations;
    private RacStatus? _cachedStatus;
    private bool _resourcesDisposed;
    private long _statusCachedAt;
    private bool _statusProbeQuarantined;
    private Task<RacStatus>? _statusRefreshTask;
    private bool _stopping;
    private Exception? _terminalLifetimeFailure;

    public RacExecutor(
        IOptions<RacOptions> options,
        ILogger<RacExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;

        _slots = new SemaphoreSlim(
            _options.MaxConcurrentProcesses,
            _options.MaxConcurrentProcesses);
    }

    public async ValueTask DisposeAsync()
    {
        var operationsDrained = BeginShutdown();

        await operationsDrained.ConfigureAwait(false);

        var terminalFailure = GetTerminalLifetimeFailure();

        if (terminalFailure is not null)
            throw new InvalidOperationException(
                "RAC executor shutdown completed with unconfirmed process " +
                "termination; an external process may still be running.",
                terminalFailure);
    }

    public void Dispose()
    {
        BeginShutdown().GetAwaiter().GetResult();

        var terminalFailure = GetTerminalLifetimeFailure();

        if (terminalFailure is not null)
            _logger.LogCritical(
                terminalFailure,
                "RAC executor stopped after process termination could not " +
                "be confirmed; an external process may still be running.");
    }

    public Task<RacStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfStopping();
        cancellationToken.ThrowIfCancellationRequested();

        Task<RacStatus> statusTask;

        lock (_statusSync)
        {
            if (_statusProbeQuarantined && _cachedStatus is not null)
                return Task.FromResult(_cachedStatus);

            if (_cachedStatus is not null &&
                Stopwatch.GetElapsedTime(_statusCachedAt) <
                TimeSpan.FromSeconds(
                    _options.StatusCacheSeconds))
                return Task.FromResult(_cachedStatus);

            if (_statusRefreshTask is null ||
                _statusRefreshTask.IsCompleted)
                _statusRefreshTask = RefreshStatusAsync();

            statusTask = _statusRefreshTask;
        }

        // The shared status probe does not occupy a command slot. A cancelled
        // caller only stops waiting for it.
        return statusTask.WaitAsync(cancellationToken);
    }

    public async Task<RacExecutionResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        EnterOperation();

        var releaseSlot = true;
        var slotAcquired = false;

        try
        {
            RacArgumentValidator.Validate(arguments, _options);

            _shutdownSource.Token.ThrowIfCancellationRequested();

            if (!await _slots.WaitAsync(
                        TimeSpan.Zero,
                        cancellationToken)
                    .ConfigureAwait(false))
                throw new RacCapacityExceededException(
                    "All RAC execution slots are currently occupied.");

            slotAcquired = true;

            cancellationToken.ThrowIfCancellationRequested();
            _shutdownSource.Token.ThrowIfCancellationRequested();

            try
            {
                return await ExecuteProcessAsync(
                        arguments,
                        cancellationToken,
                        _shutdownSource.Token)
                    .ConfigureAwait(false);
            }
            catch (RacExecutionOutcomeUnknownException exception)
                when (!exception.ProcessTerminationConfirmed)
            {
                releaseSlot = false;
                RecordTerminalLifetimeFailure(exception);

                _logger.LogCritical(
                    exception,
                    "RAC process termination could not be confirmed; " +
                    "the execution slot has been quarantined until restart.");

                throw;
            }
        }
        finally
        {
            try
            {
                if (slotAcquired && releaseSlot)
                    // A slot is reusable only after process and pipe cleanup.
                    _slots.Release();
            }
            finally
            {
                ExitOperation();
            }
        }
    }

    private async Task<RacStatus> RefreshStatusAsync()
    {
        EnterOperation();

        try
        {
            RacStatus status;
            var quarantineStatusProbe = false;

            try
            {
                var result = await ExecuteProcessAsync(
                        ["--version"],
                        CancellationToken.None,
                        _shutdownSource.Token)
                    .ConfigureAwait(false);

                var available = result.ExitCode == 0;

                status = new RacStatus
                {
                    Available = available,
                    Version = available
                        ? result.StandardOutput.Trim()
                        : null,
                    ExecutablePath = _options.ExecutablePath,
                    Message = available
                        ? ""
                        : result.TimedOut
                            ? "RAC status probe timed out; availability is unknown."
                            : result.StandardError.Trim()
                };
            }
            catch (RacUnavailableException exception)
            {
                status = new RacStatus
                {
                    Available = false,
                    Version = null,
                    ExecutablePath = _options.ExecutablePath,
                    Message = exception.Message
                };
            }
            catch (RacOutputLimitExceededException)
            {
                status = CreateUnavailableStatus(
                    "RAC status output exceeded the configured limit.");
            }
            catch (RacExecutionOutcomeUnknownException exception)
                when (!exception.ProcessTerminationConfirmed)
            {
                quarantineStatusProbe = true;
                RecordTerminalLifetimeFailure(exception);

                _logger.LogCritical(
                    exception,
                    "RAC status process termination could not be confirmed; " +
                    "status probes have been quarantined until restart.");

                status = CreateUnavailableStatus(
                    "RAC status process termination could not be confirmed; " +
                    "status probes are disabled until restart.");
            }
            catch (RacExecutionOutcomeUnknownException)
            {
                status = CreateUnavailableStatus(
                    "RAC status probe did not produce a confirmed result.");
            }

            RacStatus? previousStatus;

            lock (_statusSync)
            {
                previousStatus = _cachedStatus;
                _statusProbeQuarantined |= quarantineStatusProbe;
                _cachedStatus = status;
                _statusCachedAt = Stopwatch.GetTimestamp();
            }

            LogStatusChange(previousStatus, status);

            return status;
        }
        finally
        {
            ExitOperation();
        }
    }

    private RacStatus CreateUnavailableStatus(string message)
    {
        return new RacStatus
        {
            Available = false,
            Version = null,
            ExecutablePath = _options.ExecutablePath,
            Message = message
        };
    }

    private void LogStatusChange(
        RacStatus? previousStatus,
        RacStatus currentStatus)
    {
        if (previousStatus is not null &&
            previousStatus.Available == currentStatus.Available)
            return;

        using var scope = _logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["Phase"] = "RAC",
                ["RacOperation"] = "status-probe"
            });

        if (previousStatus is null)
        {
            _logger.Log(
                currentStatus.Available
                    ? LogLevel.Information
                    : LogLevel.Warning,
                InitialStatusEvent,
                "Initial RAC status probe completed with availability " +
                "{RacAvailable}.",
                currentStatus.Available);

            return;
        }

        _logger.Log(
            currentStatus.Available
                ? LogLevel.Information
                : LogLevel.Warning,
            StatusChangedEvent,
            "RAC availability changed from {PreviousRacAvailable} to " +
            "{RacAvailable}.",
            previousStatus.Available,
            currentStatus.Available);
    }

    private async Task<RacExecutionResult> ExecuteProcessAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        CancellationToken shutdownToken)
    {
        var outputEncoding = OperatingSystem.IsWindows()
            ? CodePagesEncodingProvider.Instance.GetEncoding(866)
              ?? throw new InvalidOperationException(
                  "Code page 866 is not available.")
            : Encoding.UTF8;

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // Nothing that can reject configuration may run after Process.Start.
        using var timeoutCancellationTokenSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var linkedCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellationTokenSource.Token,
                shutdownToken);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to start RAC executable.");

            throw new RacUnavailableException(
                "RAC executable could not be started.",
                exception);
        }

        StreamReader? standardOutputReader = null;
        StreamReader? standardErrorReader = null;
        OutputCapture? standardOutput = null;
        OutputCapture? standardError = null;
        CancellationTokenSource? outputStopSource = null;
        Task? standardOutputTask = null;
        Task? standardErrorTask = null;

        try
        {
            Exception? executionFailure = null;
            var cancellationObserved = false;

            try
            {
                standardOutputReader = process.StandardOutput;
                standardErrorReader = process.StandardError;
                standardOutput = new OutputCapture(
                    standardOutputReader.BaseStream,
                    outputEncoding,
                    _options.MaxOutputBytes);
                standardError = new OutputCapture(
                    standardErrorReader.BaseStream,
                    outputEncoding,
                    _options.MaxOutputBytes);
                outputStopSource = new CancellationTokenSource();

                standardOutputTask = standardOutput.ReadAsync(
                    outputStopSource.Token);
                standardErrorTask = standardError.ReadAsync(
                    outputStopSource.Token);

                var processExitTask = process.WaitForExitAsync(
                    CancellationToken.None);

                cancellationObserved = await MonitorLifecycleAsync(
                        processExitTask,
                        standardOutputTask,
                        standardErrorTask,
                        linkedCancellationTokenSource.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                executionFailure = exception;
            }

            var requiresCleanup =
                executionFailure is not null || cancellationObserved;

            if (requiresCleanup)
            {
                ProcessCleanupResult cleanup;

                try
                {
                    cleanup = await CleanupProcessAsync(
                            process,
                            standardOutputReader,
                            standardErrorReader,
                            outputStopSource,
                            standardOutputTask,
                            standardErrorTask)
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    throw new RacExecutionOutcomeUnknownException(
                        "RAC process cleanup failed and process " +
                        "termination could not be confirmed; the command " +
                        "outcome is unknown.",
                        CombineFailures(
                            executionFailure,
                            cleanupFailure),
                        false);
                }

                if (!cleanup.ProcessTerminationConfirmed)
                    throw new RacExecutionOutcomeUnknownException(
                        "RAC process termination could not be confirmed; " +
                        "the command outcome is unknown.",
                        CombineFailures(
                            executionFailure,
                            cleanup.Failure ?? new InvalidOperationException(
                                "Process termination was not confirmed.")),
                        false);

                if (cleanup.Failure is not null)
                {
                    _logger.LogError(
                        cleanup.Failure,
                        "Failed to clean up a started RAC process; " +
                        "the command outcome is unknown.");

                    throw new RacExecutionOutcomeUnknownException(
                        "RAC process cleanup failed; " +
                        "the command outcome is unknown.",
                        CombineFailures(
                            executionFailure,
                            cleanup.Failure));
                }
            }

            if (executionFailure is not null)
            {
                if (executionFailure is
                    RacOutputLimitExceededException)
                    ExceptionDispatchInfo
                        .Capture(executionFailure)
                        .Throw();

                throw new RacExecutionOutcomeUnknownException(
                    "RAC execution failed after the process started; " +
                    "the command outcome is unknown.",
                    executionFailure);
            }

            if (cancellationObserved &&
                cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();

            if (cancellationObserved &&
                shutdownToken.IsCancellationRequested)
                shutdownToken.ThrowIfCancellationRequested();

            var timedOut = cancellationObserved;

            stopwatch.Stop();

            return new RacExecutionResult
            {
                ExitCode = timedOut
                    ? -1
                    : process.ExitCode,
                StandardOutput = standardOutput?.GetText() ?? "",
                StandardError = standardError?.GetText() ?? "",
                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                TimedOut = timedOut
            };
        }
        finally
        {
            var finalizationFailure = FinalizeOutputResources(
                outputStopSource,
                standardOutputReader,
                standardErrorReader,
                standardOutput,
                standardError);

            if (finalizationFailure is not null)
                _logger.LogError(
                    finalizationFailure,
                    "Failed to finalize RAC output resources.");
        }
    }

    private static async Task<bool> MonitorLifecycleAsync(
        Task processExitTask,
        Task standardOutputTask,
        Task standardErrorTask,
        CancellationToken cancellationToken)
    {
        var cancellationSignal =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

        using var cancellationRegistration =
            cancellationToken.UnsafeRegister(
                static state =>
                    ((TaskCompletionSource)state!)
                    .TrySetResult(),
                cancellationSignal);

        var pendingTasks = new HashSet<Task>
        {
            processExitTask,
            standardOutputTask,
            standardErrorTask
        };

        while (pendingTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(
                    pendingTasks.Append(cancellationSignal.Task))
                .ConfigureAwait(false);

            if (completedTask == cancellationSignal.Task)
            {
                // A process or pipe failure takes precedence over cancellation.
                var failedTask = pendingTasks.FirstOrDefault(task => task.IsFaulted);

                if (failedTask is not null)
                    await failedTask.ConfigureAwait(false);

                if (pendingTasks.All(task => task.IsCompletedSuccessfully))
                {
                    await Task.WhenAll(pendingTasks)
                        .ConfigureAwait(false);
                    return false;
                }

                return true;
            }

            await completedTask.ConfigureAwait(false);
            pendingTasks.Remove(completedTask);
        }

        return false;
    }

    private static async Task<ProcessCleanupResult> CleanupProcessAsync(
        Process process,
        StreamReader? standardOutputReader,
        StreamReader? standardErrorReader,
        CancellationTokenSource? outputStopSource,
        Task? standardOutputTask,
        Task? standardErrorTask)
    {
        var failures = new List<Exception>();
        var processExited = false;

        // Close the pipes even if Kill fails; the process may be blocked on
        // writing to them.
        try
        {
            outputStopSource?.Cancel();
        }
        catch (AggregateException exception)
        {
            failures.Add(exception);
        }

        DisposeReader(standardOutputReader, failures);
        DisposeReader(standardErrorReader, failures);

        try
        {
            processExited = process.HasExited;
        }
        catch (Exception exception)
            when (IsProcessControlException(exception))
        {
            failures.Add(exception);
        }

        if (!processExited)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // The process exited after the HasExited check.
            }
            catch (Exception exception)
                when (exception is Win32Exception or
                          NotSupportedException or
                          AggregateException)
            {
                failures.Add(exception);
            }

            try
            {
                await process
                    .WaitForExitAsync(CancellationToken.None)
                    .ConfigureAwait(false);

                processExited = true;
            }
            catch (Exception exception)
                when (IsProcessControlException(exception))
            {
                failures.Add(exception);

                try
                {
                    processExited = process.HasExited;
                }
                catch (Exception confirmationFailure)
                    when (IsProcessControlException(
                              confirmationFailure))
                {
                    failures.Add(confirmationFailure);
                }
            }
        }

        var outputTasks = new[]
            {
                standardOutputTask,
                standardErrorTask
            }
            .OfType<Task>()
            .ToArray();

        if (outputTasks.Length > 0)
            try
            {
                await Task.WhenAll(outputTasks)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (exception is not
                        RacOutputLimitExceededException &&
                    !IsExpectedOutputStopException(exception))
                    failures.Add(exception);
            }

        var failure = failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(failures)
        };

        return new ProcessCleanupResult(
            failure,
            processExited);
    }

    private static void DisposeReader(
        StreamReader? reader,
        ICollection<Exception> failures)
    {
        try
        {
            reader?.Dispose();
        }
        catch (Exception exception)
            when (exception is IOException or
                      ObjectDisposedException)
        {
            failures.Add(exception);
        }
    }

    private static bool IsExpectedOutputStopException(
        Exception exception)
    {
        if (exception is OperationCanceledException or
            ObjectDisposedException or IOException)
            return true;

        return exception is AggregateException aggregateException &&
               aggregateException.InnerExceptions.All(
                   IsExpectedOutputStopException);
    }

    private static Exception? FinalizeOutputResources(
        CancellationTokenSource? outputStopSource,
        StreamReader? standardOutputReader,
        StreamReader? standardErrorReader,
        OutputCapture? standardOutput,
        OutputCapture? standardError)
    {
        var failures = new List<Exception>();

        TryFinalize(
            () => outputStopSource?.Cancel(),
            failures);
        TryFinalize(
            () => standardOutputReader?.Dispose(),
            failures);
        TryFinalize(
            () => standardErrorReader?.Dispose(),
            failures);
        TryFinalize(
            () => standardOutput?.Dispose(),
            failures);
        TryFinalize(
            () => standardError?.Dispose(),
            failures);
        TryFinalize(
            () => outputStopSource?.Dispose(),
            failures);

        return failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(failures)
        };
    }

    private static void TryFinalize(
        Action action,
        ICollection<Exception> failures)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static bool IsProcessControlException(
        Exception exception)
    {
        return exception is Win32Exception or
            InvalidOperationException or
            NotSupportedException or
            AggregateException;
    }

    private static Exception CombineFailures(
        Exception? executionFailure,
        Exception cleanupFailure)
    {
        return executionFailure is null
            ? cleanupFailure
            : new AggregateException(
                executionFailure,
                cleanupFailure);
    }

    private Task BeginShutdown()
    {
        lock (_lifetimeSync)
        {
            if (!_stopping)
            {
                _stopping = true;
                _shutdownSource.Cancel();
            }

            CompleteShutdownIfDrained();

            return _operationsDrained.Task;
        }
    }

    private void EnterOperation()
    {
        lock (_lifetimeSync)
        {
            ObjectDisposedException.ThrowIf(
                _stopping,
                this);

            _activeOperations++;
        }
    }

    private void ExitOperation()
    {
        lock (_lifetimeSync)
        {
            _activeOperations--;

            if (_activeOperations < 0)
                throw new InvalidOperationException(
                    "RAC executor operation tracking became inconsistent.");

            CompleteShutdownIfDrained();
        }
    }

    private void ThrowIfStopping()
    {
        lock (_lifetimeSync)
        {
            ObjectDisposedException.ThrowIf(
                _stopping,
                this);
        }
    }

    private void RecordTerminalLifetimeFailure(Exception failure)
    {
        lock (_lifetimeSync)
        {
            _terminalLifetimeFailure = _terminalLifetimeFailure is null
                ? failure
                : new AggregateException(
                    _terminalLifetimeFailure,
                    failure);
        }
    }

    private Exception? GetTerminalLifetimeFailure()
    {
        lock (_lifetimeSync)
        {
            return _terminalLifetimeFailure;
        }
    }

    private void CompleteShutdownIfDrained()
    {
        if (!_stopping ||
            _activeOperations != 0 ||
            _resourcesDisposed)
            return;

        _resourcesDisposed = true;
        _slots.Dispose();
        _shutdownSource.Dispose();
        _operationsDrained.TrySetResult();
    }

    private sealed record ProcessCleanupResult(
        Exception? Failure,
        bool ProcessTerminationConfirmed);

    private sealed class OutputCapture(
        Stream stream,
        Encoding encoding,
        int maxOutputBytes) : IDisposable
    {
        private readonly MemoryStream _output = new();

        public void Dispose()
        {
            _output.Dispose();
        }

        public async Task ReadAsync(
            CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];

            try
            {
                while (true)
                {
                    var bytesRead = await stream.ReadAsync(
                            buffer,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (bytesRead == 0)
                        break;

                    if (_output.Length + bytesRead >
                        maxOutputBytes)
                        throw new RacOutputLimitExceededException(
                            maxOutputBytes);

                    await _output.WriteAsync(
                            buffer.AsMemory(0, bytesRead),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
                when (cancellationToken.IsCancellationRequested &&
                      exception is OperationCanceledException or
                          ObjectDisposedException or IOException)
            {
                // Cleanup stopped the read; keep the bytes already captured.
            }
        }

        public string GetText()
        {
            return encoding.GetString(
                _output.GetBuffer(),
                0,
                checked((int)_output.Length));
        }
    }
}