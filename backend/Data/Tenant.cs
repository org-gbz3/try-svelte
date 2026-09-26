namespace backend.Data;

// 運営者が作成する利用組織の単位。ユーザーは TenantMembership を介して複数のテナントに所属できる。
public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    // ApplicationUser.CreatedAt と同じく、SQLite での並び替えに備えてUTCのDateTimeで保持する。
    public DateTime CreatedAt { get; set; }
}
