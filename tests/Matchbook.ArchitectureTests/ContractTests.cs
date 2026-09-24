using System.Reflection;
using System.Text;
using Matchbook.Contracts.Budgets;

namespace Matchbook.ArchitectureTests;

/// <summary>
/// Pins the shape of every integration event. A message's full type name is its address on the broker, and its
/// members are what an older or newer consumer will find in it, so any change here is a change to a promise made
/// to four other services. The test fails with the new shape; if the change follows docs/contracts.md (members
/// added at the end, nothing renamed or removed), update <c>contracts.txt</c> in the same commit.
/// </summary>
public sealed class ContractTests
{
    private static readonly Assembly Contracts = typeof(FundsReserved).Assembly;

    [Fact]
    public void Every_contract_keeps_its_name_and_its_members()
    {
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts.txt")).ReplaceLineEndings("\n");

        string actual = Describe();

        if (actual != expected)
        {
            string written = Path.Combine(AppContext.BaseDirectory, "contracts.actual.txt");
            File.WriteAllText(written, actual);
            Assert.Fail($"An integration event changed. Follow docs/contracts.md, then copy {written} over contracts.txt.");
        }
    }

    [Fact]
    public void Every_contract_is_an_immutable_sealed_record()
    {
        foreach (Type type in Contracts.GetExportedTypes().Where(static type => !IsConstants(type)))
        {
            type.IsSealed.ShouldBeTrue($"{type.FullName} is not sealed");
            type.GetMethod("<Clone>$").ShouldNotBeNull($"{type.FullName} is not a record");
            type.GetProperties().ShouldAllBe(
                property => property.SetMethod == null || property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Length > 0,
                $"{type.FullName} has a settable property");
        }
    }

    [Fact]
    public void Values_that_look_like_enums_are_strings_on_the_wire() =>
        Contracts.GetExportedTypes()
            .SelectMany(static type => type.GetProperties())
            .ShouldAllBe(static property => !property.PropertyType.IsEnum);

    private static bool IsConstants(Type type) => type.IsAbstract && type.IsSealed;

    private static string Describe()
    {
        var text = new StringBuilder();

        foreach (Type type in Contracts.GetExportedTypes().OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            if (IsConstants(type))
            {
                IEnumerable<string> values = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(static field => field.IsLiteral)
                    .Select(static field => $"{field.Name} = \"{field.GetRawConstantValue()}\"");
                text.Append(type.FullName).Append(" { ").AppendJoin(", ", values).Append(" }\n");
                continue;
            }

            // Positional records: the primary constructor lists the members in declaration order.
            ConstructorInfo constructor = type.GetConstructors().OrderByDescending(static c => c.GetParameters().Length).First();
            var nullability = new NullabilityInfoContext();
            IEnumerable<string> members = constructor.GetParameters().Select(parameter =>
                $"{Name(parameter.ParameterType)}{(nullability.Create(parameter).ReadState == NullabilityState.Nullable && !parameter.ParameterType.IsValueType ? "?" : "")} {parameter.Name}");
            text.Append(type.FullName).Append('(').AppendJoin(", ", members).Append(")\n");
        }

        return text.ToString();
    }

    private static string Name(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying) + "?";
        }

        if (type.IsGenericType)
        {
            string name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
        }

        return type.Name;
    }
}
