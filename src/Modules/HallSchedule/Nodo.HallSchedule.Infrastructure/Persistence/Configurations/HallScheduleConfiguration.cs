using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nodo.HallSchedule.Domain;

namespace Nodo.HallSchedule.Infrastructure.Persistence.Configurations;

internal sealed class HallScheduleConfiguration : IEntityTypeConfiguration<HallScheduleAggregate>
{
    public void Configure(EntityTypeBuilder<HallScheduleAggregate> builder)
    {
        builder.ToTable("hall_schedules");

        // The identity is (hall, day): two columns, rebuilt through the aggregate's private constructor.
        builder.Ignore(x => x.Id);
        builder.Property(x => x.HallId).HasConversion(id => id.Value, value => new HallId(value));
        builder.Property(x => x.Date);
        builder.HasKey(x => new { x.HallId, x.Date });

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasMany(x => x.Blocks)
            .WithOne()
            .HasForeignKey("HallId", "ScheduleDate")
            .HasPrincipalKey(x => new { x.HallId, x.Date })
            .IsRequired() // a block without a hall would escape the exclusion constraint
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Blocks).HasField("_blocks").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.ActiveBlocks);
    }
}
