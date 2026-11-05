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
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
