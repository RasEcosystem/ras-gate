using System.Net;
using System.Text.Json;
using RasGate.Contracts.Common;

namespace RasGate.UnitTests.Contracts;

public sealed class ApiResponseTests
{
    [Fact]
    public void Ok_CreatesSuccessfulResponseWithData()
    {
        var response = ApiResponse<string>.Ok("result");

        Assert.True(response.Success);
        Assert.Equal("result", response.Data);
        Assert.Null(response.Error);
        Assert.Null(response.Errors);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Fail_CreatesFailedResponseWithoutData()
    {
        var response = ApiResponse<string>.Fail(
            HttpStatusCode.BadRequest,
            new ApiError(
                "bad_request",
                "Bad request"));

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.NotNull(response.Error);
        Assert.Null(response.Errors);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public void Fail_Serialization_DoesNotContainData()
    {
        var response = ApiResponse<string>.Fail(
            HttpStatusCode.ServiceUnavailable,
            new ApiError(
                "rac_unavailable",
                "RAC executable could not be started."));

        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase
            });

        Assert.DoesNotContain(
            "\"data\"",
            json,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "\"success\":false",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Ok_Serialization_DoesNotContainErrorFields()
    {
        var response =
            ApiResponse<string>.Ok("result");

        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase
            });

        Assert.DoesNotContain(
            "\"error\"",
            json,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "\"errors\"",
            json,
            StringComparison.OrdinalIgnoreCase);
    }
}