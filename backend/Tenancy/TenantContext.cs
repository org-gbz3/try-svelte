namespace backend.Tenancy;

// リクエスト中に操作対象となっているテナント。URLパスの {tenantId} から設定し、
// Cookie・セッションには保持しない(複数タブで別テナントを開いても混線させないため)。
public sealed class TenantContext
{
    public Guid? TenantId { get; private set; }

    public void Set(Guid tenantId)
    {
        // 途中で対象テナントが変わると、フィルター済みの読み取りと書き込みの対象がずれるため再設定を禁止する。
        if (TenantId is not null && TenantId != tenantId)
            throw new InvalidOperationException("リクエスト中に対象テナントを変更できません。");
        TenantId = tenantId;
    }
}
