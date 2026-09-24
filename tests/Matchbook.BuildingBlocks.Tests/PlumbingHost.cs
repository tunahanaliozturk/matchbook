using MassTransit;
using Matchbook.BuildingBlocks.Hosting;
using Matchbook.BuildingBlocks.Http;
using Matchbook.BuildingBlocks.Messaging;
using Matchbook.BuildingBlocks.Persistence;
using Matchbook.BuildingBlocks.Security;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.BuildingBlocks.Tests;

public sealed record Ping(Guid Id);

public sealed record Pong(Guid Id);

public sealed record Noted(Guid Id);

public sealed class Note
{
    public Guid Id { get; set; }

    public string Text { get; set; } = "";
}

public sealed class Handled
{
    public Guid Id { get; set; }

    public Guid PingId { get; set; }
}

public interface IPlumbingDb
{
    DbSet<Note> Notes { get; }

    DbSet<Handled> Handled { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The smallest service the building blocks make: a table, an outbox, an inbox and one consumer. There are no
/// migrations for it, so the schema is created directly and the pending-model warning, which exists to catch a
/// forgotten migration, is switched off for this context alone.
/// </summary>
public sealed class PlumbingDbContext(DbContextOptions<PlumbingDbContext> options) : DbContext(options), IPlumbingDb
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Handled> Handled => Set<Handled>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.ConfigureWarnings(static warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddTransactionalOutboxEntities();
        modelBuilder.Entity<Note>().HasIndex(static note => note.Text).IsUnique().HasDatabaseName("ux_notes_text");
        modelBuilder.Entity<Handled>();
    }
}

/// <summary>Records every ping it handles and answers it, both inside the consumer's transaction.</summary>
public sealed class PingConsumer(IPlumbingDb db, IEventPublisher events) : IConsumer<Ping>
{
    public async Task Consume(ConsumeContext<Ping> context)
    {
        db.Handled.Add(new Handled { Id = Guid.CreateVersion7(), PingId = context.Message.Id });
        await events.PublishAsync(new Pong(context.Message.Id), context.CancellationToken);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

internal static class PlumbingHost
{
    public static async Task<(WebApplication App, Uri Broker)> StartAsync(Matchbook.Testing.Infrastructure infrastructure)
    {
        string database = await infrastructure.CreateDatabaseAsync("plumbing");
        Uri broker = await infrastructure.CreateVirtualHostAsync("plumbing");

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = database,
            ["ConnectionStrings:RabbitMq"] = broker.ToString(),
            ["Auth:Audience"] = TestIdentity.Audience,
            ["Logging:LogLevel:Default"] = "Warning",
        });

        builder.AddMatchbookDefaults("plumbing");
        builder.Services.AddMatchbookDatabase<PlumbingDbContext, IPlumbingDb>(builder.Configuration);
        builder.Services.AddMatchbookMessaging<PlumbingDbContext>(builder.Configuration, "plumbing", static bus => bus.AddConsumer<PingConsumer>());
        builder.Services.AddAuthorizationBuilder().AddRolePolicy("budgets", Roles.BudgetAdmin);
        builder.Services.MapUniqueViolation("ux_notes_text", "note.duplicate", "That label exists.");
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, static options =>
        {
            options.TokenValidationParameters.ValidIssuer = TestIdentity.Issuer;
            options.TokenValidationParameters.IssuerSigningKey = TestIdentity.SigningKey;
        });

        WebApplication app = builder.Build();
        app.UseMatchbookDefaults();

        app.MapPost("/notes", static async (bool fail, IPlumbingDb db, IEventPublisher events, CancellationToken cancellationToken) =>
        {
            var note = new Note { Id = Guid.CreateVersion7(), Text = Guid.NewGuid().ToString() };
            db.Notes.Add(note);
            await events.PublishAsync(new Noted(note.Id), cancellationToken);

            if (fail)
            {
                throw new InvalidOperationException($"Failed before commit: {note.Id}");
            }

            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(note.Id);
        });

        app.MapPost("/labels/{text}", static async (string text, IPlumbingDb db, CancellationToken cancellationToken) =>
        {
            db.Notes.Add(new Note { Id = Guid.CreateVersion7(), Text = text });
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok();
        });

        app.MapGet("/me", static (HttpContext context) => TypedResults.Ok(context.User.ToActor()));
        app.MapGet("/budgets", static () => TypedResults.Ok()).RequireAuthorization("budgets");
        app.MapGet("/refuse/{kind}", static (ViolationKind kind) =>
        {
            throw new BusinessRuleException($"test.{kind.ToString().ToLowerInvariant()}", "Refused on purpose.", kind);
        });

        // No migrations exist for the test context, so create its schema before the host migrates (a no-op).
        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlumbingDbContext>().Database.EnsureCreatedAsync();
        }

        await app.StartAsync();
        return (app, broker);
    }
}
