using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Dok.Api.OpenApi;

public static class SchemaTransformers
{
    public static Task TransformDomainTypes(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken ct)
    {
        if (context.JsonTypeInfo.Type == typeof(Plate))
        {
            schema.Type = JsonSchemaType.String;
            schema.Pattern = @"^[A-Z]{3}\d[A-Z]\d{2}$|^[A-Z]{3}\d{4}$";
        }
        else if (context.JsonTypeInfo.Type == typeof(Money))
        {
            schema.Type = JsonSchemaType.String;
            schema.Pattern = @"^\d+\.\d{2}$";
        }
        return Task.CompletedTask;
    }
}
