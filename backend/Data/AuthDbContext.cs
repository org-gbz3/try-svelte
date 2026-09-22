using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<PermissionAction> PermissionActions => Set<PermissionAction>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PermissionAction>(entity =>
        {
            entity.HasIndex(action => action.ActionKey).IsUnique();
        });

        builder.Entity<RolePermission>(entity =>
        {
            entity.HasKey(permission => new { permission.RoleId, permission.PermissionActionId });
            // ロール削除・権限アクション廃止に追随し、宙に浮いた権限行を残さない。
            entity.HasOne<IdentityRole>()
                .WithMany()
                .HasForeignKey(permission => permission.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PermissionAction>()
                .WithMany()
                .HasForeignKey(permission => permission.PermissionActionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
