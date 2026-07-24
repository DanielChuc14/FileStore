using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class AppConfigConfiguration : IEntityTypeConfiguration<AppConfig>
{
    public void Configure(EntityTypeBuilder<AppConfig> builder)
    {
        builder.ToTable("AppConfigs");
        builder.HasKey(c => c.Key);

        builder.Property(c => c.Key).HasMaxLength(100);
        builder.Property(c => c.Value).HasMaxLength(2000).IsRequired();

        builder.HasData(SeedData.AppConfigs);
    }
}
