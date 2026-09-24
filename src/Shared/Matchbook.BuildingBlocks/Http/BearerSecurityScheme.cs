using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Matchbook.BuildingBlocks.Http;

/// <summary>
/// Declares in every service's OpenAPI document that its operations take a bearer token from the identity
/// provider, so generated clients and API explorers send one without being told.
/// </summary>
internal sealed class BearerSecurityScheme : IOpenApiDocumentTransformer
{
    private const string Name = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Name] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "An access token from the Matchbook realm in Keycloak.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(Name, document)] = [] });
        return Task.CompletedTask;
    }
}
