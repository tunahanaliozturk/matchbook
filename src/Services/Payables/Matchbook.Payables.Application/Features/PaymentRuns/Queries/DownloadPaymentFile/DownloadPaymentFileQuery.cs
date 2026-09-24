using Matchbook.SharedKernel;

namespace Matchbook.Payables.Application.Features.PaymentRuns.Queries.DownloadPaymentFile;

public sealed record DownloadPaymentFileQuery(Guid PaymentRunId) : IQuery<PaymentFile>;
