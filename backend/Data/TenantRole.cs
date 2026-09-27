using backend.Tenancy;

namespace backend.Data;

// テナント内で定義するロール。AspNetRoles はロール名がシステム全体で一意なため、
// 別テナントで同名ロールを作れるよう専用テーブルに分ける(decisions/0010参照)。
public class TenantRole : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    // テナント作成時に作る既定ロール「テナント管理者」の印。名前は改名できるため、名前ではなくこの列で判定する。
    // 既定ロールは全テナント用権限キーへの Write を常に持ち、削除・権限変更はできない(decisions/0011参照)。
    public bool IsDefaultAdmin { get; set; }
}
