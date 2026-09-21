# 0003. /api/auth/me での実効権限マップの返却

## 状態

採用

## 背景

ロールベース認可([decisions/0001](0001-role-based-authorization-design.md))の実装により、APIアクション単位の権限判定は
バックエンドで完結するようになったが、フロントエンド(SPA)側はログイン中ユーザーがどの権限を持つか一切知らない。

フロントエンドの管理画面(ロール管理)を追加するにあたり、トップ画面のナビゲーションに管理画面へのリンクを表示するかどうかを
判断する必要が生じた。`/api/auth/me` は現状 `{ id, email }` のみを返す。

## 検討した選択肢

- 案A: `/api/auth/me`(および `/api/auth/login`)が、ログイン中ユーザーの全 `PermissionAction` に対する実効権限マップ
  (`permissions: { "Admin.Roles": 2, ... }`)を返す。
  - 長所: 将来 `[PermissionKey]` が増えても `/me` 自体は変更不要。`PermissionAuthorizationHandler` と同じ
    「`UserRoles`→`RolePermissions`→`PermissionActions` を結合し複数ロールの最大値を取る」ロジックを共有クエリに
    切り出せるため、判定ロジックの重複が生じない。
  - 短所: レスポンスに権限キー一覧というやや詳細な情報が含まれる(ただし本人の権限のみで、他ユーザーの情報は含まない)。
- 案B: 個別の真偽値フラグ(例 `canManageRoles: bool`)のみを都度追加する。
  - 長所: 変更が最小限で済み、返すデータも必要最低限。
  - 短所: 権限で出し分けたいナビ項目が増えるたびに `/me` のレスポンス形状とバックエンドの判定コードを個別に追加する
    必要があり、`[PermissionKey]`/`[RequirePermission]` によるアクション単位の権限管理という設計方針と噛み合わない。
- 案C: `/me` は現状維持(`{ id, email }` のみ)とし、フロントエンドは管理系APIを実際に呼んで `403` が返るかどうかで
  権限の有無を判断する。
  - 長所: バックエンド変更が不要。
  - 短所: ナビ表示のためだけに保護されたAPIへ探りの呼び出しを行うことになり、UIの見た目のためにサーバーへの
    無駄なリクエストと失敗ログ(403)が発生する。

## 決定

案Aを採用する。`PermissionAuthorizationHandler` が持つ結合ロジックを `backend/Authorization/PermissionQueries.cs` の
`PermissionQueries.ForUser` に切り出し、`AuthController` の `/login`・`/me` の両方がこれを使って実効権限マップを算出して
返す。

## 理由

案Bは権限キーが増えるたびに個別対応が必要になり、本プロジェクトが権限をアクションキー単位で汎用的に管理する設計
(ADR 0001)と方向性が合わない。案Cは実装コストが最も低いが、UI表示のためだけに保護APIへ探りを入れる呼び出しが発生し、
今後管理画面が増えるほど悪化する。案Aは既存の権限判定ロジックを共有クエリとして再利用でき、フロントエンドは
`/me` の結果だけで必要なナビ出し分けを完結できる。

権限情報は既存方針どおり Cookie には一切載せず、`/me` はリクエストのたびにDBを参照して算出するため、ADR 0001 の
「ロール・権限変更を既存のログインセッションに即時反映する」という制約とは矛盾しない。また、画面側の表示制御は
あくまでUXであり、各APIの `[Authorize]`/`[RequirePermission]` による強制はこれまでどおり独立して維持する
([ASP.NET_Core_Identity.md](../ASP.NET_Core_Identity.md) の「画面のボタンを隠すだけではAPIを保護できない」方針)。

## 影響

- `GET /api/auth/me`・`POST /api/auth/login` のレスポンスに `permissions` フィールドが追加される(既存の `id`・`email`
  フィールドは変更なし)。
- `PermissionAuthorizationHandler` は `PermissionQueries.ForUser` を使うようリファクタしたが、判定結果(複数ロールの
  最大値が要求レベル以上かどうか)は変わらない。
- フロントエンドはこの `permissions` マップをもとにナビゲーションの表示を判断できるが、直接URLを開かれた場合に備えて
  各画面は引き続き保護APIの `403` 応答を「権限不足」として扱う。
