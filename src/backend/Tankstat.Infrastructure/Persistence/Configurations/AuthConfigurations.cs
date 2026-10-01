using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tankstat.Domain.Access;
using Tankstat.Domain.Users;

namespace Tankstat.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();
        b.Property(u => u.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(u => u.Subject).HasMaxLength(255).IsRequired();
        b.Property(u => u.Email).HasMaxLength(254).IsRequired();
        b.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(500);
        b.HasIndex(u => new { u.Provider, u.Subject }).IsUnique();
        b.HasIndex(u => u.AvatarImageId);
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.ToTable("PasswordResetTokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.SecretHash).HasMaxLength(128).IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccessGrantConfiguration : IEntityTypeConfiguration<AccessGrant>
{
    public void Configure(EntityTypeBuilder<AccessGrant> b)
    {
        b.ToTable("AccessGrants");
        b.HasKey(g => g.Id);
        b.Property(g => g.Id).ValueGeneratedNever();
        b.Property(g => g.Level).HasConversion<string>().HasMaxLength(10);
        b.HasOne<User>().WithMany().HasForeignKey(g => g.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(g => g.GranteeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(g => new { g.OwnerId, g.GranteeId }).IsUnique();
    }
}

internal sealed class AccessSettingsConfiguration : IEntityTypeConfiguration<AccessSettings>
{
    public void Configure(EntityTypeBuilder<AccessSettings> b)
    {
        b.ToTable("AccessSettings");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.DefaultLevelForOthers).HasConversion<string>().HasMaxLength(10);
    }
}

internal sealed class ResourceGrantConfiguration : IEntityTypeConfiguration<ResourceGrant>
{
    public void Configure(EntityTypeBuilder<ResourceGrant> b)
    {
        b.ToTable("ResourceGrants");
        b.HasKey(g => g.Id);
        b.Property(g => g.Id).ValueGeneratedNever();
        b.Property(g => g.ResourceType).HasConversion<string>().HasMaxLength(20);
        b.Property(g => g.Feature).HasConversion<string>().HasMaxLength(20);
        b.Property(g => g.Level).HasConversion<string>().HasMaxLength(10);
        b.HasOne<User>().WithMany().HasForeignKey(g => g.GranteeId).OnDelete(DeleteBehavior.Cascade);
        // The resource id points at different tables by type, so it has no foreign key; grants are removed with their resource in code.
        b.HasIndex(g => new { g.ResourceType, g.ResourceId, g.GranteeId, g.Feature }).IsUnique();
        b.HasIndex(g => new { g.GranteeId, g.ResourceType, g.Feature });
    }
}
