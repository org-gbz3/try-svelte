using System.Reflection;
using backend.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class AuthDbContext(DbContextOptions<AuthDbContext> options, TenantContext tenantContext)
    : IdentityDbContext<ApplicationUser>(options)
{
    // IgnoreQueryFilters で個別に解除できるよう、テナント分離のフィルターに名前を付ける。
    public const string TenantFilter = "Tenant";

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(AuthDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<PermissionAction> PermissionActions => Set<PermissionAction>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();
    public DbSet<TenantMemberRole> TenantMemberRoles => Set<TenantMemberRole>();
    public DbSet<TenantRolePermission> TenantRolePermissions => Set<TenantRolePermission>();

    // クエリフィルターはモデルとしてキャッシュされるため、テナントIDはコンテキストのメンバー経由で参照し、
    // クエリ実行ごとに現在のリクエストの値へ差し替えられるようにする。
    private Guid? CurrentTenantId => tenantContext.TenantId;

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

        builder.Entity<Tenant>(entity =>
        {
            entity.Property(tenant => tenant.Name).HasMaxLength(256);
        });

        builder.Entity<TenantMembership>(entity =>
        {
            entity.HasKey(membership => new { membership.TenantId, membership.UserId });
            // ログインユーザーの所属テナント一覧をユーザーIDから引くため。
            entity.HasIndex(membership => membership.UserId);
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(membership => membership.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TenantRole>(entity =>
        {
            entity.Property(role => role.Name).HasMaxLength(256);
            entity.HasIndex(role => new { role.TenantId, role.Name }).IsUnique();
            // 既定ロールはテナントごとに1つだけ。
            entity.HasIndex(role => role.TenantId).IsUnique().HasFilter("[IsDefaultAdmin] = 1");
            // TenantMemberRole から (TenantId, Id) で参照し、別テナントのロール割り当てをDB制約で防ぐ。
            entity.HasAlternateKey(role => new { role.TenantId, role.Id });
            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(role => role.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TenantMemberRole>(entity =>
        {
            entity.HasKey(memberRole => new { memberRole.TenantId, memberRole.UserId, memberRole.TenantRoleId });
            // 所属解除に追随して割り当ても消す。
            entity.HasOne<TenantMembership>()
                .WithMany()
                .HasForeignKey(memberRole => new { memberRole.TenantId, memberRole.UserId })
                .OnDelete(DeleteBehavior.Cascade);
            // SQL Server はテナント削除からの連鎖削除経路が複数になる構成を拒否するため、ロール側は連鎖させない。
            // ロール削除時は割り当てを先に削除する。
            entity.HasOne<TenantRole>()
                .WithMany()
                .HasForeignKey(memberRole => new { memberRole.TenantId, memberRole.TenantRoleId })
                .HasPrincipalKey(role => new { role.TenantId, role.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TenantRolePermission>(entity =>
        {
            entity.HasKey(permission => new { permission.TenantRoleId, permission.PermissionActionId });
            entity.HasOne<TenantRole>()
                .WithMany()
                .HasForeignKey(permission => permission.TenantRoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<PermissionAction>()
                .WithMany()
                .HasForeignKey(permission => permission.PermissionActionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 個々のクエリで条件を書き忘れても他テナントの行を返さないよう、ITenantOwned の全エンティティに一括適用する。
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(type => typeof(ITenantOwned).IsAssignableFrom(type.ClrType)))
            ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
    }

    // テナント文脈がないときは一致する行がなく0件になる(横断参照は IgnoreQueryFilters で明示させる)。
    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantOwned =>
        builder.Entity<TEntity>().HasQueryFilter(TenantFilter, entity => entity.TenantId == CurrentTenantId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // クエリフィルターは読み取りだけを守るため、書き込み側でも対象テナントとの一致を検証する。
    private void EnforceTenantOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty && CurrentTenantId is { } current)
                    entry.Entity.TenantId = current;
                // 運営者APIなどテナント文脈のない処理では、TenantId の明示を必須にする。
                if (entry.Entity.TenantId == Guid.Empty)
                    throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} の TenantId が設定されていません。");
            }
            else if (entry.State == EntityState.Modified && entry.Property(nameof(ITenantOwned.TenantId)).IsModified)
            {
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} の TenantId は変更できません。");
            }

            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && CurrentTenantId is { } tenantId && entry.Entity.TenantId != tenantId)
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} は対象テナント以外のデータです。");
        }
    }
}
