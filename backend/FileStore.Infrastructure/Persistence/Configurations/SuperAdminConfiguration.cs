using FileStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileStore.Infrastructure.Persistence.Configurations;

public class SuperAdminConfiguration : IEntityTypeConfiguration<SuperAdmin>
{
    public void Configure(EntityTypeBuilder<SuperAdmin> builder)
    {
        builder.ToTable("SuperAdmins");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
        builder.Property(a => a.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(a => a.Email).IsUnique();

        builder.Property(a => a.IsActive).IsRequired();
    }
}
