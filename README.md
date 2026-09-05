# try-svelte

SvelteKit(SPA)をビルドして `backend/wwwroot` に配備し、ASP.NET Core(.NET 10)で配信する構成。

## 構成

- `backend/` — ASP.NET Core Web API（Controllers ベース、.NET 10）。`wwwroot` に配置された静的ファイルを配信し、API は `api/` 配下。
- `frontend/` — SvelteKit（`@sveltejs/adapter-static` によるSPAビルド）。ビルド出力は直接 `backend/wwwroot` へ書き出される。

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
