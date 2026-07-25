using System.Text.Json;
using System.Text.Json.Serialization;

namespace RasGate.Web.Api;

public static class ApiJson
{
    public static readonly JsonSerializerOptions Default = Create();

    public static void Configure(
        JsonSerializerOptions options)
    {
        options.Converters.Add(
            new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options =
            new JsonSerializerOptions(
                JsonSerializerDefaults.Web);

        Configure(options);

        return options;
    }
}