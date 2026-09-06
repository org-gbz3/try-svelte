# try-svelte

SvelteKit(SPA)をビルドして `backend/wwwroot` に配備し、ASP.NET Core(.NET 10)で配信する構成。

## 構成

- `backend/` — ASP.NET Core Web API（Controllers ベース、.NET 10）。`wwwroot` に配置された静的ファイルを配信し、API は `api/` 配下。
- `frontend/` — SvelteKit（`@sveltejs/adapter-static` によるSPAビルド）。ビルド出力は直接 `backend/wwwroot` へ書き出される。

## Dev Container

Docker Compose で開発用の `app` と SQL Server 2025 Developer の `sqlserver` を同時起動する。
SQL Server のヘルスチェックが成功してから開発用コンテナを起動する。
Docker ホストは x86-64 Linux が対象で、SQL Server 用に最低 2 GB のメモリが必要。
Developer エディションは開発・テスト用として使用する。

初回起動前に、リポジトリ直下で設定ファイルを作成する。

```sh
cp .devcontainer/.env.example .devcontainer/.env
```

`.devcontainer/.env` の `MSSQL_SA_PASSWORD` に、大文字・小文字・数字・記号のうち
3 種類以上を含む 8 文字以上のパスワードを設定する。空欄では起動できない。
このファイルは Git 管理対象外。ホストの 1433 番ポートが使用中なら `MSSQL_PORT` を変更する。
その後、VS Code の「Dev Containers: Rebuild Container」で再作成する。
VS Code から開発コンテナを終了すると、Compose のサービスも停止する。

Codex のサンドボックスで namespace を作成できるよう、`app` に
`security_opt: [seccomp:unconfined]` を指定している。この開発用コンテナ全体の seccomp 制限が解除される。

### SQL Server への接続

| 接続元 | サーバー | 認証 |
| --- | --- | --- |
| Docker ホストの DB ツール | `127.0.0.1,1433`（ポートは `MSSQL_PORT`） | SQL Server 認証、ユーザー `sa`、設定したパスワード |
| 開発用コンテナのバックエンド | `sqlserver,1433` | 同上 |

DB ツールにホストとポートの別欄がある場合は、それぞれ `127.0.0.1` と `1433` を指定する。
開発用の自己署名証明書を使うため、接続時は暗号化を有効にし「サーバー証明書を信頼する」を有効にする。
ポートは Docker ホストのループバックアドレスだけに公開する。
リモート Docker ホストを利用する場合は、SSH トンネルなどでそのホストの 1433 番ポートへ接続する。

認証用 DB は `TrySvelte`、ユーザー管理は ASP.NET Core Identity と EF Core を使用する。
初回は以下の「認証 DB の準備」を実行する。コンテナ起動だけでは DB を作成しない。

DB データは名前付きボリューム `sqlserver-data` に保存し、通常の停止やコンテナ再作成では保持する。
既存データがある場合、`.env` の変更だけでは `sa` のパスワードは変更されないため、SQL Server 側で変更する。
データをすべて削除して初期化する場合のみ、Dev Container を終了した後、Docker ホストのリポジトリ直下で実行する。

```sh
docker compose -f .devcontainer/compose.yaml down --volumes
```

次回起動時に空の SQL Server が作成される。

## 開発

2つのターミナルで並行起動する。

```sh
# ターミナル1: backend (http://localhost:5000)
cd backend
dotnet run

# ターミナル2: frontend (http://localhost:5173, /api は backend にプロキシ)
cd frontend
npm run dev
```

## 認証 DB の準備

リポジトリ直下で実行する。接続文字列は Git 管理対象の設定ファイルに保存しない。
以下は開発用の例で、`YOUR_PASSWORD` は `.devcontainer/.env` に設定した値へ置き換える。
パスワードにセミコロンなどを含む場合は、SQL Server 接続文字列の規則に従って値を引用する。

```sh
export ConnectionStrings__AuthDatabase='Server=sqlserver,1433;Database=TrySvelte;User Id=sa;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True'
dotnet tool restore
dotnet ef database update --project backend
```

バックエンドは同じ環境変数を設定したターミナルから起動する。
開発コンテナ外から実行する場合は接続先を `127.0.0.1,1433`（または変更したポート）にする。
環境変数の代わりに .NET User Secrets の `ConnectionStrings:AuthDatabase` も利用できる。
マイグレーションは `backend/Data/Migrations` に管理し、起動時の自動適用は行わない。

## ログイン機能

ASP.NET Core Identity の Cookie 認証を使用する。登録後はログイン画面へ移動する。
パスワードは12〜128文字で、大文字・小文字・数字・記号をそれぞれ含める。
ログイン失敗5回で15分間ロックアウトする。メール確認・パスワード再設定・MFA は未実装。

| API | 動作 |
| --- | --- |
| `GET /api/auth/csrf` | CSRF Cookie と JSON の `token` を取得 |
| `POST /api/auth/register` | `{ email, password }` で登録。成功 `201`、入力不備・登録不可 `400` |
| `POST /api/auth/login` | `{ email, password }` で認証。成功 `200` と `{ id, email }`、認証失敗 `401` |
| `GET /api/auth/me` | 認証済みなら `200` と `{ id, email }`、未認証なら `401` |
| `POST /api/auth/logout` | Cookie を削除し `204` |

更新 API は直前に `/api/auth/csrf` を呼び、返却されたトークンを `X-CSRF-TOKEN` ヘッダーに設定する。
Cookie も同時に送信する。CSRF トークンなし・不正なトークンは `400`。
ログイン前後でトークンの対象ユーザーが変わるため、トークンを使い回さない。
認証 API の応答は `Cache-Control: no-store` を返す。

SPA は起動・再読み込み時に `/api/auth/me` を呼ぶ。確認中は待機表示、通信・サーバーエラー時は
再試行画面を表示し、未ログインとは区別する。ユーザー情報はメモリ上に保持し、localStorage は認証に使用しない。
保護された API は `frontend/src/lib/auth.svelte.ts` の `apiFetch` 経由で呼び出す。
`401` は未ログインへ遷移し、`403` は権限不足として認証状態を維持する。
画面表示とは独立して API ごとに `[Authorize]` で認証する（`/api/weatherforecast` も保護対象）。
存在しない `/api` 配下の URL は SPA の HTML ではなく `404` を返す。

認証 Cookie は HttpOnly・SameSite=Lax、有効期間は8時間、スライディング延長なし。
永続 Cookie は発行しない。本番環境では認証・CSRF Cookie に Secure を必須とするため HTTPS で配信する。
開発時は既存の Vite `/api` プロキシ経由で HTTP を利用できる。
通常のログアウトはそのブラウザーの Cookie を削除する。他端末の一括ログアウトは未実装。

本番では専用の DB ユーザーと検証可能な SQL Server 証明書を使用する。
ASP.NET Core Data Protection の鍵は再起動後も保持し、複数インスタンスの場合は共有する。
TLS をリバースプロキシで終端する場合は、信頼するプロキシを限定して転送ヘッダーを設定する。

## 確認

```sh
npm --prefix frontend run check
npm --prefix frontend run test
dotnet build backend
dotnet test backend.Tests
dotnet publish backend -c Release
```

`dotnet test backend.Tests` は、各テストの確認内容を日本語の表示名で、成否・所要時間とともに出力する。
各テストメソッドは1つの確認観点を扱い、個別のメモリ DB で独立して実行する。登録・ログインの準備処理はヘルパーにまとめる。
詳細ログが必要な場合は `dotnet test backend.Tests --logger "console;verbosity=detailed"` で上書きできる。

統合テストは SQLite のメモリ DB と実際の Identity・Cookie・CSRF 処理を使用し、
登録・重複・入力検証・認証失敗・ロックアウト・ログイン状態取得・ログアウト・API の認証保護を確認する。
SQL Server 固有のマイグレーション適用は、開発用 SQL Server で別途確認する。

## ビルド・配備

```sh
dotnet publish backend -c Release
```

`dotnet publish` 実行時に MSBuild ターゲットが `frontend` で `npm ci && npm run build` を実行し、
ビルド成果物が自動的に `backend/wwwroot` に生成される。
フロントエンドのビルドは .NET の静的ファイル収集より前に完了し、生成後のファイル一覧を収集対象に登録する。
通常の `dotnet build` / `dotnet run` ではフロントエンドをビルドしない。
`dotnet publish --no-build` はフロントエンドも再ビルドしないため、先に通常の `dotnet publish` を実行しておく。

## CSP

CSP は `frontend/vite.config.ts` で設定する。アプリ側のスタイルは CSS クラスに記載する。
SvelteKit が生成する読み上げ通知要素（`svelte-announcer`）の固定インラインスタイルは、
`style-src-attr` の `unsafe-hashes` と SHA-256 ハッシュで限定的に許可する。
SvelteKit 更新時にこのスタイルが変わった場合は、実際のスタイル内容を確認してハッシュを更新する。

## DESINE.md

- [pre-design-md](https://pre-design-md.dev/)
