using System.Text.Json;
using RasGate.Core.Common;

namespace RasGate.UnitTests.Core;

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
    }

    [Fact]
    public void Fail_CreatesFailedResponseWithoutData()
    {
        var response = ApiResponse<string>.Fail(
            new ApiError(
                "bad_request",
                "Bad request"));

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.NotNull(response.Error);
        Assert.Null(response.Errors);
    }

    [Fact]
    public void Fail_Serialization_DoesNotContainData()
    {
        var response = ApiResponse<string>.Fail(
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

    [Fact]
    public void Ok_JsonRoundTrip_PreservesWireContract()
    {
        var options = new JsonSerializerOptions(
            JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(
            ApiResponse<string>.Ok("result"),
            options);

        var response = JsonSerializer.Deserialize<ApiResponse<string>>(
            json,
            options);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("result", response.Data);
        Assert.Null(response.Error);
        Assert.Null(response.Errors);
        Assert.DoesNotContain(
            "statusCode",
            json,
            StringComparison.OrdinalIgnoreCase);
    }
}