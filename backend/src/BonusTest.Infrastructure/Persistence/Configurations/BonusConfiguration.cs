using BonusTest.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BonusTest.Infrastructure.Persistence.Configurations;

public class BonusConfiguration : IEntityTypeConfiguration<Bonus>
{
    public void Configure(EntityTypeBuilder<Bonus> builder)
    {
        builder.ToTable("Bonus").HasKey(x => x.BonusId);
        builder.Property(x => x.DepartmentCode).HasColumnType("char(5)").IsRequired();
        builder.Property(x => x.Period).HasColumnType("char(7)").IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnType("char(6)").IsRequired();
        builder.Property(x => x.StateCode).HasColumnType("char(4)").IsRequired();
        builder.Property(x => x.Value).HasPrecision(28, 14);
        builder.Property(x => x.MonthlySalary).HasPrecision(16, 2);
        builder.Property(x => x.TaxRate).HasPrecision(5, 2);
        builder.Property(x => x.GrossAmount).HasPrecision(16, 2);
        builder.Property(x => x.TaxAmount).HasPrecision(16, 2);
        builder.Property(x => x.NetAmount).HasPrecision(16, 2);
        builder.Property(x => x.CreatedBy).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ModifiedBy).HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasPrecision(7);
        builder.Property(x => x.ModifiedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.DepartmentCode, x.EmployeeNumber })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BonusType>().WithMany().HasForeignKey(x => x.BonusTypeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<BonusReason>().WithMany().HasForeignKey(x => x.ReasonCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<State>().WithMany().HasForeignKey(x => x.StateCode).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.DepartmentCode, x.EmployeeNumber, x.Period })
            .IsUnique().HasDatabaseName("UX_Bonus_Employee_Period")
            .HasFilter("StateCode <> '0203' AND IsDeleted = 0");
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
