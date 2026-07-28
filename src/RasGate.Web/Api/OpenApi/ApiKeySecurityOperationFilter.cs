using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using RasGate.Web.Authentication;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace RasGate.Web.Api.OpenApi;

public sealed class ApiKeySecurityOperationFilter : IOperationFilter
{
    public void Apply(
        OpenApiOperation operation,
        OperationFilterContext context)
    {
        var metadata =
            context.ApiDescription
                .ActionDescriptor
                .EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any() ||
            !metadata.OfType<IAuthorizeData>().Any())
            return;

        var scheme =
            new OpenApiSecuritySchemeReference(
                ApiKeyAuthenticationDefaults.Scheme,
                context.Document);

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [scheme] = []
            }
        ];
    }
}