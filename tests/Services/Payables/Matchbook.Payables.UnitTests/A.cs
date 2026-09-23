using Matchbook.Payables.Domain;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;

namespace Matchbook.Payables.UnitTests;

/// <summary>Test data with sensible defaults, so each test states only what it is about.</summary>
internal static class A
{
    public static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 30, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    public static readonly Guid SupplierId = Guid.Parse("5a000000-0000-4000-8000-000000000001");

    public static readonly Guid OtherSupplierId = Guid.Parse("5a000000-0000-4000-8000-000000000002");

    public static readonly Guid OrderId = Guid.Parse("0d000000-0000-4000-8000-000000000001");

    public static readonly Actor Clerk = Person(Roles.ApClerk);

    public static readonly Actor Approver = Person(Roles.ApApprover);

    public static readonly Actor Treasurer = Person(Roles.Treasurer);

    public static readonly Actor SecondTreasurer = Person(Roles.Treasurer);

    public static Actor Person(params string[] roles) => new(Guid.NewGuid(), "someone", roles.ToHashSet());

    public static InvoiceLine Line(int lineNumber, decimal quantity, decimal unitPrice) => new(lineNumber, quantity, unitPrice);

    /// <summary>A captured invoice whose declared total is the sum of its lines.</summary>
    public static Invoice Invoice(params InvoiceLine[] lines) => Invoice("INV-1", lines);

    public static Invoice Invoice(string number, params InvoiceLine[] lines) =>
        Invoice(number, Today, SupplierId, Clerk, lines);

    public static Invoice Invoice(string number, DateOnly invoiceDate, Guid supplierId, Actor clerk, params InvoiceLine[] lines) =>
        Domain.Invoices.Invoice.Capture(
            Guid.CreateVersion7(Now),
            supplierId,
            number,
            invoiceDate,
            OrderId,
            lines,
            lines.Sum(line => line.Amount),
            clerk,
            Now);

    public static PurchaseOrder IssuedOrder(params OrderedLine[] lines) => IssuedOrder(SupplierId, lines);

    public static PurchaseOrder IssuedOrder(Guid supplierId, params OrderedLine[] lines)
    {
        PurchaseOrder order = PurchaseOrder.FirstMentioned(OrderId);
        order.RecordIssue("PO-2026-000001", supplierId, lines, Now);
        return order;
    }

    public static OrderedLine Ordered(int lineNumber, decimal quantity, decimal unitPrice) => new(lineNumber, quantity, unitPrice);

    /// <summary>A position with the given quantities received and nothing invoiced yet.</summary>
    public static OrderPosition Received(params (int LineNumber, decimal Quantity)[] received) =>
        new(received.ToDictionary(line => line.LineNumber, line => line.Quantity), new Dictionary<int, decimal>());

    public static OrderPosition Position(
        IEnumerable<(int LineNumber, decimal Quantity)> received,
        IEnumerable<(int LineNumber, decimal Quantity)> invoiced) =>
        new(
            received.ToDictionary(line => line.LineNumber, line => line.Quantity),
            invoiced.ToDictionary(line => line.LineNumber, line => line.Quantity));

    public static Supplier Supplier(Guid id, bool active = true, int? accountVersion = 1, int paymentTermsDays = 30) =>
        Domain.Suppliers.Supplier.From(
            id,
            new SupplierSnapshot(
                1,
                "ACME Industrial Supplies GmbH",
                active,
                paymentTermsDays,
                accountVersion is { } version ? Account(version) : null,
                Now));

    public static SupplierAccount Account(int version) =>
        new(version, Iban.Parse("DE89370400440532013000"), Bic.Parse("COBADEFFXXX"), "ACME Industrial Supplies GmbH");
}
