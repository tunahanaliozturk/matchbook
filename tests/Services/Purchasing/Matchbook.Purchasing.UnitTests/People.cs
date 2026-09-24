using Matchbook.SharedKernel;

namespace Matchbook.Purchasing.UnitTests;

/// <summary>The people the tests act as, named after the seeded realm users where one fits.</summary>
internal static class People
{
    public static readonly Actor Bruno = Person("bruno", Roles.Buyer);

    public static readonly Actor Beth = Person("beth", Roles.Buyer);

    public static readonly Actor Rosa = Person("rosa", Roles.Receiver);

    public static readonly Actor Audrey = Person("audrey", Roles.Auditor);

    /// <summary>Bruno again, also holding the receiver role: the case separation of duties exists for.</summary>
    public static readonly Actor BrunoAsReceiver = new(Bruno.Id, "bruno", new HashSet<string> { Roles.Buyer, Roles.Receiver });

    /// <summary>A buyer who also receives, but did not issue the order in question.</summary>
    public static readonly Actor BethAsReceiver = new(Beth.Id, "beth", new HashSet<string> { Roles.Buyer, Roles.Receiver });

    private static Actor Person(string name, string role) => new(Guid.CreateVersion7(), name, new HashSet<string> { role });
}
