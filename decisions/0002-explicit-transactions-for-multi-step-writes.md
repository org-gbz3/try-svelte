# 0002. 複数保存ポイントを持つDB書き込みの明示的トランザクション化

## 状態

採用

## 背景

最初の管理者アカウントをブートストラップする `AdminBootstrap.RunAsync`([decisions/0001](0001-role-based-authorization-design.md)
で導入)で、ユーザー作成後にロール作成・権限付与が何らかの理由で失敗すると、一部だけコミット済みの中途半端な状態が残り、
次回起動時の冪等性チェック(「管理者ロールが既にあるか」)が誤って「既に完了している」と判定してしまい、二度とユーザー作成が
リトライされなくなる不具合が実際に発生した。

原因は、ASP.NET Core Identity の `UserManager`/`RoleManager` の各メソッド(`CreateAsync`・`AddToRoleAsync` 等)が呼び出しごとに
内部で `SaveChangesAsync()` を自動実行するため(既定で `AutoSaveChanges = true`)、1つの論理操作の中でこれらを複数回呼ぶと、
何もしなければ暗黙的に別々のトランザクションに分かれてしまうことにある。

このバグを踏まえ、`backend/Controllers`・`backend/Authorization` 配下でDBに書き込む全11メソッドを調査したところ、複数の
保存ポイント(Identity Managerの呼び出し・`SaveChangesAsync` の合計)を持つのは `AdminBootstrap.RunAsync`(最大5箇所)と
`UserRolesController.Set`(ロールの追加・削除で最大2箇所)の2つのみで、残り9メソッド(`AuthController` の各エンドポイント、
`RolesController` の Create/Rename/Delete/SetPermissions、`PermissionActionSync.RunAsync`)は元々1回の `SaveChangesAsync` で
完結しており、EF Core の暗黙トランザクションだけで既にアトミックだった。

## 検討した選択肢

- 案A: DBへの書き込み処理では常に明示的にトランザクションを定義する
  - 長所: ルールが単純で覚えやすく、将来の実装でも一律に安全側に倒せる。
  - 短所: 実態として書き込みメソッドの大多数(11件中9件)は1回の `SaveChangesAsync` で完結しておりEF Coreの暗黙トランザクションで
    既にアトミック。これらにも明示的トランザクションを追加するのは正しさの向上を伴わない定型コードの増加であり、タスクに必要な
    範囲を超えた保険的コードを避けるという本プロジェクトの方針とも整合しない。
- 案B: 暗黙的なトランザクションのまま実装し、Identity に関する特殊事情はソースコードのコメントに記載するだけにする
  - 長所: 既存コードへの変更が最小限で済む。
  - 短所: コメントには強制力がない。実際、この特殊事情を認識していたにもかかわらず `AdminBootstrap.RunAsync` と
    `UserRolesController.Set` の両方で見落としたまま実装してしまった実績があり、コメントだけでは再発を防げないことが
    既に実証されている。
- 案C(採用): 1つの論理操作の中で保存ポイントが2つ以上になる場合に限り、明示的トランザクションを必須とする
  - 長所: 実際にリスクがある箇所だけを機械的に特定できる基準であり、コードレビューでも「この中でManager呼び出しや
    SaveChangesが2回以上あるか」という具体的なチェック項目にできる。単一保存ポイントの操作には冗長なコードを追加しない。
  - 短所: 「2つ以上」の判定を実装者・レビュアーが意識する必要があり、案Aほど画一的ではない。

## 決定

案C を採用する。`AdminBootstrap.RunAsync` と `UserRolesController.Set` を `AuthDbContext.Database.BeginTransactionAsync()` で
明示的に囲むよう修正し、この基準を [AGENTS.md](../AGENTS.md) の「DB更新処理を実装するとき」に実装ルールとして明文化する。

## 理由

案Aは実態(11件中9件が既にアトミック)に対して過剰であり、本プロジェクトが重視する「タスクに必要な範囲を超えた抽象化を
避ける」方針と衝突する。案Bは「コメントによる注意喚起」がこの特殊事情を認識していた実装者自身によって2箇所で見落とされたと
いう具体的な失敗実績があり、保守性の観点で信頼できない。案Cは、実際にリスクが生じる条件(保存ポイントが2つ以上)を明確化する
ことで、過不足のない対応を機械的に判断できるようにする。

## 影響

- `AdminBootstrap.RunAsync`・`UserRolesController.Set` を明示的トランザクションで囲む変更が必要(実施済み)。
- 今後、Identity の Manager 呼び出しや `SaveChangesAsync` を1つの論理操作内で2回以上行う実装を追加する場合は、同様に
  `Database.BeginTransactionAsync()` で囲むことが AGENTS.md 上のルールになる。
- 本プロジェクトは現時点で `EnableRetryOnFailure()`(接続リトライ戦略)を使用していないため、明示的トランザクションを
  素朴な `BeginTransactionAsync`/`CommitAsync` で実装している。将来リトライ戦略を有効にする場合は、`IExecutionStrategy` で
  トランザクション全体を包み直す必要がある(現状は未対応)。
