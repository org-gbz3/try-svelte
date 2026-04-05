# try-svelte

Svelte 学習用のリポジトリです。最小構成のフロントエンド方針と、Copilot で作業しやすい運用ルールをまとめています。

## 目的

- 小さな機能実装を通じて Svelte の基本を学ぶ。
- 依存関係を最小限に保ち、構成を理解しやすくする。
- セキュアで再現可能な開発環境を優先する。

## プロジェクト方針

[docs/project-policy.md](docs/project-policy.md) に、スコープ・実装方針・セキュリティ方針を記載しています。

## クイックスタート

1. Node.js と npm が使えることを確認する。
2. 依存関係をインストールする。
3. 開発サーバーを起動する。

```bash
npm install
npm run dev
```

## Copilot 設定

リポジトリ共通の Copilot 指示は次のファイルで管理しています。

- .github/copilot-instructions.md
- .github/instructions/svelte-learning.instructions.md
- .github/instructions/docs.instructions.md
- .github/prompts/learning-task.prompt.md
