namespace backend.Authorization;

// APIアクションを権限管理の対象として宣言する。起動時に PermissionActionSync がこの属性を走査し、
// PermissionActions テーブルへ同期する(管理画面の権限一覧がコードと常に一致するようにするため)。
[AttributeUsage(AttributeTargets.Method)]
public sealed class PermissionKeyAttribute(string key, string displayName) : Attribute
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;
}
