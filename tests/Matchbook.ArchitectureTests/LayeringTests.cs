using System.Reflection;

namespace Matchbook.ArchitectureTests;

/// <summary>
/// The layering of every service, and the walls between services, checked against what each compiled assembly
/// actually references rather than against project files. A project reference nothing uses does not show up
/// here, and a type used through a transitive reference does, which is the dependency that matters.
/// </summary>
public sealed class LayeringTests
{
    private static readonly string[] ServiceNames = ["Suppliers", "Budgets", "Requisitions", "Purchasing", "Payables"];

    public static TheoryData<string> Services => new(ServiceNames);

    // Concrete technologies the inner layers may not name. Application is allowed EF Core, which is provider
    // neutral: it queries through the service's DbContext interface (ADR 0003), while the Postgres provider,
    // the migrations and the messaging library stay in Infrastructure.
    private static readonly string[] Technologies =
    [
        "Npgsql",
        "MassTransit",
        "RabbitMQ",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Options",
        "OpenTelemetry",
    ];

    private static Assembly Layer(string service, string layer) => Assembly.Load($"Matchbook.{service}.{layer}");

    private static string[] References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(static name => name.Name!)];

    private static string[] Projects(Assembly assembly) =>
        [.. References(assembly).Where(static name => name.StartsWith("Matchbook.", StringComparison.Ordinal))];

    private static bool IsBaseLibrary(string name) =>
        name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib";

    [Theory]
    [MemberData(nameof(Services))]
    public void The_domain_knows_only_the_shared_kernel(string service)
    {
        Assembly domain = Layer(service, "Domain");

        References(domain)
            .Where(static name => !IsBaseLibrary(name))
            .ShouldAllBe(name => name == "Matchbook.SharedKernel");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void The_application_knows_its_domain_the_contracts_and_ef_core(string service)
    {
        Assembly application = Layer(service, "Application");
        string[] allowedProjects = [$"Matchbook.{service}.Domain", "Matchbook.SharedKernel", "Matchbook.Contracts"];
        string[] allowedPackages =
        [
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
        ];

        Projects(application).ShouldAllBe(name => allowedProjects.Contains(name));
        References(application)
            .Where(static name => !IsBaseLibrary(name) && !name.StartsWith("Matchbook.", StringComparison.Ordinal))
            .ShouldAllBe(name => allowedPackages.Contains(name));
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Neither_inner_layer_names_a_concrete_technology(string service)
    {
        foreach (Assembly inner in (Assembly[])[Layer(service, "Domain"), Layer(service, "Application")])
        {
            References(inner).ShouldNotContain(
                name => Technologies.Any(technology => name.StartsWith(technology, StringComparison.Ordinal)),
                $"{inner.GetName().Name} references a technology");
        }
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Infrastructure_knows_nothing_of_the_host(string service) =>
        Projects(Layer(service, "Infrastructure")).ShouldNotContain($"Matchbook.{service}.Api");

    [Theory]
    [MemberData(nameof(Services))]
    public void No_layer_of_a_service_references_another_service(string service)
    {
        string[] others = [.. ServiceNames.Where(other => other != service)];

        foreach (string layer in (string[])["Domain", "Application", "Infrastructure", "Api"])
        {
            Projects(Layer(service, layer)).ShouldNotContain(
                name => others.Any(other => name.StartsWith($"Matchbook.{other}.", StringComparison.Ordinal)),
                $"Matchbook.{service}.{layer} reaches into another service");
        }
    }

    [Fact]
    public void The_contracts_and_the_shared_kernel_depend_on_nothing()
    {
        foreach (string name in (string[])["Matchbook.Contracts", "Matchbook.SharedKernel"])
        {
            References(Assembly.Load(name)).ShouldAllBe(reference => IsBaseLibrary(reference), $"{name} has a dependency");
        }
    }

    [Fact]
    public void The_gateway_knows_no_service() =>
        Projects(Assembly.Load("Matchbook.Gateway")).ShouldAllBe(
            name => name == "Matchbook.BuildingBlocks" || name == "Matchbook.SharedKernel");
}
