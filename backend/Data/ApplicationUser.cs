using Microsoft.AspNetCore.Identity;

namespace backend.Data;

// 標準 IdentityUser に登録日時を追加したアプリ固有のユーザー型。一覧画面の登録日時ソートに必要。
public class ApplicationUser : IdentityUser
{
    // 既存ユーザーはマイグレーション時点で NULL(登録日時不明)のまま。捏造した値は入れない(decisions/0004参照)。
    // SQLite プロバイダが ORDER BY での DateTimeOffset を扱えないため、常にUTCのDateTimeとして保持する。
    public DateTime? CreatedAt { get; set; }
}
