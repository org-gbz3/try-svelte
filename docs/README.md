# docs(機能別ER図・画面CRUD対応表)

DBで管理するデータ(エンティティ)のテーブル間関係と、画面操作とCRUD操作の対応を、機能ドメイン単位で示す資料。README.md・ASP.NET_Core_Identity.md と同じく現時点の仕様を表す資料であり、decisions/ のような経緯の記録ではない。

権限キー(`[PermissionKey]`)のプレフィックスを機能ドメインの単位とする。新しい権限キードメインを追加した場合は、対応するファイルをこのディレクトリに追加し、下の一覧にも追記する。

エンティティ・コントローラーのCRUD操作・対応する画面のいずれかを変更した場合は、同じ変更の中で該当ファイルも更新する。詳細は [AGENTS.md](../AGENTS.md) の「DBエンティティ・画面のCRUDを変更するとき」を参照。

## 一覧

| ファイル | 機能ドメイン | 対象コントローラー |
| --- | --- | --- |
| [features/auth.md](features/auth.md) | 認証(登録・メール確認・ログイン/ログアウト) | `AuthController` |
| [features/admin-roles.md](features/admin-roles.md) | ロール管理(`Admin.Roles`) | `RolesController` |
| [features/admin-user-roles.md](features/admin-user-roles.md) | ユーザーへのロール割り当て(`Admin.UserRoles`) | `UserRolesController` |
| [features/admin-users.md](features/admin-users.md) | ユーザー一覧(`Admin.Users`) | `UsersController` |

`WeatherForecastController` はDB操作を伴わないデモ用エンドポイントのため対象外。
