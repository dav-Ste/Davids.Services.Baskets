using System.Security.Claims;
using Davids.ServiceDefaults;
using Davids.Services.Baskets.Api;
using Davids.Services.Baskets.Api.Exceptions;
using Davids.Services.Baskets.Application.Commands;
using Davids.Services.Baskets.Application.Services;
using Davids.Services.Baskets.Infrastructure;
using Davids.Services.Baskets.Infrastructure.Auth;
using Davids.Services.Baskets.Infrastructure.Persistence;
using Davids.Services.Baskets.Infrastructure.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.LoadVaultAsync();
builder.AddServiceDefaults("davids-baskets");
builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);
builder.Services.AddBasketsInfrastructure(builder.Configuration);

var customerOidc = builder.Configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
    ?? new OidcAuthoritySettings { Audience = "davids-services" };
customerOidc.Audience ??= "davids-services";
var customerStaticKey = customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");

var requireHttpsMetadata =
    builder.Environment.IsProduction() ||
    !(builder.Environment.IsDevelopment() ||
      builder.Environment.IsEnvironment("Testing") ||
      builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment"));

builder.Services.AddAuthentication(BasketsInfrastructureExtensions.CustomerScheme)
    .AddJwtBearer(BasketsInfrastructureExtensions.CustomerScheme, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(customerOidc.MetadataAddress))
        {
            options.MetadataAddress = customerOidc.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(customerOidc.EffectiveIssuer),
            ValidIssuer = customerOidc.EffectiveIssuer,
            ValidateAudience = true,
            ValidAudience = customerOidc.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = customerStaticKey,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var role = context.Principal?.FindFirstValue("role");
                if (!string.Equals(role, DavidsRoles.Customer, StringComparison.Ordinal))
                {
                    context.Fail("CustomerBearer accepts customer ('user') tokens only.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CustomerOnly", policy =>
    {
        policy.AddAuthenticationSchemes(BasketsInfrastructureExtensions.CustomerScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", DavidsRoles.Customer);
    });

builder.Services.AddExceptionHandler<BasketApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddBasketsOpenApi();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Automatic migration is development-only.");
    }

    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<BasketsDbContext>().Database.MigrateAsync();
}

app.UseTrustedForwardedHeaders();
app.UseExceptionHandler();
if (!(app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment")))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "docs";
        options.SwaggerEndpoint("../openapi/v1.json", "Davids Baskets v1");
    });
}

static bool TryGetCustomerId(ClaimsPrincipal user, out Guid customerId)
{
    var raw = user.FindFirstValue("sub");
    return Guid.TryParse(raw, out customerId) && customerId != Guid.Empty;
}

app.MapGet("/", () => Results.Text("Davids Baskets Service"))
    .ExcludeFromDescription()
    .AllowAnonymous();

app.MapGet("/baskets/me", async (ClaimsPrincipal user, IBasketsService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.GetBasketAsync(customerId, ct));
    })
    .RequireAuthorization("CustomerOnly");

app.MapPut("/baskets/me/items", async (UpsertBasketItem command, ClaimsPrincipal user, IBasketsService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.UpsertItemAsync(customerId, command, ct));
    })
    .RequireAuthorization("CustomerOnly");

app.MapDelete("/baskets/me/items/{productId:guid}", async (Guid productId, ClaimsPrincipal user, IBasketsService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.RemoveItemAsync(customerId, productId, ct));
    })
    .RequireAuthorization("CustomerOnly");

app.MapDelete("/baskets/me", async (ClaimsPrincipal user, IBasketsService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        await service.ClearBasketAsync(customerId, ct);
        return Results.NoContent();
    })
    .RequireAuthorization("CustomerOnly");

app.Run();

public partial class Program;
