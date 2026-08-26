using JobAppManager.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAppManager.Data.Configurations;

public class SubmittedItemConfiguration : IEntityTypeConfiguration<SubmittedItem>
{
    public void Configure(EntityTypeBuilder<SubmittedItem> builder)
    {
        builder.ToTable("SubmittedItems");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Kind).IsRequired();

        builder.HasIndex(s => s.ApplicationId);
    }
}
