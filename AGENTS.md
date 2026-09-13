# プロジェクト方針

- SvelteKit の SPA を ASP.NET Core から配信する構成を維持する。
- API は /api 配下に配置する。
- 実装とドキュメントを一致させ、構成変更時は README.md も更新する。

## 作業前に読む文書

- 開発・ビルド手順は README.md を参照する。
- frontend 配下を変更する前に、frontend/AGENTS.md が存在する場合は読む。
- backend 配下を変更する前に、backend/AGENTS.md が存在する場合は読む。

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

## 設定・データ・配備を変更するとき

- 実際のパスワード・API キー・秘密を含む接続文字列は Git 管理対象のファイルに保存せず、環境変数または User Secrets を使用する。設定例にはプレースホルダーを使う。
- パスワードや接続設定全体はログに出さない。SMTP の詳細例外は Development 環境に限定し、API 応答には含めない。
- EF Core のマイグレーションは `backend/Data/Migrations` に管理し、アプリ起動時の自動適用は行わない。
- 本番の認証・CSRF Cookie の Secure 設定と HTTPS 配信を維持する。Data Protection の鍵は永続化し、複数インスタンスでは共有する構成にする。
- 通常の `dotnet build` / `dotnet run` はフロントエンドをビルドしない。配備確認には通常の `dotnet publish backend -c Release` を使用し、`--no-build` で再生成を省略しない。
- CSP は `frontend/vite.config.ts` で管理し、アプリのスタイルには CSS クラスを使う。SvelteKit 更新で `svelte-announcer` のインラインスタイルが変わった場合は、実際の内容に合わせて許可ハッシュを更新する。

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
