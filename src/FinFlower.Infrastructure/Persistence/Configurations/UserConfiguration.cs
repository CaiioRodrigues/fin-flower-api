using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinFlower.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name).IsRequired().HasMaxLength(User.MaxNameLength);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(User.MaxEmailLength);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);

        // Impede duas contas com o mesmo e-mail mesmo sob requisições concorrentes:
        // a garantia é do banco, não de um "select antes do insert".
        builder.HasIndex(u => u.Email).IsUnique();

        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
