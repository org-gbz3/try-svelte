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

現時点ではアプリ用 DB・ログイン用テーブル・バックエンドの接続設定は作成しない。
後続のログイン機能実装で追加する。

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
