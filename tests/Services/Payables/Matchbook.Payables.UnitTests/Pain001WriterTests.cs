using System.Globalization;
using System.Xml.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Matchbook.Payables.Application.Features.PaymentRuns;
using Matchbook.Payables.Domain;

namespace Matchbook.Payables.UnitTests;

public sealed class Pain001WriterTests
{
    private static readonly XNamespace Pain = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

    private static readonly Iban CreditorIban = Iban.Parse("GB82WEST12345698765432");

    private static readonly Bic CreditorBic = Bic.Parse("NWBKGB2L");

    [Fact]
    public async Task The_file_has_a_group_header_one_payment_block_and_a_transaction_per_invoice()
    {
        CreditTransfer[] transfers =
        [
            Transfer("01a0d06ef1fa7bd6984c4e6f0855d682", 100m, "INV-0001"),
            Transfer("01a0d06ef308711fa9d1da89fc8486e5", 1234.5m, "INV-0002"),
        ];

        XDocument file = await Write(Header(transfers), transfers);

        XElement root = file.Root!;
        root.Name.ShouldBe(Pain + "Document");
        XElement header = root.Element(Pain + "CstmrCdtTrfInitn")!.Element(Pain + "GrpHdr")!;
        Text(header, "MsgId").ShouldBe("01a0d06e346f7175bf629e1e68e8e56f");
        Text(header, "CreDtTm").ShouldBe("2026-09-01T09:30:00Z");
        Text(header, "NbOfTxs").ShouldBe("2");
        Text(header, "CtrlSum").ShouldBe("1334.50");
        Text(header, "InitgPty", "Nm").ShouldBe("Matchbook Trading Ltd");

        XElement payment = root.Descendants(Pain + "PmtInf").Single();
        Text(payment, "PmtInfId").ShouldBe("01a0d06e346f7175bf629e1e68e8e56f");
        Text(payment, "PmtMtd").ShouldBe("TRF");
        Text(payment, "NbOfTxs").ShouldBe("2");
        Text(payment, "CtrlSum").ShouldBe("1334.50");
        Text(payment, "ReqdExctnDt", "Dt").ShouldBe("2026-09-03");
        Text(payment, "Dbtr", "Nm").ShouldBe("Matchbook Trading Ltd");
        Text(payment, "DbtrAcct", "Id", "IBAN").ShouldBe("DE89370400440532013000");
        Text(payment, "DbtrAgt", "FinInstnId", "BICFI").ShouldBe("COBADEFFXXX");

        XElement second = payment.Elements(Pain + "CdtTrfTxInf").Last();
        Text(second, "PmtId", "EndToEndId").ShouldBe("01a0d06ef308711fa9d1da89fc8486e5");
        XElement amount = second.Element(Pain + "Amt")!.Element(Pain + "InstdAmt")!;
        amount.Value.ShouldBe("1234.50");
        amount.Attribute("Ccy")!.Value.ShouldBe("EUR");
        Text(second, "CdtrAgt", "FinInstnId", "BICFI").ShouldBe("NWBKGB2L");
        Text(second, "Cdtr", "Nm").ShouldBe("ACME & Söhne <UK> Ltd");
        Text(second, "CdtrAcct", "Id", "IBAN").ShouldBe("GB82WEST12345698765432");
        Text(second, "RmtInf", "Ustrd").ShouldBe("INV-0002");
    }

    [Fact]
    public async Task Names_are_escaped_by_the_writer_and_cut_to_the_140_characters_the_standard_allows()
    {
        string longName = new string('N', 139) + "\U0001F600";
        CreditTransfer[] transfers = [Transfer("e2e", 1m, "INV-1") with { CreditorName = longName }];

        XDocument file = await Write(Header(transfers), transfers);

        string written = file.Descendants(Pain + "Cdtr").Single().Element(Pain + "Nm")!.Value;
        written.ShouldBe(new string('N', 139));
    }

    [Fact]
    public async Task The_same_input_writes_the_same_bytes()
    {
        CreditTransfer[] transfers = [Transfer("a", 1m, "INV-1"), Transfer("b", 2.2m, "INV-2")];

        byte[] first = await Bytes(Header(transfers), transfers);
        byte[] second = await Bytes(Header(transfers), transfers);

        first.ShouldBe(second);
    }

    [Fact]
    public async Task A_file_whose_transactions_do_not_add_up_to_its_header_is_never_completed()
    {
        CreditTransfer[] transfers = [Transfer("a", 1m, "INV-1"), Transfer("b", 2m, "INV-2")];
        PaymentFileHeader wrong = Header(transfers) with { ControlSum = 4m };

        await Should.ThrowAsync<InvalidOperationException>(() => Bytes(wrong, transfers));
    }

    [Fact]
    public async Task An_end_to_end_id_longer_than_35_characters_is_refused()
    {
        CreditTransfer[] transfers = [Transfer(new string('x', 36), 1m, "INV-1")];

        await Should.ThrowAsync<ArgumentException>(() => Bytes(Header(transfers), transfers));
    }

    [Property(MaxTest = 100)]
    public Property The_control_sum_and_count_in_the_file_match_the_transactions_it_carries() =>
        Prop.ForAll(TransferLists.ToArbitrary(), async transfers =>
        {
            XDocument file = await Write(Header(transfers), transfers);

            XElement[] written = [.. file.Descendants(Pain + "CdtTrfTxInf")];
            decimal writtenSum = written.Sum(transaction => decimal.Parse(
                transaction.Element(Pain + "Amt")!.Element(Pain + "InstdAmt")!.Value,
                CultureInfo.InvariantCulture));

            written.Length.ShouldBe(transfers.Length);
            writtenSum.ShouldBe(transfers.Sum(transfer => transfer.Amount));
            foreach (XElement count in file.Descendants(Pain + "NbOfTxs"))
            {
                count.Value.ShouldBe(transfers.Length.ToString(CultureInfo.InvariantCulture));
            }

            foreach (XElement controlSum in file.Descendants(Pain + "CtrlSum"))
            {
                decimal.Parse(controlSum.Value, CultureInfo.InvariantCulture).ShouldBe(writtenSum);
            }

            written.Select(transaction => transaction.Element(Pain + "PmtId")!.Element(Pain + "EndToEndId")!.Value).ShouldBeUnique();
        });

    private static readonly Gen<CreditTransfer[]> TransferLists =
        Gen.Choose(1, 99_999_999)
            .Select(cents => cents / 100m)
            .NonEmptyListOf()
            .Select(amounts => amounts
                .Select((amount, i) => Transfer(Guid.NewGuid().ToString("N"), amount, $"INV-{i}"))
                .ToArray());

    private static CreditTransfer Transfer(string endToEndId, decimal amount, string invoiceNumber) =>
        new(endToEndId, amount, "ACME & Söhne <UK> Ltd", CreditorIban, CreditorBic, invoiceNumber);

    private static PaymentFileHeader Header(CreditTransfer[] transfers) =>
        new(
            "01a0d06e346f7175bf629e1e68e8e56f",
            A.Now,
            A.Today.AddDays(2),
            transfers.Length,
            transfers.Sum(transfer => transfer.Amount),
            "Matchbook Trading Ltd",
            Iban.Parse("DE89370400440532013000"),
            Bic.Parse("COBADEFFXXX"));

    private static async Task<XDocument> Write(PaymentFileHeader header, CreditTransfer[] transfers)
    {
        using var stream = new MemoryStream(await Bytes(header, transfers));
        return XDocument.Load(stream);
    }

    private static async Task<byte[]> Bytes(PaymentFileHeader header, CreditTransfer[] transfers)
    {
        using var stream = new MemoryStream();
        await Pain001Writer.WriteAsync(stream, header, transfers.ToAsyncEnumerable(), CancellationToken.None);
        return stream.ToArray();
    }

    private static string Text(XElement element, params string[] path) =>
        path.Aggregate(element, (current, name) => current.Element(Pain + name)!).Value;
}
