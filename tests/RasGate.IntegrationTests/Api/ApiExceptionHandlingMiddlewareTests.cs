using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RasGate.Core.Common;
using RasGate.Core.Rac.Exceptions;
using RasGate.Web.Middlewares;

namespace RasGate.IntegrationTests.Api;

public sealed class ApiExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task ExpectedClientFailure_IsWarningAndNotError()
    {
        var logger =
            new CollectingLogger<ApiExceptionHandlingMiddleware>();
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw new RacCapacityExceededException(
                "All RAC execution slots are currently occupied."),
            logger);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            context.Response.StatusCode);
        Assert.True(
            context.Response.Headers.ContainsKey("X-Trace-Id"));
        Assert.Single(logger.Events);
        Assert.Equal(LogLevel.Warning, logger.Events[0].Level);
        Assert.Null(logger.Events[0].Exception);
    }

    [Fact]
    public async Task UnexpectedFailure_IsSingleErrorWithGenericResponse()
    {
        var exception = new InvalidOperationException(
            "sensitive implementation detail");
        var logger =
            new CollectingLogger<ApiExceptionHandlingMiddleware>();
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw exception,
            logger);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            context.Response.StatusCode);
        Assert.True(
            context.Response.Headers.ContainsKey("X-Trace-Id"));
        Assert.Single(logger.Events);
        Assert.Equal(LogLevel.Error, logger.Events[0].Level);
        Assert.Same(exception, logger.Events[0].Exception);

        context.Response.Body.Position = 0;
        var response = await JsonSerializer
            .DeserializeAsync<ApiResponse<object>>(
                context.Response.Body,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal("internal_error", response.Error?.Code);
        Assert.DoesNotContain(
            "sensitive implementation detail",
            response.Error?.Message ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRacOutcome_IsBadGatewayAndExplicitlyUnsafeToRetry()
    {
        var middleware = new ApiExceptionHandlingMiddleware(
            _ => throw new RacExecutionOutcomeUnknownException(
                "RAC process cleanup failed; the command outcome is unknown.",
                new IOException("simulated cleanup failure")),
            new CollectingLogger<ApiExceptionHandlingMiddleware>());
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(
            StatusCodes.Status502BadGateway,
            context.Response.StatusCode);

        context.Response.Body.Position = 0;
        var response = await JsonSerializer
            .DeserializeAsync<ApiResponse<object>>(
                context.Response.Body,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web));

        Assert.Equal(
            "rac_execution_outcome_unknown",
            response?.Error?.Code);
        Assert.Contains(
            "Automatic retry is unsafe",
            response?.Error?.Message,
            StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<LogEvent> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
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
            Events.Add(new LogEvent(logLevel, exception));
        }
    }

    private sealed record LogEvent(
        LogLevel Level,
        Exception? Exception);
}