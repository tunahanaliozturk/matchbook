using Matchbook.Budgets.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchbook.Budgets.Infrastructure.Configurations;

internal sealed class IdempotentRequestConfiguration : IEntityTypeConfiguration<IdempotentRequest>
{
    public const string PrimaryKey = "pk_idempotent_requests";

    public void Configure(EntityTypeBuilder<IdempotentRequest> entity)
    {
        entity.ToTable("idempotent_requests");
        entity.HasKey(r => r.Id).HasName(PrimaryKey);
        entity.Property(r => r.Id).ValueGeneratedNever();
        entity.Property(r => r.Operation).HasMaxLength(64);

        // jsonb rather than text: the database checks it is JSON, and an operator can query a request by its fields.
        entity.Property(r => r.Request).HasColumnType("jsonb");
        entity.Property(r => r.Response).HasColumnType("jsonb");
    }
}
