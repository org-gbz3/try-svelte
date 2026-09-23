# 0008. パスワード認証へのTOTP方式MFA追加

## 状態

採用

## 背景

パスワード認証にMFA(多要素認証)を追加したいという要望があった。`README.md`・`ASP.NET_Core_Identity.md` にはこれまで「MFA は未実装」「MFA端末を紛失した場合の復旧手段(回復コード等)も未実装」と記載されていた。

[decisions/0007](0007-passkey-authentication-design.md)(パスキー認証)は「パスキーはパスワードに並ぶ代替の主認証手段として実装し、MFAとしては実装しない」と決定している。その理由は「Identityのパスキー実装に2FA向けの組み込みサポートがなく、独自に『パスワード成功→追加認証待ち』の状態管理を実装する必要があり、実装・UXの複雑さが大きく増す」というものだった。

しかし、この制約はパスキー固有のものであり、パスワード+TOTP(認証アプリ)の組み合わせには当てはまらない。調査の結果、`SignInManager<TUser>.PasswordSignInAsync` は `TwoFactorEnabled=true` のユーザーに対して `SignInResult.RequiresTwoFactor` を返し、その際Identityは自動的に一時的な2FA保留Cookie(`IdentityConstants.TwoFactorUserIdScheme`)を発行する。この状態は `SignInManager.GetTwoFactorAuthenticationUserAsync()` で参照でき、`TwoFactorAuthenticatorSignInAsync`/`TwoFactorRecoveryCodeSignInAsync` で本サインインを完了できる。つまり「パスワード成功→追加認証待ち」の状態管理はIdentity標準機能としてすでに用意されており、独自実装は不要だった。TOTP用のトークンプロバイダー(`AuthenticatorTokenProvider<TUser>`)も `AddDefaultTokenProviders()` により追加のNuGetパッケージなしで既に利用可能。リカバリーコードも `GenerateNewTwoFactorRecoveryCodesAsync`/`RedeemTwoFactorRecoveryCodeAsync` がIdentity標準で用意されている。

ユーザーとの事前合意事項:
- 方式はTOTP(認証アプリ)のみ。メールOTPは実装しない。
- 有効化は全ユーザー任意のセルフサービス(パスキーの `/settings/passkeys` と同じ運用)。管理者による強制はスコープ外。
- 端末記憶(trusted device skip)機能は含めない。毎回コードを要求する。

## 検討した選択肢

### 1. ログインAPIが追加認証要求をどう表現するか

- 案A: 従来の失敗と同じ `401` を返し、レスポンスの `message` 文字列で判別させる。
  - 長所: 既存の「非成功はすべて401」という単純な構造を維持できる。
  - 短所: `frontend/src/lib/auth.svelte.ts` の `apiFetch` は401受信時に `user = null; status = 'anonymous'` にする副作用を持つ。パスワードが正しいことは証明済みであり「未認証」ではないため、この副作用は意味的に誤りで、フロントエンド側での特別な握り消しが必要になる。
- 案B: `200 OK` + `{ requiresTwoFactor: true }` を返す。
  - 長所: 401の副作用と衝突しない。「ログインは成功していないが失敗でもない」という第三の状態を素直に表現できる。
  - 短所: 呼び出し側は `response.ok` だけでなくボディも見る必要がある(既存の `responseError` パターンからの小さな逸脱)。

### 2. リカバリーコードでのログインに対応するか

- 案A: 対応する(`TwoFactorRecoveryCodeSignInAsync` を使う専用エンドポイントを追加)。
  - 長所: `ASP.NET_Core_Identity.md` が課題として挙げていた「MFA端末紛失時の復旧手段」に対する自己完結的な回答になる。Identity標準機能を使うため実装コストが低い。
  - 短所: リカバリーコードの発行・再生成・残数表示のUIが追加で必要になる。
- 案B: 対応しない(端末紛失時は都度サポート対応とする)。
  - 長所: 実装範囲を最小化できる。
  - 短所: 端末紛失=ロックアウトとなり、復旧手段が未実装のまま残る。

### 3. MFA無効化・リカバリーコード再生成時の再認証方法

- 案A: 現在のパスワードで再認証する(`SignInManager.CheckPasswordSignInAsync`)。
  - 長所: 認証アプリを紛失していても無効化できる。既存のロックアウト機構(`Lockout.MaxFailedAccessAttempts`)がそのまま適用される。
  - 短所: パスワードを忘れている場合は無効化できない(ただしパスワード再設定([decisions/0006](0006-password-reset-design.md))で復旧可能)。
- 案B: 現在のTOTPコードで再認証する。
  - 長所: 「MFAを解除するには第二要素も必要」という一貫した強度になる。
  - 短所: 認証アプリを紛失した場合、無効化そのものができなくなるデッドロックに陥る。

### 4. パスキーとの相互作用

- 案A: 現状維持。パスキーログイン(`PasskeySignInAsync`)は2要素判定の経路を通らないため、MFAが有効でもTOTPを要求しない。設定画面でその旨を明示する。
  - 長所: [decisions/0007](0007-passkey-authentication-design.md) の決定と矛盾しない。追加実装が不要。
  - 短所: パスキーとパスワード+MFAの両方を有効にしたユーザーにとって、パスキーログインが結果的にMFAをバイパスする経路になる。
- 案B: パスキーログインにも2要素相当のチェックを追加する。
  - 長所: 認証強度の一貫性が保てる。
  - 短所: 0007が「複雑さが増す」として避けた独自の状態管理を、結局実装することになる。今回の要望(パスワード認証へのMFA追加)のスコープを超える。

### 5. QRコード生成方式

- 案A: クライアント側(npmパッケージ `qrcode`)で `otpauth://` URIから生成する。
  - 長所: `frontend/vite.config.ts` のCSP(`img-src: self, data:`)を変更せずに実現できる。バックエンドに画像生成用のNuGet依存を追加しない。
  - 短所: フロントエンドの依存が1つ増える。
- 案B: サーバー側(NuGetパッケージ)でQR画像を生成し `data:` URIとして返す。
  - 長所: フロントエンドの依存を増やさない。
  - 短所: バックエンドに新規NuGet依存が増える。画像エンコード処理をAPIに持たせる必要がある。

## 決定

1. ログインAPIは `200 OK` + `{ requiresTwoFactor: true }` で追加認証要求を表現する(案B)。
2. リカバリーコードでのログインに対応する(案A)。
3. MFA無効化・リカバリーコード再生成は現在のパスワードで再認証する(案A)。
4. パスキーとMFAの関係は現状維持とし、パスキーログインはMFA非対象のまま、設定画面(`/settings/mfa`)にその旨を明示する(案A)。
5. QRコードはクライアント側(`qrcode` パッケージ)で生成する(案A)。

## 理由

各選択肢の長所・短所は上記の通り。特に3点目は「認証アプリを紛失した場合に無効化できなくなる」という具体的なデッドロックを避けるための決定であり、4点目は0007の決定を覆さずに整合させるための決定である。

## 影響

- 新規マイグレーションは不要。`TwoFactorEnabled` は既存の `AspNetUsers` 列、`AuthenticatorKey`/リカバリーコードはIdentity標準の `AspNetUserTokens` テーブルに格納される。
- `backend/Program.cs` に `Tokens.AuthenticatorIssuer` の設定と、2FA保留Cookie(`IdentityConstants.TwoFactorUserIdScheme`)の明示的な設定を追加。
- `backend/Controllers/AuthController.cs` に7エンドポイントを追加: `POST login/verify-2fa`、`POST login/verify-recovery-code`、`GET mfa/status`、`POST mfa/setup`、`POST mfa/enable`、`POST mfa/disable`、`POST mfa/recovery-codes`。
- `frontend/src/routes/settings/mfa/+page.svelte` を新設。`frontend/src/routes/login/+page.svelte` にTOTP/リカバリーコード入力ステップを追加。
- `frontend/package.json` に依存 `qrcode` を追加。
- パスキーとMFAを両方有効にしたユーザーは、パスキーログイン時にTOTPコードを要求されない。将来この挙動を変更する場合は、この決定を見直し新しいADRを追加した上で本ADRを「廃止・置換」にする。
