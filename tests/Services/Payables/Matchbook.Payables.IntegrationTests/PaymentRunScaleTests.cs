using System.Diagnostics;
using System.Globalization;
using System.Xml;
using Matchbook.Payables.Application;
using Matchbook.Payables.Application.Features.PaymentRuns;
using Matchbook.Payables.Domain;
using Matchbook.Payables.Domain.Invoices;
using Matchbook.Payables.Domain.Orders;
using Matchbook.Payables.Domain.Suppliers;
using Matchbook.SharedKernel;
using Matchbook.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Matchbook.Payables.IntegrationTests;

/// <summary>
/// A run of several thousand invoices, drafted, released and downloaded through the API, timed. The file is read as a
/// stream and counted, never loaded whole, to show the server writes it the same way.
/// </summary>
public sealed class PaymentRunScaleTests(PayablesFixture fixture) : IClassFixture<PayablesFixture>
{
    private const int Invoices = 5_000;
    private const int Suppliers = 50;

    private readonly Scenario _given = new(fixture);

    [Fact]
    public async Task A_run_of_five_thousand_invoices_drafts_releases_and_streams_its_file()
    {
        decimal total = await SeedPayableInvoicesAsync();

        var clock = Stopwatch.StartNew();
        PaymentRunView draft = await _given.DraftAsync(TestUsers.Tess);
        TimeSpan drafted = clock.Elapsed;

        clock.Restart();
        PaymentRunView released = await _given.ReleaseAsync(TestUsers.Trevor, draft.Id);
        TimeSpan releasing = clock.Elapsed;

        clock.Restart();
        HttpResponseMessage response = await _given.As(TestUsers.Tess).GetAsync(
            new Uri($"/payment-runs/{draft.Id}/file", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead);
        (int transactions, decimal controlSum, long bytes) = await CountAsync(await response.Content.ReadAsStreamAsync());
        TimeSpan downloading = clock.Elapsed;

        draft.ItemCount.ShouldBe(Invoices);
        released.PaidCount.ShouldBe(Invoices);
        released.PaidTotal.ShouldBe(total);
        transactions.ShouldBe(Invoices);
        controlSum.ShouldBe(total);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Invoices} invoices from {Suppliers} suppliers: draft {drafted.TotalMilliseconds:0} ms, release {releasing.TotalMilliseconds:0} ms, file {downloading.TotalMilliseconds:0} ms for {bytes / 1024.0 / 1024.0:0.0} MiB."));
    }

    /// <summary>
    /// Writes suppliers and payable invoices straight through the DbContext: capturing five thousand over HTTP would
    /// time the match, which other tests cover, and not the run.
    /// </summary>
    private async Task<decimal> SeedPayableInvoicesAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly invoiceDate = Scenario.Today.AddDays(-40);
        Actor clerk = TestUsers.Alice;
        decimal total = 0m;

        await _given.Host.InScopeAsync<int>(async services =>
        {
            IPayablesDb db = services.GetRequiredService<IPayablesDb>();
            Guid[] suppliers = [.. Enumerable.Range(0, Suppliers).Select(_ => Guid.CreateVersion7())];
            foreach (Guid supplier in suppliers)
            {
                db.Suppliers.Add(Supplier.From(supplier, new SupplierSnapshot(
                    1,
                    "Scale Supplier",
                    IsActive: true,
                    PaymentTermsDays: 30,
                    new SupplierAccount(1, Scenario.Protector.Protect("GB82WEST12345698765432"), "5432", Bic.Parse("NWBKGB2L"), "Scale Supplier"),
                    now)));
            }

            for (int i = 0; i < Invoices; i++)
            {
                Guid supplier = suppliers[i % Suppliers];
                decimal amount = 100m + (i % 997) / 100m;
                PurchaseOrder order = PurchaseOrder.FirstMentioned(Guid.CreateVersion7());
                order.RecordIssue($"PO-SCALE-{i}", supplier, [new OrderedLine(1, 1, amount)], now);

                Invoice invoice = Invoice.Capture(Guid.CreateVersion7(), supplier, $"SCALE-{i:D5}", invoiceDate, order.Id, [new InvoiceLine(1, 1, amount)], amount, clerk, now);
                invoice.Evaluate(order, new OrderPosition(new Dictionary<int, decimal> { [1] = 1 }, new Dictionary<int, decimal>()), 30, now);
                invoice.Status.ShouldBe(InvoiceStatus.Payable);

                db.Invoices.Add(invoice);
                total += amount;
            }

            return await db.SaveChangesAsync(CancellationToken.None);
        });

        return total;
    }

    private static async Task<(int Transactions, decimal ControlSum, long Bytes)> CountAsync(Stream body)
    {
        var counting = new CountingStream(body);
        using XmlReader reader = XmlReader.Create(counting, new XmlReaderSettings { Async = true });
        int transactions = 0;
        decimal sum = 0m;

        while (await reader.ReadAsync())
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "InstdAmt" })
            {
                transactions++;
                sum += decimal.Parse(await reader.ReadElementContentAsStringAsync(), CultureInfo.InvariantCulture);
            }
        }

        return (transactions, sum, counting.BytesRead);
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesRead;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
