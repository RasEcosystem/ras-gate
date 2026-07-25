using System.Net;
using System.Text.Json.Serialization;

namespace RasGate.Contracts.Common;

public sealed class ApiResponse<T> : IApiResponse
{
    private ApiResponse()
    {
    }

    [JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(1)]
    public T? Data { get; private init; }

    [JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(2)]
    public ApiError? Error { get; private init; }

    [JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyOrder(3)]
    public IReadOnlyCollection<ApiError>? Errors { get; private init; }

    [JsonPropertyOrder(0)] public bool Success { get; private init; }

    [JsonIgnore]
    public HttpStatusCode StatusCode { get; private init; }
        = HttpStatusCode.OK;

    public static ApiResponse<T>
        Ok(
            T? data = default)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            StatusCode =
                HttpStatusCode.OK
        };
    }

    public static ApiResponse<T> Fail(HttpStatusCode statusCode)
    {
        return Fail(statusCode, GetDefaultError(statusCode));
    }

    public static ApiResponse<T> Fail(
        HttpStatusCode statusCode,
        string code,
        string message)
    {
        return Fail(statusCode, new ApiError(code, message));
    }

    public static ApiResponse<T> Fail(
        HttpStatusCode statusCode,
        ApiError error)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = error,
            StatusCode = statusCode
        };
    }

    public static ApiResponse<T> Fail(
        HttpStatusCode statusCode,
        IEnumerable<ApiError> errors)
    {
        return Fail(statusCode, GetDefaultError(statusCode), errors);
    }

    public static ApiResponse<T> Fail(
        HttpStatusCode statusCode,
        ApiError error,
        IEnumerable<ApiError> errors)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Error = error,
            Errors = errors.ToList(),
            StatusCode = statusCode
        };
    }

    private static ApiError
        GetDefaultError(
            HttpStatusCode status)
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

            HttpStatusCode.Conflict =>
                new ApiError(
                    "conflict",
                    "Conflict"),

            HttpStatusCode.InternalServerError =>
                new ApiError("internal_error",
                    "Unexpected server error"),

            _ =>
                new ApiError(
                    "request_failed",
                    "Unexpected server error")
        };
    }
}