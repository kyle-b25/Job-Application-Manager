using JobAppManager.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobAppManager.Data.Configurations;

public class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("Applications");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.CompanyName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.JobTitle).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Location).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(4000);
        builder.Property(a => a.JobUrl).HasMaxLength(2000);

        builder.Property(a => a.InterestLevel).IsRequired();
        builder.Property(a => a.Status).IsRequired();

        // The spreadsheet page sorts and filters on these three.
        builder.HasIndex(a => a.DateApplied);
        builder.HasIndex(a => a.CompanyName);
        builder.HasIndex(a => a.Status);

        builder.HasMany(a => a.Contacts)
            .WithOne(c => c.Application!)
            .HasForeignKey(c => c.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.StatusHistory)
            .WithOne(s => s.Application!)
            .HasForeignKey(s => s.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
