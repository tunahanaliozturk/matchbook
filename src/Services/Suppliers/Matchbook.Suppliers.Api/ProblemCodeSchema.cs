using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Matchbook.Suppliers.Api;

/// <summary>
/// Adds the <c>code</c> extension to the problem details schema. It is the one field a client is meant to branch
/// on, so a client generated from the document should have it typed rather than buried in extension data.
/// </summary>
internal static class ProblemCodeSchema
{
    public static OpenApiOptions DescribeProblemCodes(this OpenApiOptions options) =>
        options.AddSchemaTransformer(static (schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type.IsAssignableTo(typeof(ProblemDetails)))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                schema.Properties["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Stable and machine-readable, such as supplier.self_approval. Absent on 400.",
                };
            }

            return Task.CompletedTask;
        });
}
