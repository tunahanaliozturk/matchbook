using System.Diagnostics;
using Matchbook.Stack;

[assembly: AssemblyFixture(typeof(Matchbook.SystemTests.StackFixture))]

namespace Matchbook.SystemTests;

/// <summary>
/// The running compose stack, shared by every test. Fails at once, with the command to fix it, when the stack is
/// not up, instead of letting each test time out against a closed port.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public StackFixture()
    {
        Gateway = new Gateway(Options, http);
        Quiescence = new Quiescence(Options, http);
    }

    public StackOptions Options { get; } = new();

    internal Gateway Gateway { get; }

    public Quiescence Quiescence { get; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(Options.Gateway, "/health/live"));
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"The stack is not answering at {Options.Gateway}. Start it first: docker compose up -d --build --wait", exception);
        }
    }

    /// <summary>Waits for quiet, reads the five databases and returns every broken invariant.</summary>
    public async Task<IReadOnlyList<Discrepancy>> ReconcileAsync(TimeSpan patience)
    {
        await Quiescence.WaitAsync(patience, CancellationToken.None);
        Ledgers ledgers = await Ledgers.ReadAsync(Options, CancellationToken.None);
        return Reconciliation.Check(ledgers);
    }

    public ValueTask DisposeAsync()
    {
        Gateway.Dispose();
        http.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Harm done to the stack's containers from outside, as an operator or the kernel would do it.</summary>
internal static class Containers
{
    public static Task RestartAsync(string service) => DockerAsync("restart", Name(service));

    /// <summary>SIGKILL: no graceful shutdown, no flush, whatever was in flight is simply gone.</summary>
    public static Task KillAsync(string service) => DockerAsync("kill", "--signal", "SIGKILL", Name(service));

    public static Task StartAsync(string service) => DockerAsync("start", Name(service));

    // The name compose gives the first replica of a service in the project "matchbook".
    private static string Name(string service) => $"matchbook-{service}-1";

    private static async Task DockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process docker = Process.Start(start)!;
        Task<string> output = docker.StandardOutput.ReadToEndAsync();
        string error = await docker.StandardError.ReadToEndAsync();
        await output;
        await docker.WaitForExitAsync();

        if (docker.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker {string.Join(' ', arguments)} exited {docker.ExitCode}: {error}");
        }
    }
}
