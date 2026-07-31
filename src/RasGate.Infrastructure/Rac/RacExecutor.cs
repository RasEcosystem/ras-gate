using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RasGate.Application.Rac;
using RasGate.Application.Rac.Exceptions;
using RasGate.Application.Rac.Interfaces;

namespace RasGate.Infrastructure.Rac;

public sealed class RacExecutor : IRacExecutor, IDisposable
{
    private readonly ILogger<RacExecutor> _logger;
    private readonly RacOptions _options;
    private readonly SemaphoreSlim _slots;

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

    public void Dispose()
    {
        _slots.Dispose();
    }

    public async Task<RacStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ExecuteAsync(
                ["--version"],
                cancellationToken);

            var available = result.ExitCode == 0;

            return new RacStatus
            {
                Available = available,
                Version = available
                    ? result.StandardOutput.Trim()
                    : null,
                ExecutablePath = _options.ExecutablePath,
                Message = available
                    ? ""
                    : result.StandardError.Trim()
            };
        }
        catch (RacUnavailableException exception)
        {
            return new RacStatus
            {
                Available = false,
                Version = null,
                ExecutablePath = _options.ExecutablePath,
                Message = exception.Message
            };
        }
    }

    public async Task<RacExecutionResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
            throw new ArgumentException(
                "At least one RAC argument is required.",
                nameof(arguments));

        if (!await _slots.WaitAsync(
                TimeSpan.Zero,
                cancellationToken))
            throw new RacCapacityExceededException(
                "All RAC execution slots are currently occupied.");

        try
        {
            try
            {
                return await ExecuteProcessAsync(
                    arguments,
                    cancellationToken);
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
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<RacExecutionResult> ExecuteProcessAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var outputEncoding = OperatingSystem.IsWindows()
            ? Encoding.GetEncoding(866)
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

        using var process = new Process
        {
            StartInfo = startInfo
        };

        var stopwatch = Stopwatch.StartNew();

        process.Start();

        var standardOutputTask = ReadOutputAsync(
            process.StandardOutput.BaseStream,
            outputEncoding,
            _options.MaxOutputBytes);

        var standardErrorTask = ReadOutputAsync(
            process.StandardError.BaseStream,
            outputEncoding,
            _options.MaxOutputBytes);

        var outputTask = Task.WhenAll(
            standardOutputTask,
            standardErrorTask);

        using var timeoutCancellationTokenSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var linkedCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellationTokenSource.Token);

        var processExitTask = process.WaitForExitAsync(
            linkedCancellationTokenSource.Token);

        var monitoredTasks = new HashSet<Task>
        {
            processExitTask,
            standardOutputTask,
            standardErrorTask
        };

        var timedOut = false;

        try
        {
            while (true)
            {
                var completedTask =
                    await Task.WhenAny(monitoredTasks);

                await completedTask;

                if (completedTask == processExitTask)
                    break;

                monitoredTasks.Remove(completedTask);
            }

            await outputTask;
        }
        catch (RacOutputLimitExceededException)
        {
            TryKillProcess(process);

            await WaitForExitSafelyAsync(process);
            await IgnoreOutputErrorsAsync(outputTask);

            throw;
        }
        catch (OperationCanceledException)
            when (timeoutCancellationTokenSource.IsCancellationRequested
                  && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;

            TryKillProcess(process);

            await WaitForExitSafelyAsync(process);
            await IgnoreOutputErrorsAsync(outputTask);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);

            await WaitForExitSafelyAsync(process);
            await IgnoreOutputErrorsAsync(outputTask);

            throw;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        stopwatch.Stop();

        return new RacExecutionResult
        {
            ExitCode = timedOut
                ? -1
                : process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            TimedOut = timedOut
        };
    }

    private static async Task IgnoreOutputErrorsAsync(Task outputTask)
    {
        try
        {
            await outputTask;
        }
        catch
        {
            // ignored
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static async Task WaitForExitSafelyAsync(
        Process process)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync(
                    CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task<string> ReadOutputAsync(
        Stream stream,
        Encoding encoding,
        int maxOutputBytes)
    {
        var buffer = new byte[8192];

        await using var output = new MemoryStream();

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer);

            if (bytesRead == 0)
                break;

            if (output.Length + bytesRead > maxOutputBytes)
                throw new RacOutputLimitExceededException(maxOutputBytes);

            await output.WriteAsync(buffer.AsMemory(0, bytesRead));
        }

        return encoding.GetString(output.ToArray());
    }
}