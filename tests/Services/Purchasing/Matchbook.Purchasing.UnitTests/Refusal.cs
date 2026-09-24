using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.UnitTests;

internal static class Refusal
{
    /// <summary>Asserts the action breaks the named rule, as the kind the API will map to a status code.</summary>
    public static BusinessRuleException ShouldBeRefused(Action action, string code, ViolationKind kind)
    {
        BusinessRuleException refusal = Should.Throw<BusinessRuleException>(action);
        refusal.Code.ShouldBe(code);
        refusal.Kind.ShouldBe(kind);
        return refusal;
    }
}
