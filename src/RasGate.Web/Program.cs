using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using RasGate.Core.Common;
using RasGate.Infrastructure;
using RasGate.Web.Api;
using RasGate.Web.Api.Filters;
using RasGate.Web.Api.OpenApi;
using RasGate.Web.Authentication;
using RasGate.Web.Middlewares;
using RasGate.Web.Observability;
using Serilog;
using Serilog.Events;

namespace RasGate.Web;

public class Program
{
    public static int Main(string[] args)
    {
        var validateConfiguration =
            ConfigurationValidator.IsRequested(args);

        try
        {
            using var app = BuildApplication(
                ConfigurationValidator.RemoveSwitch(args));

            if (validateConfiguration)
                return ConfigurationValidator.Validate(
                    app.Services,
                    Console.Out,
                    Console.Error);

            RunApplication(app);

            return 0;
        }
        catch (Exception exception) when (validateConfiguration)
        {
            return ConfigurationValidator.ReportStartupFailure(
                exception,
                Console.Error);
        }
    }

    private static WebApplication BuildApplication(string[] args)
    {
        var builder = CreateWebApplicationBuilder(args);

        builder.Host.UseWindowsService(options =>
        {
            options.ServiceName = "RasGate";
        });

        builder.Host.UseSystemd();

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
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<
                ApiKeySecurityTransformer>();

            options.AddOperationTransformer<
                ApiKeySecurityTransformer>();
        });

        builder.Services.AddRasGateOptions(builder.Configuration);
        builder.Services.AddRac(builder.Configuration);

        var app = builder.Build();

        app.ConfigureLogging();
        app.ConfigurePipeline();

        return app;
    }

    internal static WebApplicationBuilder CreateWebApplicationBuilder(
        string[] args)
    {
        return WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });
    }

    private static void RunApplication(WebApplication app)
    {
        var logger = app.Services
            .GetRequiredService<ILogger<Program>>();

        try
        {
            app.ConfigureApplicationLifecycleLogging(logger);
            app.Run();
        }
        catch (Exception exception)
        {
            logger.LogUnexpectedTermination(exception);
            throw;
        }
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
                            ApiResponse<object>.FailWithDefaultError(
                                HttpStatusCode.BadRequest,
                                errors));
                    };
            });
    }

    public static void ConfigureLogging(
        this WebApplication app)
    {
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
        app.UseApiTraceHeader();
        app.UseApiExceptionHandling();
        app.UseApiStatusCodeResponses();

        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
    }
}
