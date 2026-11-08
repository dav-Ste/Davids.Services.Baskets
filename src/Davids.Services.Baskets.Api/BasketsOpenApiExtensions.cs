using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Davids.Services.Baskets.Api;

public static class BasketsOpenApiExtensions
{
    public static IServiceCollection AddBasketsOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Davids Baskets API";
                document.Info.Version = "1.0.0";
                document.Info.Description = "Customer shopping basket endpoints isolated by verified CustomerBearer OIDC identity.";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "RS256 CustomerBearer OIDC access token (aud: davids-services)."
                };

                if (document.Components.Schemas is not null &&
                    document.Components.Schemas.TryGetValue("UpsertBasketItem", out var upsertRef) &&
                    upsertRef is OpenApiSchema upsertSchema &&
                    upsertSchema.Properties is not null)
                {
                    upsertSchema.Required = new HashSet<string>(StringComparer.Ordinal) { "productId", "quantity" };
                    if (upsertSchema.Properties.TryGetValue("quantity", out var qtyProp) && qtyProp is OpenApiSchema qtySchema)
                    {
                        qtySchema.Type = JsonSchemaType.Integer;
                        qtySchema.Minimum = "0";
                        qtySchema.Maximum = "99";
                    }
                }

                foreach (var (_, pathItem) in document.Paths ?? [])
                {
                    foreach (var (_, operation) in pathItem.Operations ?? [])
                    {
                        if (operation.RequestBody is OpenApiRequestBody reqBody)
                        {
                            reqBody.Required = true;
                        }

                        if (operation.Security is { Count: > 0 })
                        {
                            operation.Security =
                            [
                                new OpenApiSecurityRequirement
                                {
                                    [new OpenApiSecuritySchemeReference("BearerAuth", document)] = []
                                }
                            ];
                        }
                    }
                }

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();

                operation.Responses ??= new OpenApiResponses();
                operation.Responses.TryAdd("429", new OpenApiResponse { Description = "Rate limit exceeded." });

                if (requiresAuth)
                {
                    operation.Security = [new OpenApiSecurityRequirement()];
                    operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid RS256 CustomerBearer access token." });
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Caller lacks customer permission for this basket resource." });
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }
}
