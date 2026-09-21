namespace backend.Data;

// ロールが特定のAPIアクションに対して持つ権限レベル。
public class RolePermission
{
    public required string RoleId { get; set; }
    public required Guid PermissionActionId { get; set; }
    public PermissionLevel Level { get; set; }
}
