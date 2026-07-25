using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using RasGate.Application.Rac.Exceptions;
using RasGate.Contracts.Common;
using RasGate.Web.Api;

namespace RasGate.Web.Middlewares;

public static class ApiExceptionHandlingExtensions
{
    public static void UseApiExceptionHandling(
        this IApplicationBuilder app)
    {
        app.UseExceptionHandler(builder =>
        {
            builder.Run(async context =>
            {
                var feature =
                    context.Features.Get<IExceptionHandlerFeature>();

                var exception = feature?.Error;

                if (exception is null)
                    throw new InvalidOperationException(
                        "Exception handler invoked without exception.");

                context.Response.ContentType = "application/json";

                var response = exception switch
                {
                    RacCapacityExceededException capacity =>
                        ApiResponse<object>.Fail(
                            HttpStatusCode.TooManyRequests,
                            new ApiError(
                                "rac_capacity_exceeded",
                                capacity.Message)),

                    RacUnavailableException unavailable =>
                        ApiResponse<object>.Fail(
                            HttpStatusCode.ServiceUnavailable,
                            new ApiError(
                                "rac_unavailable",
                                unavailable.Message)),

                    ArgumentException arg =>
                        ApiResponse<object>.Fail(
                            HttpStatusCode.BadRequest,
                            new ApiError(
                                "bad_request",
                                arg.Message)),

                    _ => ApiResponse<object>.Fail(
                        HttpStatusCode.InternalServerError)
                };

                var traceId = ApiTrace.GetTraceId(context);

                context.Response.Headers[ApiTrace.HeaderName] = traceId;

                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiExceptionHandling");

                if (response.StatusCode >=
                    HttpStatusCode.InternalServerError)
                    logger.LogError(
                        exception,
                        "Unhandled exception. TraceId: {TraceId}",
                        traceId);
                else
                    logger.LogWarning(
                        "Request failed with {ExceptionType}: {Message}. " +
                        "TraceId: {TraceId}",
                        exception.GetType().Name,
                        exception.Message,
                        traceId);

                context.Response.StatusCode =
                    (int)response.StatusCode;

                await context.Response.WriteAsJsonAsync(response);
            });
        });
    }
}