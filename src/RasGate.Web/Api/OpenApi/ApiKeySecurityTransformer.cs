using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using RasGate.Web.Authentication;

namespace RasGate.Web.Api.OpenApi;

public sealed class ApiKeySecurityTransformer :
    IOpenApiDocumentTransformer,
    IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[
                ApiKeyAuthenticationDefaults.Scheme] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = ApiKeyAuthenticationDefaults.HeaderName,
                In = ParameterLocation.Header,
                Description =
                    "API key required by protected endpoints."
            };

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata =
            context.Description
                .ActionDescriptor
                .EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any() ||
            !metadata.OfType<IAuthorizeData>().Any())
            return Task.CompletedTask;

        var scheme =
            new OpenApiSecuritySchemeReference(
                ApiKeyAuthenticationDefaults.Scheme,
                context.Document);

        operation.Security ??= [];
        operation.Security.Add(
            new OpenApiSecurityRequirement
            {
                [scheme] = []
            });

        return Task.CompletedTask;
    }
}