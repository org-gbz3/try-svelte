# try-svelte

SvelteKit(SPA)をビルドして `backend/wwwroot` に配備し、ASP.NET Core(.NET 10)で配信する構成。

## 構成

- `backend/` — ASP.NET Core Web API（Controllers ベース、.NET 10）。`wwwroot` に配置された静的ファイルを配信し、API は `api/` 配下。
- `frontend/` — SvelteKit（`@sveltejs/adapter-static` によるSPAビルド）。ビルド出力は直接 `backend/wwwroot` へ書き出される。

## Dev Container

Codex のサンドボックスで namespace を作成できるよう、コンテナの起動時に
`--security-opt=seccomp=unconfined` を指定している。このコンテナ全体の seccomp 制限が解除される。
設定変更後は VS Code の「Dev Containers: Rebuild Container」で再作成する。

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
