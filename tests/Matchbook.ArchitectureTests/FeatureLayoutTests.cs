using System.Reflection;
using System.Text.RegularExpressions;
using Matchbook.SharedKernel;

namespace Matchbook.ArchitectureTests;

/// <summary>
/// The shape ADR 0008 promises: every use case a command or a query, in a folder named for it under the area it
/// belongs to, with Api mirroring Application's areas. Handlers are registered by scanning, so these tests are what
/// keep a registration nobody wrote down from surprising anybody.
/// </summary>
public sealed class FeatureLayoutTests
{
    public static TheoryData<string> Services => new(["Suppliers", "Budgets", "Requisitions", "Purchasing", "Payables"]);

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_command_handler_sits_in_its_use_case_folder_and_is_named_for_its_command(string service) =>
        UseCaseViolations(service, typeof(ICommandHandler<,>), "Commands", "Command").ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_query_handler_sits_in_its_use_case_folder_and_is_named_for_its_query(string service) =>
        UseCaseViolations(service, typeof(IQueryHandler<,>), "Queries", "Query").ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_integration_event_handler_is_named_for_its_event_and_sits_in_integration_events(string service)
    {
        // One file per event, no folder each: a namespace named for the event would hide the event's own type from
        // the handler inside it.
        string[] violations =
        [
            .. Handlers(service, typeof(IIntegrationEventHandler<>)).Select(handler =>
            {
                string handled = Handled(handler, typeof(IIntegrationEventHandler<>))[0].Name;
                string expected = $"Matchbook.{service}.Application.IntegrationEvents.{handled}Handler";
                return handler.FullName == expected ? null : $"{handler.FullName} should be {expected}";
            }).OfType<string>(),
        ];

        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void A_handler_handles_one_request(string service) =>
        ApplicationTypes(service)
            .Where(static type => HandlerContracts(type).Length > 1)
            .Select(static type => type.FullName)
            .ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_command_and_query_has_exactly_one_handler(string service)
    {
        Type[] types = ApplicationTypes(service);
        Type[] handled = [.. types.SelectMany(static type => HandlerContracts(type)).Select(static contract => contract.GetGenericArguments()[0])];

        types
            .Where(static type => Implements(type, typeof(ICommand<>)) || Implements(type, typeof(IQuery<>)))
            .Where(request => handled.Count(type => type == request) != 1)
            .Select(static request => request.FullName)
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Nothing_else_in_application_is_called_a_handler(string service) =>
        ApplicationTypes(service)
            .Where(static type => type.Name.EndsWith("Handler", StringComparison.Ordinal) && HandlerContracts(type).Length == 0)
            .Select(static type => type.FullName)
            .ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(Services))]
    public void A_query_handler_neither_publishes_nor_runs_a_command(string service) =>
        Handlers(service, typeof(IQueryHandler<,>))
            .Where(static handler => handler.GetConstructors()
                .SelectMany(static constructor => constructor.GetParameters())
                .Any(static parameter => parameter.ParameterType == typeof(IEventPublisher)
                    || (parameter.ParameterType.IsGenericType
                        && parameter.ParameterType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                    || HandlerContracts(parameter.ParameterType).Any(static contract =>
                        contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))))
            .Select(static handler => handler.FullName)
            .ShouldBeEmpty();

    [Theory]
    [MemberData(nameof(Services))]
    public void Endpoints_live_under_the_api_features_of_their_area(string service)
    {
        Regex place = new($@"^Matchbook\.{service}\.Api\.Features\.(?<area>[A-Za-z]+)$");

        EndpointClasses(service)
            .Where(type => place.Match(type.Namespace ?? "") is not { Success: true } match
                || type.Name != $"{match.Groups["area"].Value}Endpoints")
            .Select(static type => type.FullName)
            .ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void The_api_has_the_same_areas_as_the_application(string service)
    {
        string[] application =
        [
            .. ApplicationTypes(service)
                .Select(type => Area(type.Namespace, $"Matchbook.{service}.Application.Features."))
                .OfType<string>()
                .Distinct()
                .Order(),
        ];
        string[] api =
        [
            .. EndpointClasses(service)
                .Select(type => Area(type.Namespace, $"Matchbook.{service}.Api.Features."))
                .OfType<string>()
                .Distinct()
                .Order(),
        ];

        application.ShouldNotBeEmpty();
        api.ShouldBe(application);
    }

    private static string[] UseCaseViolations(string service, Type contract, string folder, string suffix)
    {
        Regex place = new($@"^Matchbook\.{service}\.Application\.Features\.[A-Za-z]+\.{folder}\.(?<case>[A-Za-z]+)$");

        return
        [
            .. Handlers(service, contract).Select(handler =>
            {
                Type request = Handled(handler, contract)[0];

                if (place.Match(handler.Namespace ?? "") is not { Success: true } match)
                {
                    return $"{handler.FullName} is not in Features.<Area>.{folder}.<UseCase>";
                }

                string useCase = match.Groups["case"].Value;

                return handler.Name != $"{useCase}Handler" ? $"{handler.FullName} should be named {useCase}Handler"
                    : request.Name != $"{useCase}{suffix}" ? $"{handler.FullName} handles {request.Name}, not {useCase}{suffix}"
                    : request.Namespace != handler.Namespace ? $"{request.FullName} should sit next to its handler"
                    : null;
            }).OfType<string>(),
        ];
    }

    private static string? Area(string? space, string prefix) =>
        space is not null && space.StartsWith(prefix, StringComparison.Ordinal) ? space[prefix.Length..].Split('.')[0] : null;

    private static Type[] ApplicationTypes(string service) =>
        [.. Assembly.Load($"Matchbook.{service}.Application").GetTypes().Where(static type => !IsCompilerGenerated(type))];

    private static Type[] EndpointClasses(string service) =>
        [
            .. Assembly.Load($"Matchbook.{service}.Api").GetTypes()
                .Where(static type => type is { IsAbstract: true, IsSealed: true }
                    && type.Name.EndsWith("Endpoints", StringComparison.Ordinal)
                    && !IsCompilerGenerated(type)),
        ];

    private static Type[] Handlers(string service, Type contract) =>
        [.. ApplicationTypes(service).Where(type => type is { IsClass: true, IsAbstract: false } && Handled(type, contract).Length > 0)];

    private static Type[] Handled(Type type, Type contract) =>
        [
            .. type.GetInterfaces()
                .Where(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == contract)
                .Select(static implemented => implemented.GetGenericArguments()[0]),
        ];

    private static Type[] HandlerContracts(Type type) =>
        [
            .. type.GetInterfaces().Where(static implemented => implemented.IsGenericType
                && implemented.GetGenericTypeDefinition() is var definition
                && (definition == typeof(ICommandHandler<,>)
                    || definition == typeof(IQueryHandler<,>)
                    || definition == typeof(IIntegrationEventHandler<>))),
        ];

    private static bool Implements(Type type, Type contract) =>
        type.GetInterfaces().Any(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == contract);

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal) || type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute));
}
