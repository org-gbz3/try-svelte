# プロジェクト方針

- SvelteKit の SPA を ASP.NET Core から配信する構成を維持する。
- API は /api 配下に配置する。
- 実装とドキュメントを一致させ、構成変更時は README.md も更新する。

## 作業前に読む文書

- 開発・ビルド手順は README.md を参照する。
- frontend 配下を変更する前に、frontend/AGENTS.md が存在する場合は読む。
- backend 配下を変更する前に、backend/AGENTS.md が存在する場合は読む。
- 方針・仕様の変更に着手する前に、関連する過去の検討経緯が decisions/ にないか確認する。
- DBエンティティ・APIのCRUD操作・対応する画面を変更する前に、[docs/features/](docs/features/) の該当ファイルを確認する。

## 共通コーディング規約

- 識別子は英語、説明コメントは日本語で記載する。
- 既存の命名とファイル構成に合わせる。
- コメントには処理の説明より、判断の理由を記載する。
- backend/wwwroot のビルド生成物は直接編集せず、frontend のソースを変更して再生成する。

## 認証・API を変更するとき

- ASP.NET Core Identity の Cookie 認証を使用し、フロントエンドの認証情報を localStorage に保存しない。
- 保護する API には画面側の制御とは独立して `[Authorize]` を適用する。
- フロントエンドから保護された API を呼ぶときは `frontend/src/lib/auth.svelte.ts` の `apiFetch` を使用する。`401` は未認証、`403` は権限不足として区別する。
- 更新 API は CSRF 対策を維持し、呼び出し直前に `/api/auth/csrf` で取得したトークンを `X-CSRF-TOKEN` に設定して Cookie とともに送る。ログイン前後でトークンを使い回さない。
- 認証 API の応答は `Cache-Control: no-store` とし、存在しない `/api` 配下の URL に SPA の HTML を返さない。
- メール確認トークンは Identity で生成・検証する。リンクのフラグメントに格納し、確認は CSRF 対策付き POST で行う。GET で確認済みにしない。
- 確認トークンの有効期限とメール本文の説明には、同じ `Email:ConfirmationTokenLifespanMinutes` の設定値を使用する。
- 確認メールの再送応答から、未登録・確認済みなどのアカウント状態を判別できないようにする。

## DB更新処理を実装するとき

- ASP.NET Core Identity の `UserManager`/`RoleManager` の各メソッド(`CreateAsync`・`AddToRoleAsync` 等)は呼び出しごとに
  内部で `SaveChangesAsync()` を自動実行する。1つの論理操作の中でこれらの呼び出しや `SaveChangesAsync()` を2回以上行う場合は、
  `AuthDbContext.Database.BeginTransactionAsync()` で明示的に囲み、途中で失敗したときに全体がロールバックされるようにする。
  1回の `SaveChangesAsync()` で完結する操作(複数エンティティの追加・削除でも1回にまとめられるもの)には不要で、
  EF Core の暗黙トランザクションで既にアトミック。経緯は [decisions/0002](decisions/0002-explicit-transactions-for-multi-step-writes.md) を参照する。

## 設定・データ・配備を変更するとき

- 実際のパスワード・API キー・秘密を含む接続文字列は Git 管理対象のファイルに保存せず、環境変数または User Secrets を使用する。設定例にはプレースホルダーを使う。
- パスワードや接続設定全体はログに出さない。SMTP の詳細例外は Development 環境に限定し、API 応答には含めない。
- EF Core のマイグレーションは `backend/Data/Migrations` に管理し、アプリ起動時の自動適用は行わない。
- 本番の認証・CSRF Cookie の Secure 設定と HTTPS 配信を維持する。Data Protection の鍵は永続化し、複数インスタンスでは共有する構成にする。
- 通常の `dotnet build` / `dotnet run` はフロントエンドをビルドしない。配備確認には通常の `dotnet publish backend -c Release` を使用し、`--no-build` で再生成を省略しない。
- CSP は `frontend/vite.config.ts` で管理し、アプリのスタイルには CSS クラスを使う。SvelteKit 更新で `svelte-announcer` のインラインスタイルが変わった場合は、実際の内容に合わせて許可ハッシュを更新する。

## DBエンティティ・画面のCRUDを変更するとき

- エンティティ(`backend/Data`)、コントローラーのCRUD操作、または対応するフロントエンド画面を変更した場合は、`docs/features/` の該当ファイル(ER図・画面操作とCRUD対応表)も同じ変更の中で更新する。
- 新しい権限キードメイン(新しいプレフィックス)を追加した場合は `docs/features/` に新しいファイルを追加し、[docs/README.md](docs/README.md) の一覧表にも追記する。

## 方針・仕様を決定するとき

- トレードオフのある方針判断や、複数案から選定したときは、経緯を [decisions/](decisions/README.md) に ADR として追加する。些末な実装の言い換えは対象外。
- 既存の決定を覆す場合は新しい ADR を追加し、旧 ADR の内容は書き換えずに状態を「廃止・置換」にして新旧を相互リンクする。
- ADR は経緯の記録であり、現時点の仕様の正ではない。現時点の仕様は README.md や ASP.NET_Core_Identity.md など既存の文書側に反映する。

## テストを追加・変更するとき

- バックエンドの各テストには、確認内容を表す日本語の表示名を付ける。
- 各テストメソッドは1つの確認観点を扱い、DB を使うテストは個別のメモリ DB で独立させる。登録・ログインなどの準備処理はヘルパーにまとめる。
- 認証の統合テストでは SQLite のメモリ DB と実際の Identity・Cookie・CSRF 処理を使用する。SQL Server 固有のマイグレーション適用は開発用 SQL Server で別途確認する。

## 変更後の確認

以下のコマンドはリポジトリ直下から実行する。

- フロントエンド変更時: npm --prefix frontend run check
- バックエンド変更時: dotnet build backend
- フロントエンドの動作・テスト変更時: npm --prefix frontend run test
- バックエンドの動作・テスト変更時: dotnet test backend.Tests
- 配備構成の変更時: dotnet publish backend -c Release
- 完了報告には確認結果と、実行できなかった確認を記載する。
