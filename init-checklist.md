# GitHub リポジトリ セキュリティ 初期設定チェックリスト

## チェック方針

- **対象**：新規リポジトリを安全な状態で作成するための初期設定
- **目的**：リポジトリ作成のたびに設定漏れが生じないよう、デフォルト設定とテンプレートで自動化する
- **構成**：Part 1・2 は一度だけ実施する準備作業、Part 3 が毎回の作成手順
- **前提**：個人 Free プランを想定。Private リポジトリでは Secret scanning・Code scanning は利用不可

---

## 確認記録

| 項目 | 内容 |
|------|------|
| 設定日 | 2026/4/5/ |
| GitHub アカウント名 | gbz3 |
| 設定者 | 本人 |

---

## Part 1：アカウントのデフォルト設定（一度だけ実施）

新規リポジトリ作成時に自動適用される設定。

**右上アイコン → Settings → Code security**

### Dependabot（Public / Private 両方に有効）

- [x] `Dependency graph` → **Enable all** をクリックした
  - ダイアログで `Enable by default for new repositories` にチェックを入れた
- [x] `Dependabot alerts` → **Enable all** をクリックした
  - ダイアログで `Enable by default for new repositories` にチェックを入れた
- [x] `Dependabot security updates` → **Enable all** をクリックした
  - ダイアログで `Enable by default for new repositories` にチェックを入れた

### Secret scanning / Code scanning（Public リポジトリのみ・設定不要）

> Public リポジトリではデフォルトで自動 ON のため、アカウントのデフォルト設定から操作する項目はない。
> 個人プラン（Free / Pro）では Private リポジトリへの適用不可。

- [x] 上記を確認・認識した（設定操作は不要）

---

## Part 2：テンプレートリポジトリの作成（一度だけ実施）

Branch protection rules など、デフォルト設定では自動適用できない設定を新規リポジトリに引き継ぐためのテンプレート。

### 2-1. テンプレートリポジトリの準備

- [x] テンプレート用リポジトリを新規作成した（例: `template-default`）
- [x] **Settings → General → Template repository** にチェックを入れた

### 2-2. Branch protection rules（Ruleset）の設定

**テンプレートリポジトリの Settings → Rules → Rulesets → New ruleset**

- [x] Ruleset を新規作成した
  - Ruleset name: `protect-main`（任意）
  - Enforcement status: `Active`
- [x] `Target branches` に `main`（または `master`）を追加した
- [x] `Restrict deletions` を有効にした（デフォルトブランチの削除を防止）
- [x] `Require a pull request before merging` を有効にした（直接プッシュを禁止）
- [x] `Block force pushes` を有効にした（履歴の強制書き換えを防止）

### 2-3. 不要な機能の無効化

**テンプレートリポジトリの Settings → General → Features**

- [x] 使用しない機能を OFF にした
  - `Wikis`：使わない場合は OFF
  - `Projects`：使わない場合は OFF
  - `Discussions`：使わない場合は OFF

---

## Part 3：新規リポジトリ作成時の手順

テンプレートを使って作成することで、Part 2 の設定が自動的に引き継がれる。

- [x] GitHub トップ → `New repository` をクリックした
- [x] `Repository template` で `template-default` を選択した
- [x] リポジトリ名・可視性（Public / Private）を設定した
- [x] `Create repository` をクリックした
- [x] Part 1 のデフォルト設定（Dependabot 等）が自動適用されていることを確認した
- [ ] Branch protection rules（Ruleset）が引き継がれていることを確認した
  - **Settings → Rules → Rulesets** で `protect-main` が存在することを確認
- [x] Actions の SHA ピンニングを有効にした
  - **Settings → Actions → General → Actions permissions** で `Require actions to be pinned to a full-length commit SHA` を ON にした
- [x] Dependabot for Actions を設定した（SHA ピンニングと組み合わせて最新 SHA に自動更新）
  - `.github/dependabot.yml` をリポジトリに追加した
    ```yaml
    version: 2
    updates:
      - package-ecosystem: github-actions
        directory: /
        schedule:
          interval: weekly
    ```
- [x] ワークフローの `GITHUB_TOKEN` 権限を最小化した
  - **Settings → Actions → General → Workflow permissions** で `Read repository contents and packages permissions` を選択した
  - 各ワークフローファイルにも `permissions: contents: read` を明示した
- [ ] Environment protection rules を設定した（本番デプロイがある場合）
  - **Settings → Environments** で対象 Environment に `Required reviewers` を設定した
  - ※ Public リポジトリのみ利用可（Private は GitHub Pro 以上が必要）

---

## 設定範囲サマリー

| 設定内容 | 設定方法 | Public | Private |
|----------|----------|:------:|:-------:|
| Dependency graph | Part 1 デフォルト設定 | ✅ | ✅ |
| Dependabot alerts | Part 1 デフォルト設定 | ✅ | ✅ |
| Dependabot security updates | Part 1 デフォルト設定 | ✅ | ✅ |
| Secret scanning + Push protection | Public は自動 ON・設定不要 | ✅ | ❌ |
| Code scanning（CodeQL） | Public は自動 ON・設定不要 | ✅ | ❌ |
| Branch protection rules | Part 2 テンプレート | ✅ | ✅ |
| 不要な機能の無効化 | Part 2 テンプレート | ✅ | ✅ |
| Actions SHA ピンニング | Part 3 手動設定 | ✅ | ✅ |
| Dependabot for Actions | Part 3 手動設定 | ✅ | ✅ |
| GITHUB_TOKEN 権限最小化 | Part 3 手動設定 | ✅ | ✅ |
| Environment protection | Part 3 手動設定 | ✅ | ❌ |
