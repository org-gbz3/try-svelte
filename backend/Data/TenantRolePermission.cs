namespace backend.Data;

// テナントロールが特定のAPIアクションに対して持つ権限レベル。RolePermission のテナント版。
public class TenantRolePermission
{
    public Guid TenantRoleId { get; set; }
    public Guid PermissionActionId { get; set; }
    public PermissionLevel Level { get; set; }
}
