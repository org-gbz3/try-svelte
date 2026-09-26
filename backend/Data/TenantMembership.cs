namespace backend.Data;

// ユーザーのテナント所属。所属テナント一覧の取得でテナントを横断して参照するため、
// ITenantOwned にせずクエリフィルターの対象外とする。
public class TenantMembership
{
    public Guid TenantId { get; set; }
    public required string UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
