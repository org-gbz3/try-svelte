namespace backend.Data;

// 権限キーを判定する範囲。システムロール(運営者)とテナントロールで同じキーを混在させないために区別する。
public enum PermissionScope
{
    System = 0,
    Tenant = 1
}
