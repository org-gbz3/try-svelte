# ASP.NET Core Identity のユーザー管理方針

ASP.NET Core Identity を導入するときは、まず「誰が登録でき、何を満たすとログインでき、いつ利用できなくなるか」を決める。設定値だけでなく、必要な画面・API・メール・運用手順をセットで設計する。

この文書は ASP.NET Core 10 を対象とし、一般的な設計上の選択肢と、このリポジトリの現在の実装を区別して記載する。未実装の項目は今後の判断事項であり、この文書の追加によって有効になるものではない。

Identity はユーザー、パスワード、ロール、確認トークンなどを管理する仕組みである。サービスを登録するだけで、このアプリに管理画面・メール配送・退会機能が揃うわけではない。ここでは `UserManager` と `SignInManager` を独自のコントローラーから利用する。[Microsoft Learn: Identity の概要](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)

## 1. 登録対象とアカウントの単位

| 最初に決めること | 選択によるアプリの挙動 | このアプリの現状 |
| --- | --- | --- |
| 公開登録・招待制・管理者作成のどれにするか | 公開登録なら登録画面を公開する。招待制なら招待の期限・対象者・使用済み判定が必要。管理者作成なら初期パスワード設定や初回ログインの案内が必要 | 公開登録 API を提供 |
| メールアドレスとユーザー名を同じにするか | 同じならメールでログインできる。別ならメール変更でログイン名を変えずに済む | `UserName` と `Email` に同じ入力値を設定 |
| 同じメールで複数アカウントを許可するか | 許可するとメールだけではログイン対象や再設定対象を特定できない場合がある | `RequireUniqueEmail = true` |
| 組織ごとに別アカウントを作るか | 組織単位の重複許可や所属切り替えには、テナントを含む検索・一意性・認可の設計が必要 | テナント管理は未実装 |
| 表示名・プロフィールをどこに保存するか | `IdentityUser` の拡張か別テーブルかで、DB の関連とマイグレーションが変わる | 標準の `IdentityUser` を使用 |

業務データとの関連には、変更され得るメールアドレスよりユーザー ID を使う方針を先に決めておく。メールの一意性を Identity の検証に任せるか、DB 制約でも保証するかも検討する。`RequireUniqueEmail` の指定だけでメール列に一意インデックスが追加されるわけではない。[Microsoft Learn: Identity モデルのカスタマイズ](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-10.0)

## 2. ログインを許可する条件

| 最初に決めること | 選択によるアプリの挙動 | このアプリの現状 |
| --- | --- | --- |
| メール確認を必須にするか | 必須なら登録成功後も確認までログインできず、配送失敗・期限切れから復旧する導線が必要 | `RequireConfirmedEmail = true` |
| 管理者承認を必要とするか | 承認待ち状態と承認操作が必要。メールが届くことと利用資格があることは別に判定する | 管理者承認は未実装 |
| 電話番号確認を必要とするか | 電話番号の登録・変更・確認コード配送が必要 | 電話番号確認は未実装 |
| MFA を任意・必須のどちらにするか | パスワード成功後に追加認証へ進む。端末紛失時の復旧手段も必要 | MFA は未実装 |

`RequireConfirmedAccount` を有効にするだけで独自の管理者承認フローが完成するわけではない。承認の意味と判定を設計する。また、ログイン結果は成功・失敗だけでなく、確認条件未達、ロック中、追加認証要求などを考慮する。[Microsoft Learn: Identity の設定](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0)

現在のログイン API は `PasswordSignInAsync` の成功以外を同じ `401` にまとめる。将来 MFA を追加するときは、追加認証が必要な結果を専用フローへ分岐させる実装も必要になる。

## 3. 確認メールとトークン

| 最初に決めること | 選択によるアプリの挙動 | このアプリの現状 |
| --- | --- | --- |
| 有効期限 | 短くすると期限切れと再送が増える。長くすると古いリンクを利用できる期間が延びる | `Email:ConfirmationTokenLifespanMinutes`、既定値1440分 |
| パスワード再設定の有効期限 | 確認メールと同じ考え方だが、悪用の影響が大きいため短めにする判断もある | `Email:PasswordResetTokenLifespanMinutes`、既定値30分。確認メールとは別の名前付きトークンプロバイダーで発行 |
| 確認のタイミング | リンクを開くだけで確定すると、メールスキャナーによるアクセスでも確定する可能性がある | 確認画面のボタンから POST して確定 |
| 配送に失敗した場合 | 登録を取り消すか、未確認ユーザーを残すかで再登録・再送の導線が変わる | 未確認ユーザーを残し、登録 API は `503`。再送で復旧 |
| 再送時の応答 | 状態ごとに応答を変えると、第三者が登録状態を推測できる | 未登録・確認済み・配送失敗も同じ `200` 応答 |
| 再送前のリンクを無効にするか | 「最新の1通だけ有効」にするなら、発行状態や失効の仕組みも設計する | 再送時に古いリンクを明示的に失効する処理はない |
| 未確認ユーザーをいつ削除するか | 削除すると同じメールで再登録できるようになるが、古い確認リンクは対象を失う | 自動削除は未実装 |

このアプリはトークンを URL のフラグメントに入れ、CSRF 対策付き POST で検証する。本文の期限説明にも同じ設定値を使用する。設定変更後は再起動が必要で、新しい期限は送信済みトークンの検証にも適用される。送信済み本文は書き換わらない。

メール確認は標準(既定名)の `DataProtectionTokenProviderOptions.TokenLifespan` を変更し、パスワード再設定は `"PasswordReset"` という名前付きトークンプロバイダーを別途登録して、用途ごとに独立した期限を設定している(`backend/Program.cs`)。設計判断の経緯は [decisions/0006-password-reset-design.md](decisions/0006-password-reset-design.md) を参照する。[Microsoft Learn: 確認・復旧用トークンの有効期限](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/account-confirmation-and-password-recovery?view=aspnetcore-10.0)

## 4. パスワードと不正なログイン試行

| 最初に決めること | 選択によるアプリの挙動 | このアプリの現状 |
| --- | --- | --- |
| 長さと文字種 | 条件に合わない登録・変更を拒否する。画面の案内とサーバーの検証を揃える必要がある | 12〜128文字、大文字・小文字・数字・記号を含む |
| 失敗回数を数えるか | 数える場合はログイン処理で失敗の加算を有効にする必要がある | `lockoutOnFailure: true` |
| ロックの回数と時間 | しきい値に達すると、正しいパスワードでも解除までログインできない | 5回失敗で15分 |
| リクエスト数の制限 | アカウントが存在しない場合なども大量試行を抑えられる。共有 IP の利用者にも影響する | 認証操作を IP ごとに1分20回まで、超過は `429` |
| 失敗理由の見せ方 | 詳細表示は復旧を助ける一方、登録状態などの情報を外部に伝える | ログイン失敗は同じメッセージと `401` |

現在の最低文字数は Identity、最大文字数128は API の入力検証で定めている。文字種の条件は Identity の既定値を利用している。設定を変える場合は登録・変更・再設定の全経路で整合を確認する。[Microsoft Learn: パスワードとロックアウトの設定](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0)

ログイン試行による一時的なロックと、管理者による利用停止は区別して設計する。利用停止を追加する場合は、新規ログインだけでなく、既存のログイン状態で API を使えるかも決める。

## 5. ログイン状態をどう保持・終了するか

| 最初に決めること | 選択によるアプリの挙動 | このアプリの現状 |
| --- | --- | --- |
| Cookie またはトークン方式 | Cookie はブラウザーが送信するため CSRF 対策が必要。トークン方式は保存先・更新・失効も設計する | 同一オリジンの SPA と Cookie 認証 |
| ログイン状態の有効期間 | 短いほど再ログインが増える | 認証チケットは8時間 |
| 操作中に期限を延長するか | 延長ありなら利用中のセッションが継続しやすい。延長なしなら利用中でも期限が来る | `SlidingExpiration = false` |
| ブラウザーを閉じた後も保持するか | 永続 Cookie を使うと再起動後も期限内なら継続できる | `isPersistent: false`。ただしブラウザーのセッション復元に左右されるため、閉じれば必ずログアウトする保証にはしない |
| ログアウトの範囲 | 現在のブラウザーだけか、全端末か、端末を選んで解除するかで実装が変わる | 現在のブラウザーのみ |
| パスワード変更・利用停止をいつ反映するか | 即時反映を求めるなら、既存チケットの失効・再検証の仕組みが必要 | パスワード再設定は Identity の `ResetPasswordAsync` が `SecurityStamp` を自動更新するため、他デバイスも既定の `SecurityStampValidator` 検証間隔(既定30分、未変更)以内に失効する。ただし独自の即時・全端末失効 API は未実装 |

メール確認トークンの期限と Cookie の期限は独立している。確認リンクを30分にしても、ログイン状態が30分になるわけではない。Cookie の期間と永続性は別の設定である。パスワード再設定成功時のセッション失効も同様で、`SecurityStamp` の再検証間隔に依存し、即時ではない(設計判断は [decisions/0006-password-reset-design.md](decisions/0006-password-reset-design.md) を参照)。[Microsoft Learn: Identity の Cookie 設定](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0)

Cookie 認証では、`SameSite` の設定だけに依存せず更新 API に CSRF 対策を適用する。このアプリは直前に `/api/auth/csrf` で取得した値を `X-CSRF-TOKEN` に設定する。[Microsoft Learn: CSRF 対策](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

## 6. 認証後に何を許可するか

「ログインできること」と「そのデータを操作できること」を別々に決める。

| 最初に決めること | 選択によるアプリの挙動 | このアプリの現状 |
| --- | --- | --- |
| ログインだけで利用可能か | `[Authorize]` で認証済み利用者に限定する | `/api/auth/me` を保護。他の保護対象APIはロール・権限による判定に移行(下記) |
| 管理者・一般利用者などのロール | 管理者専用操作にはロール等の認可条件が必要 | ロールベースの認可を実装。`[PermissionKey]`/`[RequirePermission]` でAPIアクション単位に権限キーと必要レベル(`Read`/`Write`)を宣言し、ロールごとの権限レベルを `RolePermissions` テーブルで管理する。詳細は [decisions/0001-role-based-authorization-design.md](decisions/0001-role-based-authorization-design.md) と [README.md の「認可(ロール・権限)」](README.md#認可ロール権限) を参照 |
| 所有者や組織単位の権限 | 他人・別組織の ID を指定してもアクセスできないよう、対象データごとに確認する | 業務上の所有者・組織認可は今後の設計事項 |
| 権限変更の反映時期 | ログイン時点の権限を保持する設計なら、変更後の再評価やチケット更新が必要 | 権限情報をCookieに一切載せず、リクエストのたびにDBを参照して判定するため、ロール・権限の変更は既存のログインセッションに即時反映される |

画面のボタンを隠すだけでは API を保護できない。ロール・ポリシーなどの条件をサーバーでも適用する。このアプリでは未認証を `401`、権限不足を `403` とし、SPA は両者を区別する。この振り分けは Cookie 認証イベント(`OnRedirectToLogin`/`OnRedirectToAccessDenied`)がロールベースの認可でもそのまま適用される。[Microsoft Learn: ロールによる認可](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/secure-net-microservices-web-applications/authorization-net-microservices-web-applications)

## 7. 変更・復旧・退会と外部ログイン

パスワードを忘れた場合の再設定は実装済み(下記)。それ以外は未実装。運用を始める前に、必要な範囲と利用者の復旧手段を決める。

| 方針として決めること | 必要になる挙動・実装 | このアプリの現状 |
| --- | --- | --- |
| パスワードを忘れた場合 | 再設定依頼、期限付きトークン、入力画面、登録状態を公開しない応答、再設定後のセッションの扱い | 実装済み。`/forgot-password` で依頼、`/reset-password` で再設定。確認メールと同じフラグメント配布・CSRF付きPOST・非開示応答の方針。詳細は [README.md の「パスワード再設定」](README.md#パスワード再設定)、経緯は [decisions/0006-password-reset-design.md](decisions/0006-password-reset-design.md) を参照 |
| メールアドレス変更 | 新アドレスの確認、一意性検証、変更完了までの旧アドレスの扱い。このアプリではログイン名もメールなので `UserName` の更新方針も必要 | 未実装 |
| MFA 端末を紛失した場合 | 回復コード等の代替手段、本人確認を伴うサポート手順 | 未実装 |
| 利用停止と退会 | 新規ログインと既存セッションの拒否、データの削除・保持・匿名化、同じメールでの再登録可否 | 未実装 |
| Google などの外部ログイン | 外部アカウントとの関連付け、同じメールの既存ユーザーとの統合条件、外部サービスが使えない場合の復旧 | 未実装 |
| 管理者による操作 | 最初の管理者は `Admin:Bootstrap:Email`/`Admin:Bootstrap:Password` の設定により起動時に自動作成する(README.mdの「最初の管理者のブートストラップ」参照)。`Admin.Roles` への `Write` 権限を持つロールが既にあれば何もしない。権限を付与できる人は「ロール管理API(`Admin.Roles`/`Admin.UserRoles`)への `Write` 権限を持つこと」で定義済み(特別な管理者ロール名はない)。操作履歴(監査ログ)・誤操作からの復旧は未実装 | 部分実装(ブートストラップのみ) |

外部ログインなどは Identity が扱える機能だが、独自 API を使うこのアプリには、それぞれの操作経路と画面を追加する必要がある。[Microsoft Learn: Identity が扱う機能](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)

## 8. DB・鍵・配送をどう運用するか

| 最初に決めること | 選択によるアプリの挙動 | このアプリの現状・必要な対応 |
| --- | --- | --- |
| DB の更新方法 | 自動適用か事前適用かで、配備時の権限と失敗時の復旧が変わる | SQL Server と EF Core。マイグレーションは事前適用 |
| Data Protection の鍵の保存先 | 鍵を失うと既存 Cookie や確認トークンを検証できなくなることがある | 本番では永続化が必要。`Program.cs` に保存先の明示設定はない |
| 複数インスタンスでの鍵共有 | 鍵等の保護設定が揃わないと、アクセス先によって認証に失敗し得る | 本番の構成に合わせて共有先とアプリ識別を設定する |
| 配送失敗への対応 | リクエスト内で配送を待つ方式はその場で失敗を検出できる。キューを使う場合は再試行・重複送信・配送状態を管理する | リクエスト内で SMTP 送信完了を待機、最大30秒、自動再試行・キューなし |
| 秘密情報とログ | 調査に必要な記録範囲と、個人情報・秘密の露出を決める | API キー等は環境変数／User Secrets。SMTP 詳細例外は Development のみ |

鍵の保存先を明示するときは、保存場所へのアクセス制御と保存時の鍵保護も検討する。ユーザー DB のバックアップだけで Cookie・トークンの検証環境まで復旧できるとは限らない。[Microsoft Learn: Data Protection の構成](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)

## このアプリでの利用者の流れ

1. メールとパスワードで登録する。入力や登録条件を満たさなければ `400`。
2. ユーザーを作成して確認メールを送る。成功は `201`、送信失敗はユーザーを残して `503`。
3. 未確認の間は正しいパスワードでもログインできない。メールが届かなければ再送する。
4. メールのリンクを開き、確認画面でボタンを押す。有効なら `204`、無効・期限切れなら `400`。
5. 改めてログインすると Cookie を発行する。メール確認だけではログイン状態にならない。
6. SPA は `/api/auth/me` でログイン状態を取得し、保護された API を利用する。
7. Cookie の期限切れやログアウト後は再ログインする。失敗回数がしきい値に達した場合はロック解除を待つ。

上記は現在の [AuthController.cs](backend/Controllers/AuthController.cs) と [Program.cs](backend/Program.cs) の実装に基づく。API の詳細・設定手順・起動コマンドは [README.md](README.md)、変更時のルールは [AGENTS.md](AGENTS.md) を参照する。

## 方針を変更するときの確認事項

- 新規登録者だけでなく、既存の未確認ユーザー・ログイン中のユーザー・送信済みリンクへの影響を確認する。
- 設定値、API の判定、画面・メールの説明、テストを一緒に更新する。
- 成功時に加え、未確認・期限切れ・改ざん・ロック・配送失敗・CSRF 不正・権限不足を確認する。
- 新機能を実装しない場合も、利用者が困ったときの対応を決める。特に再設定・メール変更・MFA 復旧・退会は、Identity のサービス登録だけでは利用可能にならない。
