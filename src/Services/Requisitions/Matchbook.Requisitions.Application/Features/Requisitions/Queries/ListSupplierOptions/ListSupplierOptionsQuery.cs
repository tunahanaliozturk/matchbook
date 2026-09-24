using Matchbook.SharedKernel;

namespace Matchbook.Requisitions.Application.Features.Requisitions.Queries.ListSupplierOptions;

public sealed record ListSupplierOptionsQuery : IQuery<IReadOnlyList<SupplierOption>>;

/// <summary>A supplier as the requisition form offers it.</summary>
public sealed record SupplierOption(Guid Id, string LegalName);
