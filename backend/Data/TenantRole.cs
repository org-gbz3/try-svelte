using backend.Tenancy;

namespace backend.Data;

// テナント内で定義するロール。AspNetRoles はロール名がシステム全体で一意なため、
// 別テナントで同名ロールを作れるよう専用テーブルに分ける(decisions/0010参照)。
public class TenantRole : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
}
