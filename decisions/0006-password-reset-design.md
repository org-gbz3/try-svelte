# 0006. パスワード再設定機能の設計

## 状態

採用

## 背景

[ASP.NET_Core_Identity.md](../ASP.NET_Core_Identity.md) の「7. 変更・復旧・退会と外部ログイン」で、パスワードを忘れた場合の再設定は未実装として、必要な要素(再設定依頼・期限付きトークン・入力画面・登録状態を公開しない応答・再設定後のセッションの扱い)が挙げられていた。既存のメール確認(`confirm-email`)フローが同種の設計(フラグメント経由のトークン配布・CSRF付きPOSTでの確定・非開示応答・IPベースのレート制限)を既に実装しており、これを踏襲しつつ、以下3点は確認メールにはない固有のトレードオフとして判断が必要だった。

## 検討した選択肢

### 1. リセット成功時に `EmailConfirmed` を更新するか

- 案A: 変更しない。リセットはパスワードのみの操作とする。
  - 長所: 責務が単純。既存のメール確認フローと完全に独立。
  - 短所: 登録直後に確認メールを紛失したなど、メール未確認のアカウントはパスワードをリセットしてもログイン不可(`RequireConfirmedEmail = true`)のまま残り、復旧経路にならない。
- 案B: リセット成功時に `EmailConfirmed` も `true` にする。
  - 長所: 再設定メールのリンクを開けたこと自体が、確認メールのリンクを開くのと同水準のメールアドレス所有の証明になる。未確認アカウントも復旧できる。
  - 短所: 「パスワード再設定」と「メールアドレス確認」という2つの意味を1つの操作に持たせることになる。

### 2. トークンの有効期限をどう設定するか

- 案A: メール確認と同じ既定のトークンプロバイダー(`Email:ConfirmationTokenLifespanMinutes`、1440分)を流用する。
  - 長所: 追加の設定・実装が不要。
  - 短所: パスワード再設定トークンは悪用時の影響(アカウント乗っ取り)がメール確認トークンより大きいにもかかわらず、24時間という長い期限を共有することになる。
- 案B: 専用の名前付きトークンプロバイダーを新設し、より短い独自の有効期限を設定する。
  - 長所: リスクに応じた期限を個別に調整できる。`ASP.NET_Core_Identity.md` が将来の検討事項として既に示唆していた方向性と一致する。
  - 短所: `Program.cs` に名前付きトークンプロバイダーの登録・オプション設定が増える。

### 3. リセット成功後、他デバイス・他セッションの無効化をどう扱うか

- 案A: 既定の `SecurityStampValidator` 検証間隔(既定30分、未変更)による自動失効に任せる。
  - 長所: 追加実装が不要。ASP.NET Core Identity の `ResetPasswordAsync` は呼び出すだけで `SecurityStamp` を自動更新するため、他デバイスのCookieは次回の検証タイミングで自動的に無効になる。
  - 短所: 検証間隔の分だけ、リセット前のセッションが有効なまま残る(最大で既定30分程度の遅延)。
- 案B: `SecurityStampValidatorOptions.ValidationInterval` を短縮する設定を追加する(例: 5分)。
  - 長所: 他セッションをより早く失効させられる。
  - 短所: 全リクエストで検証頻度が上がりDB負荷が増える。ロールベース認可の設計([decisions/0001](0001-role-based-authorization-design.md))で既に、DB参照を都度行う方針を採用しトレードオフを引き受けているため、これ以上検証間隔を詰めることは今回のスコープでは過剰と判断した。

## 決定

- `EmailConfirmed` はリセット成功時に `true` へ更新する(案B)。
- パスワード再設定トークンは `"PasswordReset"` という専用の名前付きトークンプロバイダーを新設し、有効期限は `Email:PasswordResetTokenLifespanMinutes`(既定30分)で独立して設定する(案B)。
- 他セッションの失効は既定の `SecurityStampValidator` 検証間隔による自動失効に任せ、専用の全端末即時失効APIは実装しない(案A)。ブラウザーの現在のセッションのみ、リセット成功時に明示的に `SignOutAsync()` する。

## 理由

- 1点目: メール確認とパスワード再設定は「メールアドレスの実際の受信箱にアクセスできること」を証明する点で本質的に同じ強度の確認であり、別々に確認を求めることは利便性を損なうだけでセキュリティ上の追加効果がない。
- 2点目: パスワード再設定トークンは奪取された場合の被害(アカウント乗っ取り)がメール確認トークンより大きいため、確認メールとは独立してリスクに応じた短い期限を設定できるようにした。
- 3点目: 「利便性よりセキュリティを重視する」という要望はあるが、全リクエストでの検証間隔短縮は既存のロールベース認可の設計判断([decisions/0001](0001-role-based-authorization-design.md))で明示的に避けた方向性(近ゼロの検証間隔はDB負荷とのトレードオフが大きい)と同種であり、`ResetPasswordAsync` による自動的な `SecurityStamp` 更新で一定時間内には確実に失効する既存の仕組みで十分と判断した。

## 影響

- `DataProtectorTokenProvider<TUser>` は名前なしの `IOptions<DataProtectionTokenProviderOptions>` を直接参照するため、名前付きオプションを登録するだけではメール確認用の設定と分離できない。そのため `backend/Services/PasswordResetTokenProvider.cs` に専用のオプション型 `PasswordResetTokenProviderOptions` と専用プロバイダー `PasswordResetTokenProvider` を追加し、`backend/Program.cs` でこれを `"PasswordReset"` という名前でトークンプロバイダーとして登録する。
- `backend/appsettings.json` に `Email:PasswordResetTokenLifespanMinutes` の設定項目が追加される。
- リセット成功時、`ResetPasswordAsync` と(未確認アカウントの場合)`EmailConfirmed` の更新の2回の保存ポイントが1つの論理操作内で発生するため、`AuthDbContext.Database.BeginTransactionAsync()` による明示的トランザクションが必要になる([decisions/0002](0002-explicit-transactions-for-multi-step-writes.md)参照)。
- 他セッションの失効に即時性が必要になった場合(例: 不正利用の疑いがある場合の緊急停止)は、この決定を見直し、専用の全端末失効APIの実装を検討する。
