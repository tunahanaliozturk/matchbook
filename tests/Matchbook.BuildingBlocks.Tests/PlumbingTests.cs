using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[assembly: AssemblyFixture(typeof(Matchbook.Testing.Infrastructure))]

namespace Matchbook.BuildingBlocks.Tests;

/// <summary>
/// One host for the whole class. xUnit makes a new instance of the test class for every test, so anything
/// expensive lives in a class fixture, which can itself take the assembly's containers.
/// </summary>
public sealed class PlumbingFixture(Matchbook.Testing.Infrastructure infrastructure) : IAsyncLifetime
{
    public WebApplication App { get; private set; } = null!;

    public EventProbe Probe { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        (App, Uri broker) = await PlumbingHost.StartAsync(infrastructure);
        Probe = await EventProbe.StartAsync(broker, static listen => listen.For<Noted>().For<Pong>());
    }

    public async ValueTask DisposeAsync()
    {
        await Probe.DisposeAsync();
        await App.DisposeAsync();
    }
}

public sealed class PlumbingTests(PlumbingFixture fixture) : IClassFixture<PlumbingFixture>
{
    private readonly WebApplication _app = fixture.App;
    private readonly EventProbe _probe = fixture.Probe;

    private HttpClient Client(Actor? actor = null)
    {
        HttpClient client = _app.GetTestClient();

        if (actor is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestIdentity.TokenFor(actor));
        }

        return client;
    }

    [Fact]
    public async Task A_request_without_a_token_is_refused() =>
        (await Client().GetAsync(new Uri("/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task A_token_for_another_audience_is_refused()
    {
        HttpClient client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestIdentity.TokenFor(TestUsers.Bob, "someone-else"));

        (await client.GetAsync(new Uri("/me", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Realm_roles_in_a_keycloak_shaped_token_become_the_actors_roles()
    {
        Me me = (await Client(TestUsers.Bob).GetFromJsonAsync<Me>(new Uri("/me", UriKind.Relative)))!;

        me.Id.ShouldBe(TestUsers.Bob.Id);
        me.Name.ShouldBe("bob");
        me.Roles.ShouldBe([Roles.BudgetAdmin]);
    }

    [Fact]
    public async Task A_role_policy_admits_the_role_and_nobody_else()
    {
        (await Client(TestUsers.Bob).GetAsync(new Uri("/budgets", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client(TestUsers.Rita).GetAsync(new Uri("/budgets", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(ViolationKind.Invalid, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ViolationKind.Conflict, HttpStatusCode.Conflict)]
    [InlineData(ViolationKind.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ViolationKind.Forbidden, HttpStatusCode.Forbidden)]
    public async Task A_broken_rule_becomes_a_problem_with_its_code(ViolationKind kind, HttpStatusCode status)
    {
        HttpResponseMessage response = await Client(TestUsers.Rita).GetAsync(new Uri($"/refuse/{kind}", UriKind.Relative));

        await response.ShouldBeProblemAsync(status, $"test.{kind.ToString().ToLowerInvariant()}");
    }

    [Fact]
    public async Task Losing_a_race_on_a_unique_index_is_a_conflict_with_the_mapped_code()
    {
        HttpClient client = Client(TestUsers.Rita);
        var label = new Uri($"/labels/{Guid.NewGuid()}", UriKind.Relative);

        (await client.PostAsync(label, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await (await client.PostAsync(label, null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "note.duplicate");
    }

    [Fact]
    public async Task An_unexpected_failure_says_nothing_about_itself()
    {
        HttpResponseMessage response = await Client(TestUsers.Rita).PostAsync(new Uri("/notes?fail=true", UriKind.Relative), null);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("Failed before commit");
    }

    [Fact]
    public async Task An_event_leaves_when_its_change_commits_and_never_when_it_does_not()
    {
        HttpClient client = Client(TestUsers.Rita);

        (await client.PostAsync(new Uri("/notes?fail=true", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        HttpResponseMessage committed = await client.PostAsync(new Uri("/notes?fail=false", UriKind.Relative), null);
        Guid noteId = await committed.Content.ReadFromJsonAsync<Guid>();

        await _probe.WaitForAsync<Noted>(noted => noted.Id == noteId);

        // The failed request's event was published into the same outbox before the exception. Had it escaped,
        // it would have arrived alongside the committed one, so a short further wait is enough to show it did not.
        await Task.Delay(TimeSpan.FromSeconds(2));
        // No other test in the class commits a note, so exactly one may have been published.
        _probe.Received<Noted>().ShouldHaveSingleItem().Id.ShouldBe(noteId);
    }

    [Fact]
    public async Task A_redelivered_message_is_handled_once()
    {
        var ping = new Ping(Guid.CreateVersion7());
        Guid messageId = Guid.NewGuid();

        await _probe.PublishAsync(ping, messageId);
        await _probe.PublishAsync(ping, messageId);
        await _probe.WaitForAsync<Pong>(pong => pong.Id == ping.Id);
        await Task.Delay(TimeSpan.FromSeconds(2));

        _probe.Received<Pong>().Count(pong => pong.Id == ping.Id).ShouldBe(1);

        await using AsyncServiceScope scope = _app.Services.CreateAsyncScope();
        PlumbingDbContext db = scope.ServiceProvider.GetRequiredService<PlumbingDbContext>();
        (await db.Handled.CountAsync(handled => handled.PingId == ping.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Ready_means_the_database_and_the_broker_answer()
    {
        HttpClient anonymous = Client();

        (await anonymous.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Eventually.MatchesAsync(
            async () => (await anonymous.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode,
            static status => status == HttpStatusCode.OK);
    }
}

internal sealed record Me(Guid Id, string Name, string[] Roles);
