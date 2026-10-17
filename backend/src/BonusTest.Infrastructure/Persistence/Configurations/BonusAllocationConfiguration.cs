using BonusTest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BonusTest.Infrastructure.Persistence.Configurations;

public class BonusAllocationConfiguration : IEntityTypeConfiguration<BonusAllocation>
{
    public void Configure(EntityTypeBuilder<BonusAllocation> builder)
    {
        builder.ToTable("BonusAllocation").HasKey(x => new { x.BonusId, x.LineNumber });
        builder.Property(x => x.LineNumber).ValueGeneratedNever();
        builder.Property(x => x.ProjectName).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Hours).HasPrecision(18, 2);
        builder.Property(x => x.Amount).HasPrecision(16, 2);
        builder.HasOne<Bonus>().WithMany(x => x.Allocations).HasForeignKey(x => x.BonusId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
