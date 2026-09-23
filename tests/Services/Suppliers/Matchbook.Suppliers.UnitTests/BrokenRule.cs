using Matchbook.SharedKernel;

namespace Matchbook.Suppliers.UnitTests;

internal static class BrokenRule
{
    /// <summary>Asserts the action is refused with this code and kind, which is what a client branches on.</summary>
    public static BusinessRuleException Expect(string code, ViolationKind kind, Action action)
    {
        BusinessRuleException exception = Should.Throw<BusinessRuleException>(action);
        exception.Code.ShouldBe(code);
        exception.Kind.ShouldBe(kind);
        return exception;
    }
}
