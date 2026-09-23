using System.Security.Claims;
using System.Text.Json;
using Matchbook.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.BuildingBlocks.Security;

public static class Authentication
{
    public const string RoleClaim = "role";
    public const string NameClaim = "preferred_username";
    public const string SubjectClaim = "sub";

    /// <summary>
    /// Bearer tokens from the identity provider at <c>Auth:Authority</c>, for the audience <c>Auth:Audience</c>.
    /// Every endpoint requires an authenticated caller unless it opts out, so forgetting an attribute fails closed.
    /// </summary>
    public static IServiceCollection AddMatchbookAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Auth:Authority"];
                options.Audience = configuration["Auth:Audience"];
                options.RequireHttpsMetadata = configuration.GetValue("Auth:RequireHttpsMetadata", true);

                // Keep the token's own claim names. The default mapping renames "sub" to a long URI and
                // drops nothing useful in exchange.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = NameClaim;
                options.TokenValidationParameters.RoleClaimType = RoleClaim;

                // Behind the gateway and in compose, the issuer the service sees in the token (the public
                // Keycloak URL) differs from the address it fetches keys from (the container name).
                string? issuer = configuration["Auth:ValidIssuer"];

                if (!string.IsNullOrEmpty(issuer))
                {
                    options.TokenValidationParameters.ValidIssuer = issuer;
                }
            });

        services.AddTransient<IClaimsTransformation, RealmRolesTransformation>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>A policy satisfied by any one of <paramref name="roles"/>.</summary>
    public static AuthorizationBuilder AddRolePolicy(this AuthorizationBuilder builder, string name, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddPolicy(name, policy => policy.RequireRole(roles));
    }

    /// <summary>
    /// The caller as the domain sees them. Throws when the token has no usable subject, which the fallback
    /// policy makes impossible on any endpoint that did not opt out of authentication.
    /// </summary>
    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string subject = principal.FindFirstValue(SubjectClaim)
            ?? throw new InvalidOperationException("The token carries no subject.");

        if (!Guid.TryParse(subject, out Guid id))
        {
            throw new InvalidOperationException("The token's subject is not a user id.");
        }

        HashSet<string> roles = new(
            principal.FindAll(RoleClaim).Select(static claim => claim.Value),
            StringComparer.Ordinal);

        return new Actor(id, principal.FindFirstValue(NameClaim) ?? subject, roles);
    }

    /// <summary>
    /// Keycloak puts realm roles in a nested JSON claim, <c>realm_access.roles</c>. This lifts them into flat
    /// role claims so <c>RequireRole</c> and <see cref="ToActor"/> see them.
    /// </summary>
    private sealed class RealmRolesTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity
                || identity.HasClaim(static claim => claim.Type == RoleClaim))
            {
                return Task.FromResult(principal);
            }

            string? realmAccess = identity.FindFirst("realm_access")?.Value;

            if (realmAccess is null)
            {
                return Task.FromResult(principal);
            }

            using JsonDocument document = JsonDocument.Parse(realmAccess);

            if (document.RootElement.TryGetProperty("roles", out JsonElement roles) && roles.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement role in roles.EnumerateArray())
                {
                    if (role.GetString() is { } value)
                    {
                        identity.AddClaim(new Claim(RoleClaim, value));
                    }
                }
            }

            return Task.FromResult(principal);
        }
    }
}
