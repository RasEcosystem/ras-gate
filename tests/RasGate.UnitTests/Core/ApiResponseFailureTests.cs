using System.Net;
using System.Text.Json;
using RasGate.Core.Common;

namespace RasGate.UnitTests.Core;

public sealed class ApiResponseFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "bad_request", "Bad request")]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized", "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", "Access denied")]
    [InlineData(HttpStatusCode.NotFound, "not_found", "Resource not found")]
    [InlineData(HttpStatusCode.Conflict, "conflict", "Conflict")]
    [InlineData(
        HttpStatusCode.InternalServerError,
        "internal_error",
        "Unexpected server error")]
    public void Fail_WithStatusCode_CreatesExpectedDefaultError(
        HttpStatusCode statusCode,
        string expectedCode,
        string expectedMessage)
    {
        var response =
            ApiResponse<object>.FailWithDefaultError(statusCode);

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.NotNull(response.Error);
        Assert.Equal(expectedCode, response.Error.Code);
        Assert.Equal(expectedMessage, response.Error.Message);
    }

    [Fact]
    public void Fail_WithMultipleErrors_CreatesGeneralAndDetailedErrors()
    {
        var errors = new[]
        {
            new ApiError(
                "validation_error",
                "Arguments are required.",
                "arguments"),
            new ApiError(
                "validation_error",
                "Another validation error.",
                "other")
        };

        var response = ApiResponse<object>.FailWithDefaultError(
            HttpStatusCode.BadRequest,
            errors);

        Assert.False(response.Success);
        Assert.Null(response.Data);

        Assert.NotNull(response.Error);
        Assert.Equal("bad_request", response.Error.Code);

        Assert.NotNull(response.Errors);
        Assert.Equal(2, response.Errors.Count);
    }

    [Fact]
    public void Fail_WithExplicitError_UsesProvidedError()
    {
        var error = new ApiError(
            "rac_unavailable",
            "RAC executable could not be started.");

        var response = ApiResponse<object>.Fail(error);

        Assert.Same(error, response.Error);
    }

    [Fact]
    public void Fail_WithMultipleErrors_JsonRoundTripPreservesWireContract()
    {
        var options = new JsonSerializerOptions(
            JsonSerializerDefaults.Web);

        var original = ApiResponse<object>.FailWithDefaultError(
            HttpStatusCode.BadRequest,
            [
                new ApiError(
                    "validation_error",
                    "Arguments are required.",
                    "arguments"),
                new ApiError(
                    "validation_error",
                    "Another validation error.",
                    "other")
            ]);

        var json = JsonSerializer.Serialize(original, options);
        var response = JsonSerializer.Deserialize<ApiResponse<object>>(
            json,
            options);

        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Equal("bad_request", response.Error?.Code);
        Assert.NotNull(response.Errors);
        Assert.Collection(
            response.Errors,
            error =>
            {
                Assert.Equal("validation_error", error.Code);
                Assert.Equal("arguments", error.Target);
            },
            error =>
            {
                Assert.Equal("validation_error", error.Code);
                Assert.Equal("other", error.Target);
            });
    }
}