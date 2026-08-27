using JobAppManager.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAppManager.Data.Configurations;

public class StatusChangeConfiguration : IEntityTypeConfiguration<StatusChange>
{
    public void Configure(EntityTypeBuilder<StatusChange> builder)
    {
        builder.ToTable("StatusChanges");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Status).IsRequired();
        builder.Property(s => s.ChangedUtc).IsRequired();
        builder.Property(s => s.Note).HasMaxLength(1000);

        // Composite: history is always read for one application, in order.
        builder.HasIndex(s => new { s.ApplicationId, s.ChangedUtc });
    }
}
