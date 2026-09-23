using System.Security.Cryptography;
using Matchbook.BuildingBlocks.Security;
using Matchbook.SharedKernel;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Matchbook.Testing;

/// <summary>
/// Signs tokens the way Keycloak does, with the realm roles nested under <c>realm_access</c>, so a service under
/// test runs its real authentication and claims transformation against them. Only the signing key and the issuer
/// differ from production.
/// </summary>
public static class TestIdentity
{
    public const string Issuer = "https://identity.test/realms/matchbook";
    public const string Audience = "matchbook";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test" };
    private static readonly JsonWebTokenHandler Handler = new();

    public static SecurityKey SigningKey => Key;

    public static string TokenFor(Actor actor, string audience = Audience)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                [Authentication.SubjectClaim] = actor.Id.ToString(),
                [Authentication.NameClaim] = actor.Name,
                ["realm_access"] = new Dictionary<string, object> { ["roles"] = actor.Roles.ToArray() },
            },
        });
    }
}
