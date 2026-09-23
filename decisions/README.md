# Decisions（方針・仕様の検討経緯）

方針・仕様について複数の選択肢を比較して判断したときの経緯を、ADR（Architecture Decision Record）として記録するディレクトリ。

- ここにあるのは「なぜその決定に至ったか」という経緯の記録であり、現時点の仕様の正ではない。現時点の仕様は README.md・AGENTS.md・[ASP.NET_Core_Identity.md](../ASP.NET_Core_Identity.md) など既存の文書を参照する。
- トレードオフのある方針判断・複数案から選定したときに ADR を追加する。些末な実装の言い換えなどは対象外。
- 一度採用した ADR の内容は書き換えない。決定が覆った場合は新しい ADR を追加し、旧 ADR の状態を「廃止・置換」にして新旧を相互リンクする。

## ファイル名

`NNNN-slug-in-english.md`（例: `0001-email-confirmation-timing.md`）。番号は4桁の連番、スラッグは英語（既存の規約「識別子は英語、説明コメントは日本語」に合わせる）。本文は日本語で記載する。

## 見出しテンプレート

```markdown
# NNNN. タイトル

## 状態

提案中 / 採用 / 却下 / 廃止・置換（置換先: [NNNN](NNNN-xxx.md)）

## 背景

何を決める必要があったか、前提となる制約。

## 検討した選択肢

- 案A: 長所・短所
- 案B: 長所・短所

## 決定

何を選んだか。

## 理由

なぜその案を選んだか。

## 影響

この決定によるアプリ・運用への影響。見直す条件があれば記載する。
```

## 一覧

| 番号 | タイトル | 状態 |
| --- | --- | --- |
| [0001](0001-role-based-authorization-design.md) | ロールベース認可の実装方式 | 採用 |
| [0002](0002-explicit-transactions-for-multi-step-writes.md) | 複数保存ポイントを持つDB書き込みの明示的トランザクション化 | 採用 |
| [0003](0003-expose-effective-permissions-in-me.md) | /api/auth/me での実効権限マップの返却 | 採用 |
| [0004](0004-admin-user-list-design.md) | 管理者向けユーザー一覧画面の設計 | 採用 |
| [0005](0005-user-list-email-search-options.md) | ユーザー一覧のメール検索オプション(前方一致・大文字小文字区別)の実装方式 | 採用 |
| [0006](0006-password-reset-design.md) | パスワード再設定機能の設計 | 採用 |
| [0007](0007-passkey-authentication-design.md) | パスキー(WebAuthn)認証の設計 | 採用 |
