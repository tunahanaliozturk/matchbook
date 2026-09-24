using System.Globalization;
using System.Text;
using System.Xml;
using Matchbook.Payables.Domain;

namespace Matchbook.Payables.Application.Features.PaymentRuns;

/// <summary>What the group header and the payment information block of a credit transfer file say.</summary>
internal sealed record PaymentFileHeader(
    string MessageId,
    DateTimeOffset CreatedAt,
    DateOnly ExecutionDate,
    int NumberOfTransactions,
    decimal ControlSum,
    string DebtorName,
    Iban DebtorIban,
    Bic DebtorBic);

/// <summary>One credit transfer: one invoice paid to one supplier account.</summary>
internal sealed record CreditTransfer(
    string EndToEndId,
    decimal Amount,
    string CreditorName,
    Iban CreditorIban,
    Bic CreditorBic,
    string RemittanceInformation);

/// <summary>
/// Writes an ISO 20022 <c>pain.001.001.09</c> customer credit transfer initiation: one payment information block
/// for the run, one transaction per invoice.
/// </summary>
/// <remarks>
/// It writes as it reads, so a run of a hundred thousand invoices never sits in memory as a document. The header
/// has to state the count and control sum before the first transaction, so they come from the run, and the writer
/// adds up what it actually wrote and throws if the two disagree: a truncated download is better than a file whose
/// totals do not match its contents. The output depends only on the input, byte for byte.
/// </remarks>
internal static class Pain001Writer
{
    public const string Namespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

    private const int MaxIdLength = 35;
    private const int MaxTextLength = 140;

    private static readonly XmlWriterSettings Settings = new()
    {
        Async = true,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        CloseOutput = false,
    };

    public static async Task WriteAsync(
        Stream destination,
        PaymentFileHeader header,
        IAsyncEnumerable<CreditTransfer> transfers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(transfers);

        await using XmlWriter xml = XmlWriter.Create(destination, Settings);

        await xml.WriteStartDocumentAsync();
        await xml.WriteStartElementAsync(null, "Document", Namespace);
        await xml.WriteStartElementAsync(null, "CstmrCdtTrfInitn", Namespace);

        await xml.WriteStartElementAsync(null, "GrpHdr", Namespace);
        await Element(xml, "MsgId", Id(header.MessageId));
        await Element(xml, "CreDtTm", header.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        await Element(xml, "NbOfTxs", header.NumberOfTransactions.ToString(CultureInfo.InvariantCulture));
        await Element(xml, "CtrlSum", Money(header.ControlSum));
        await xml.WriteStartElementAsync(null, "InitgPty", Namespace);
        await Element(xml, "Nm", Text(header.DebtorName));
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();

        await xml.WriteStartElementAsync(null, "PmtInf", Namespace);
        await Element(xml, "PmtInfId", Id(header.MessageId));
        await Element(xml, "PmtMtd", "TRF");
        await Element(xml, "NbOfTxs", header.NumberOfTransactions.ToString(CultureInfo.InvariantCulture));
        await Element(xml, "CtrlSum", Money(header.ControlSum));
        await xml.WriteStartElementAsync(null, "ReqdExctnDt", Namespace);
        await Element(xml, "Dt", header.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await xml.WriteEndElementAsync();
        await Party(xml, "Dbtr", header.DebtorName);
        await Account(xml, "DbtrAcct", header.DebtorIban);
        await Agent(xml, "DbtrAgt", header.DebtorBic);

        int count = 0;
        decimal sum = 0m;
        await foreach (CreditTransfer transfer in transfers.WithCancellation(cancellationToken))
        {
            await Transaction(xml, transfer);
            count++;
            sum += transfer.Amount;
        }

        if (count != header.NumberOfTransactions || sum != header.ControlSum)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file header promises {header.NumberOfTransactions} transactions for {header.ControlSum:0.00}, but {count} for {sum:0.00} were written."));
        }

        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
        await xml.WriteEndDocumentAsync();
        await xml.FlushAsync();
    }

    private static async Task Transaction(XmlWriter xml, CreditTransfer transfer)
    {
        await xml.WriteStartElementAsync(null, "CdtTrfTxInf", Namespace);

        await xml.WriteStartElementAsync(null, "PmtId", Namespace);
        await Element(xml, "EndToEndId", Id(transfer.EndToEndId));
        await xml.WriteEndElementAsync();

        await xml.WriteStartElementAsync(null, "Amt", Namespace);
        await xml.WriteStartElementAsync(null, "InstdAmt", Namespace);
        await xml.WriteAttributeStringAsync(null, "Ccy", null, "EUR");
        await xml.WriteStringAsync(Money(transfer.Amount));
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();

        await Agent(xml, "CdtrAgt", transfer.CreditorBic);
        await Party(xml, "Cdtr", transfer.CreditorName);
        await Account(xml, "CdtrAcct", transfer.CreditorIban);

        await xml.WriteStartElementAsync(null, "RmtInf", Namespace);
        await Element(xml, "Ustrd", Text(transfer.RemittanceInformation));
        await xml.WriteEndElementAsync();

        await xml.WriteEndElementAsync();
    }

    private static async Task Party(XmlWriter xml, string name, string partyName)
    {
        await xml.WriteStartElementAsync(null, name, Namespace);
        await Element(xml, "Nm", Text(partyName));
        await xml.WriteEndElementAsync();
    }

    private static async Task Account(XmlWriter xml, string name, Iban iban)
    {
        await xml.WriteStartElementAsync(null, name, Namespace);
        await xml.WriteStartElementAsync(null, "Id", Namespace);
        await Element(xml, "IBAN", iban.Value);
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
    }

    private static async Task Agent(XmlWriter xml, string name, Bic bic)
    {
        await xml.WriteStartElementAsync(null, name, Namespace);
        await xml.WriteStartElementAsync(null, "FinInstnId", Namespace);
        await Element(xml, "BICFI", bic.Value);
        await xml.WriteEndElementAsync();
        await xml.WriteEndElementAsync();
    }

    private static Task Element(XmlWriter xml, string name, string value) =>
        xml.WriteElementStringAsync(null, name, Namespace, value);

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Id(string id) =>
        id.Length is > 0 and <= MaxIdLength
            ? id
            : throw new ArgumentException($"A pain.001 identifier is 1 to {MaxIdLength} characters: '{id}'.", nameof(id));

    // Max140Text. Cut at a character boundary so a surrogate pair is never split into invalid XML.
    private static string Text(string text)
    {
        if (text.Length <= MaxTextLength)
        {
            return text;
        }

        int length = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
        return text[..length];
    }
}
