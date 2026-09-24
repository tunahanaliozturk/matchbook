using System.Security.Claims;
using System.Threading.RateLimiting;
using Matchbook.BuildingBlocks.Hosting;
using Matchbook.BuildingBlocks.Security;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("gateway");

// The gateway checks the token too, so an unauthenticated request never reaches a service. Each service still
// validates it again: the gateway is a convenience at the edge, not the only lock.
builder.Services.AddAuthorizationBuilder().AddPolicy("authenticated", static policy => policy.RequireAuthenticatedUser());

// Services answer a create with a Location relative to themselves (/requisitions/{id}). Behind the gateway that
// path lives under /api, so the header is rewritten on the way out rather than teaching five services where
// they are mounted.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(static transforms => transforms.AddResponseTransform(static transform =>
    {
        IHeaderDictionary headers = transform.HttpContext.Response.Headers;
        string? location = headers.Location;

        if (location is { Length: > 0 } && location.StartsWith('/') && !location.StartsWith("/api/", StringComparison.Ordinal))
        {
            headers.Location = "/api" + location;
        }

        return ValueTask.CompletedTask;
    }));

// Per caller, not per address: behind a corporate proxy a whole office shares one address, and one person's
// runaway script should not lock out their colleagues.
builder.Services.AddRateLimiter(static limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("per-caller", static context => RateLimitPartition.GetTokenBucketLimiter(
        context.User.FindFirstValue(Authentication.SubjectClaim) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        static _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 400,
            TokensPerPeriod = 200,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
        }));
});

string[] origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins(origins)
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST", "PUT", "DELETE")));

builder.WebHost.ConfigureKestrel(static kestrel => kestrel.Limits.MaxRequestBodySize = 1024 * 1024);

var app = builder.Build();

app.UseCors();
app.UseMatchbookDefaults();
app.UseRateLimiter();
app.MapReverseProxy();

app.Run();

public partial class Program;
