using System.Net;
using System.Text.Json.Serialization;

namespace RasGate.Core.Common;

public sealed class ApiResponse<T> : IApiResponse
{
    [JsonConstructor]
    public ApiResponse(
        bool success,
        T? data = default,
        ApiError? error = null,
        IReadOnlyCollection<ApiError>? errors = null)
    {
        Success = success;
        Data = data;
        Error = error;
        Errors = errors?.ToArray();
    }

    [JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(1)]
    public T? Data { get; }

    [JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(2)]
    public ApiError? Error { get; }

    [JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(3)]
    public IReadOnlyCollection<ApiError>? Errors { get; }

    [JsonPropertyOrder(0)] public bool Success { get; }

    public static ApiResponse<T> Ok(T? data = default)
    {
        return new ApiResponse<T>(
            true,
            data);
    }

    public static ApiResponse<T> FailWithDefaultError(
        HttpStatusCode statusCode)
    {
        return Fail(GetDefaultError(statusCode));
    }

    public static ApiResponse<T> Fail(
        string code,
        string message)
    {
        return Fail(new ApiError(code, message));
    }

    public static ApiResponse<T> Fail(
        ApiError error)
    {
        return new ApiResponse<T>(
            false,
            default,
            error);
    }

    public static ApiResponse<T> FailWithDefaultError(
        HttpStatusCode statusCode,
        IEnumerable<ApiError> errors)
    {
        return Fail(GetDefaultError(statusCode), errors);
    }

    public static ApiResponse<T> Fail(
        ApiError error,
        IEnumerable<ApiError> errors)
    {
        return new ApiResponse<T>(
            false,
            default,
            error,
            errors.ToArray());
    }

    private static ApiError GetDefaultError(HttpStatusCode status)
    {
        return status switch
        {
            HttpStatusCode.BadRequest =>
                new ApiError(
                    "bad_request",
                    "Bad request"),

            HttpStatusCode.Unauthorized =>
                new ApiError(
                    "unauthorized",
                    "Unauthorized"),

            HttpStatusCode.Forbidden =>
                new ApiError(
                    "forbidden",
                    "Access denied"),

            HttpStatusCode.NotFound =>
                new ApiError(
                    "not_found",
                    "Resource not found"),

            HttpStatusCode.MethodNotAllowed =>
                new ApiError(
                    "method_not_allowed",
                    "Method not allowed"),

            HttpStatusCode.Conflict =>
                new ApiError(
                    "conflict",
                    "Conflict"),

            HttpStatusCode.RequestEntityTooLarge =>
                new ApiError(
                    "request_too_large",
                    "Request is too large"),

            HttpStatusCode.UnsupportedMediaType =>
                new ApiError(
                    "unsupported_media_type",
                    "Unsupported media type"),

            HttpStatusCode.TooManyRequests =>
                new ApiError(
                    "too_many_requests",
                    "Too many requests"),

            HttpStatusCode.BadGateway =>
                new ApiError(
                    "bad_gateway",
                    "Bad gateway"),

            HttpStatusCode.ServiceUnavailable =>
                new ApiError(
                    "service_unavailable",
                    "Service unavailable"),

            HttpStatusCode.InternalServerError =>
                new ApiError(
                    "internal_error",
                    "Unexpected server error"),

            _ =>
                new ApiError(
                    "request_failed",
                    "Unexpected server error")
        };
    }
}