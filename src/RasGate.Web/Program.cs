using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using RasGate.Contracts.Common;
using RasGate.Infrastructure;
using RasGate.Web.Api;
using RasGate.Web.Api.Filters;
using RasGate.Web.Api.OpenApi;
using RasGate.Web.Authentication;
using RasGate.Web.Middlewares;
using Serilog;
using Serilog.Events;

namespace RasGate.Web;

public class Program
{
    public static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();
        });

        builder.Services
            .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
            .AddScheme<
                AuthenticationSchemeOptions,
                ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                _ => { });

        builder.Services.AddAuthorization();

        builder.Services.ConfigureApi();
        builder.Services.AddOpenApi();
        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition(
                ApiKeyAuthenticationDefaults.Scheme,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    Name = ApiKeyAuthenticationDefaults.HeaderName,
                    In = ParameterLocation.Header,
                    Description =
                        "API key required by protected endpoints."
                });

            options.OperationFilter<
                ApiKeySecurityOperationFilter>();
        });

        builder.Services.AddRasGateOptions(builder.Configuration);
        builder.Services.AddRac(builder.Configuration);

        var app = builder.Build();

        app.ConfigureLogging();
        app.ConfigurePipeline();

        app.Run();
    }
}

internal static class ApplicationConfigurationExtensions
{
    public static void ConfigureApi(
        this IServiceCollection services)
    {
        services.AddRouting(options =>
        {
            options.LowercaseUrls = true;
            options.LowercaseQueryStrings = true;
        });

        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add(
                new ConsumesAttribute(
                    "application/json"));

            options.Filters.Add(
                new ProducesAttribute(
                    "application/json"));
        });

        services
            .AddControllers(options =>
            {
                options.Filters.Add<
                    ApiResponseResultFilter>();
            })
            .AddJsonOptions(options =>
            {
                ApiJson.Configure(
                    options.JsonSerializerOptions);
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory =
                    context =>
                    {
                        var errors = context.ModelState
                            .Where(entry =>
                                entry.Value?.Errors.Count > 0)
                            .SelectMany(entry =>
                                entry.Value!.Errors.Select(error =>
                                    new ApiError(
                                        "validation_error",
                                        error.ErrorMessage,
                                        entry.Key)))
                            .ToList();

                        return new BadRequestObjectResult(
                            ApiResponse<object>.Fail(
                                HttpStatusCode.BadRequest,
                                errors));
                    };
            });
    }

    public static void ConfigureLogging(
        this WebApplication app)
    {
        var includeQueryString =
            app.Configuration.GetValue<bool?>(
                "RasGate:Logging:IncludeQueryString")
            ?? app.Environment.IsDevelopment();

        var includeRequestBody =
            app.Configuration.GetValue<bool?>(
                "RasGate:Logging:IncludeRequestBody")
            ?? false;

        var maxRequestBodyBytes =
            app.Configuration.GetValue<int?>(
                "RasGate:Logging:MaxRequestBodyBytes")
            ?? 4096;

        app.UseRequestBodyLogging(
            includeRequestBody,
            maxRequestBodyBytes);

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel =
                (http, _, exception) =>
                {
                    if (exception is not null ||
                        http.Response.StatusCode >=
                        StatusCodes.Status500InternalServerError)
                        return LogEventLevel.Error;

                    var isControllerRequest =
                        http.GetEndpoint()?
                                .Metadata
                                .GetMetadata<
                                    ControllerActionDescriptor>()
                            is not null;

                    return isControllerRequest
                        ? LogEventLevel.Information
                        : LogEventLevel.Verbose;
                };

            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded " +
                "{StatusCode} in {Elapsed:0.0000} ms";

            options.EnrichDiagnosticContext =
                (context, http) =>
                {
                    context.Set(
                        "TraceId",
                        ApiTrace.GetTraceId(http));

                    context.Set(
                        "Phase",
                        "HTTP");

                    if (includeQueryString &&
                        http.Request.QueryString.HasValue)
                        context.Set(
                            "QueryString",
                            http.Request.QueryString.Value);

                    if (http.Items.TryGetValue(
                            RequestBodyLoggingExtensions
                                .RequestBodyItemKey,
                            out var requestBody))
                        context.Set(
                            "RequestBody",
                            requestBody);

                    if (http.Items.TryGetValue(
                            RequestBodyLoggingExtensions
                                .RequestBodyTruncatedItemKey,
                            out var requestBodyTruncated))
                        context.Set(
                            "RequestBodyTruncated",
                            requestBodyTruncated);

                    if (http.Connection.RemoteIpAddress
                        is not null)
                        context.Set(
                            "RemoteIP",
                            http.Connection
                                .RemoteIpAddress
                                .ToString());
                };
        });
    }

    public static void ConfigurePipeline(
        this WebApplication app)
    {
        app.UseApiExceptionHandling();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
    }
}