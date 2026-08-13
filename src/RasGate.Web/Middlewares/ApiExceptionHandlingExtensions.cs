using System.Net;
using RasGate.Core.Common;
using RasGate.Core.Rac.Exceptions;
using RasGate.Web.Api;

namespace RasGate.Web.Middlewares;

public static class ApiExceptionHandlingExtensions
{
    public static void UseApiExceptionHandling(
        this IApplicationBuilder app)
    {
        app.UseMiddleware<ApiExceptionHandlingMiddleware>();
    }
}

public sealed class ApiExceptionHandlingMiddleware
{
    private readonly ILogger<ApiExceptionHandlingMiddleware> _logger;
    private readonly RequestDelegate _next;

    public ApiExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
            when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
                throw;

            await WriteErrorAsync(context, exception);
        }
    }

    private async Task WriteErrorAsync(
        HttpContext context,
        Exception exception)
    {
        var (statusCode, response) = MapException(exception);
        var traceId = ApiTrace.GetTraceId(context);

        if (statusCode >= HttpStatusCode.InternalServerError)
            _logger.LogError(
                exception,
                "Unhandled exception. TraceId: {TraceId}",
                traceId);
        else
            _logger.LogWarning(
                "Request failed with {ExceptionType}: {Message}. " +
                "TraceId: {TraceId}",
                exception.GetType().Name,
                exception.Message,
                traceId);

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        context.Response.Headers[ApiTrace.HeaderName] = traceId;

        await context.Response.WriteAsJsonAsync(
            response,
            ApiJson.Default,
            context.RequestAborted);
    }

    private static (
        HttpStatusCode StatusCode,
        ApiResponse<object> Response) MapException(
            Exception exception)
    {
        return exception switch
        {
            RacCapacityExceededException capacity =>
            (
                HttpStatusCode.TooManyRequests,
                ApiResponse<object>.Fail(
                    new ApiError(
                        "rac_capacity_exceeded",
                        capacity.Message))),

            RacUnavailableException unavailable =>
            (
                HttpStatusCode.ServiceUnavailable,
                ApiResponse<object>.Fail(
                    new ApiError(
                        "rac_unavailable",
                        unavailable.Message))),

            RacOutputLimitExceededException limit =>
            (
                HttpStatusCode.BadGateway,
                ApiResponse<object>.Fail(
                    new ApiError(
                        "rac_output_limit_exceeded",
                        $"{limit.Message} The external command " +
                        "outcome is unknown; automatic retry " +
                        "is unsafe."))),

            RacExecutionOutcomeUnknownException unknown =>
            (
                HttpStatusCode.BadGateway,
                ApiResponse<object>.Fail(
                    new ApiError(
                        "rac_execution_outcome_unknown",
                        $"{unknown.Message} Automatic retry is unsafe."))),

            RacArgumentValidationException argument =>
            (
                HttpStatusCode.BadRequest,
                ApiResponse<object>.Fail(
                    new ApiError(
                        "bad_request",
                        argument.Message))),

            _ =>
            (
                HttpStatusCode.InternalServerError,
                ApiResponse<object>.FailWithDefaultError(
                    HttpStatusCode.InternalServerError))
        };
    }
}