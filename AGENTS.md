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

## 変更後の確認

以下のコマンドはリポジトリ直下から実行する。

- フロントエンド変更時: npm --prefix frontend run check
- バックエンド変更時: dotnet build backend
- 配備構成の変更時: dotnet publish backend -c Release
- 完了報告には確認結果と、実行できなかった確認を記載する。
