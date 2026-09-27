using backend.Tenancy;

namespace backend.Data;

// テナント所属ユーザーへのテナントロールの割り当て。
// 所属とロールの両方へ TenantId を含む複合外部キーを張り、別テナントのロールを割り当てられないようにする。
public class TenantMemberRole : ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string UserId { get; set; }
    public Guid TenantRoleId { get; set; }
}
