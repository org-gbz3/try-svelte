# try-svelte

SvelteKit(SPA)をビルドして `backend/wwwroot` に配備し、ASP.NET Core(.NET 10)で配信する構成。

## 構成

- `backend/` — ASP.NET Core Web API（Controllers ベース、.NET 10）。`wwwroot` に配置された静的ファイルを配信し、API は `api/` 配下。
- `frontend/` — SvelteKit（`@sveltejs/adapter-static` によるSPAビルド）。ビルド出力は直接 `backend/wwwroot` へ書き出される。
- `decisions/` — 方針・仕様を検討した経緯（ADR）。現時点の仕様そのものはこの README や [ASP.NET_Core_Identity.md](ASP.NET_Core_Identity.md) 側に記載し、`decisions/` にはなぜその決定に至ったかを記録する。詳細は [decisions/README.md](decisions/README.md) を参照。

エージェント向けの実装・テスト作成ルールは [AGENTS.md](AGENTS.md) を参照する。

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

認証用 DB は `TrySvelte`、ユーザー管理は ASP.NET Core Identity と EF Core を使用する。
初回は以下の「認証 DB の準備」を実行する。コンテナ起動だけでは DB を作成しない。

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

## 認証 DB の準備

リポジトリ直下で実行する。接続文字列は Git 管理対象の設定ファイルに保存しない。
以下は開発用の例で、`YOUR_PASSWORD` は `.devcontainer/.env` に設定した値へ置き換える。
パスワードにセミコロンなどを含む場合は、SQL Server 接続文字列の規則に従って値を引用する。

```sh
export ConnectionStrings__AuthDatabase='Server=sqlserver,1433;Database=TrySvelte;User Id=sa;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True'
dotnet tool restore
dotnet ef database update --project backend
```

バックエンドは同じ環境変数を設定したターミナルから起動する。
開発コンテナ外から実行する場合は接続先を `127.0.0.1,1433`（または変更したポート）にする。
環境変数の代わりに .NET User Secrets の `ConnectionStrings:AuthDatabase` も利用できる。
マイグレーションは `backend/Data/Migrations` に管理し、起動時の自動適用は行わない。

## 確認メールの設定

`backend/appsettings.json` の `Email` に各項目の日本語コメントを記載している。
ASP.NET Core はこの設定ファイルのコメントを読み飛ばす。

| 項目 | 設定する値 |
| --- | --- |
| `PublicBaseUrl` | 利用者が開く SPA の URL。Vite 開発時は `http://localhost:5173`、バックエンドから配信する場合はその URL。本番は HTTPS 必須 |
| `ConfirmationTokenLifespanMinutes` | 確認トークンの発行からの有効期限（分）。1以上の整数、既定値は `1440`（24時間）。メール本文にも同じ値を表示 |
| `Host` | 配信サービスの SMTP ホスト名 |
| `Port` | STARTTLS 用ポート（通常 `587`）。暗黙 TLS の `465` は非対応 |
| `EnableSsl` | STARTTLS を使用する場合 `true`。認証不要のローカル受信サーバーのみ `false` を使用 |
| `From` | 配信サービスで許可・検証済みの差出人アドレス |
| `Username` | SMTP 認証ユーザー名。開発用で認証不要なら空欄 |
| `Password` | SMTP パスワード。設定ファイルには保存せず User Secrets または `Email__Password` 環境変数で指定 |

### Resend を使用する

設定ファイルは Resend SMTP（`smtp.resend.com:587`、STARTTLS 有効、ユーザー名 `resend`）を使用する。
Resend の API Keys 画面で送信権限を持つ API キーを作成し、SMTP パスワードとして保存する。
バックエンドを起動する Dev Container 内のリポジトリ直下で実行する。
以前の SMTP 設定が User Secrets に残っていても切り替わるよう、接続先も指定する。

```sh
dotnet user-secrets set "Email:Host" "smtp.resend.com" --project backend
dotnet user-secrets set "Email:Port" "587" --project backend
dotnet user-secrets set "Email:EnableSsl" "true" --project backend
dotnet user-secrets set "Email:From" "onboarding@resend.dev" --project backend
dotnet user-secrets set "Email:Username" "resend" --project backend
dotnet user-secrets set "Email:Password" "YOUR_RESEND_API_KEY" --project backend
```

初期の差出人 `onboarding@resend.dev` はテスト用で、宛先には Resend アカウントの登録メールアドレスを使う。
他の利用者へ送信する場合は Resend の Domains で所有ドメインの DNS 検証を完了し、
`Email:From` をそのドメインの差出人（例: `noreply@your-domain.example`）に変更する。
API キーで送信可能なドメインを制限している場合は、この差出人のドメインを許可する。

環境変数 `Email__*` は User Secrets より優先されるため、古い接続情報があれば更新する。
`PublicBaseUrl` はメールを開くブラウザーからアクセスできる SPA の URL にする。
設定後にバックエンドを再起動し、ログイン画面の「確認メールを再送」で確認する。
Resend の Emails 画面でも送信結果を確認できる。

参考: [Resend SMTP 設定](https://resend.com/docs/send-with-smtp)、
[テスト用ドメインの宛先制限](https://resend.com/docs/knowledge-base/403-error-resend-dev-domain)。

SMTP 設定が空欄の場合は送信できない。送信失敗時は未確認アカウントを保持して `503` を返し、
ログイン画面の「確認メールを再送」から復旧できる。再送は登録状態を公開しない同じ応答を返すため、
その成功応答は配送完了の保証ではない。送信は最大30秒で打ち切り、自動再試行・配送キューは使用しない。

送信失敗時は例外の種類と SMTP ステータスをサーバーログに記録する。
Development 環境では SMTP サーバーの応答・内部例外・スタックトレースも記録するため、
バックエンドを再起動して「確認メールを再送」から原因を確認できる。
詳細ログにはメールアドレスなどが含まれる場合があるため、共有時は伏せる。
パスワードや接続設定全体はログ出力しない。本番では例外本文を記録せず、API 応答にも詳細を返さない。

確認トークンは Identity が生成・検証し、有効期限は `Email:ConfirmationTokenLifespanMinutes` で指定する。
例えば `30` にすると発行から30分間有効になり、メール本文にも「発行から30分」と表示する。
設定変更後はバックエンドを再起動する。0以下の値は起動時にエラーになる。
変更後の期限は既存トークンの検証にも適用されるため、送信済みメールの説明とは異なる場合がある。
この設定は Identity の標準 Data Protection トークンプロバイダーに適用する（現在、パスワード再設定は未実装）。
リンクのフラグメントに格納し、確認画面のボタンから CSRF 対策付き POST で送る。
メールスキャナーがリンクを開くだけでは確認済みにならない。
Data Protection の鍵を保持しないと再起動後に既存リンクが無効になることがある。
既存アカウントも `EmailConfirmed` が false ならログインできなくなるため、再送から確認を行う。
既存の Identity テーブルを使用するので追加マイグレーションは不要。

## ログイン機能

ユーザー管理で最初に決める方針と、それによるアプリの挙動は [ASP.NET_Core_Identity.md](ASP.NET_Core_Identity.md) を参照する。

ASP.NET Core Identity の Cookie 認証を使用する。登録後に確認メールを送信し、ログイン画面へ移動する。メール内のリンクを開き、確認ボタンを押すまでログインできない。
パスワードは12〜128文字で、大文字・小文字・数字・記号をそれぞれ含める。
ログイン失敗5回で15分間ロックアウトする。パスワード再設定・MFA は未実装。

| API | 動作 |
| --- | --- |
| `GET /api/auth/csrf` | CSRF Cookie と JSON の `token` を取得 |
| `POST /api/auth/register` | `{ email, password }` で登録。成功 `201`、入力不備・登録不可 `400` |
| `POST /api/auth/confirm-email` | `{ userId, token }` で確認。成功 `204`、無効・期限切れ `400` |
| `POST /api/auth/resend-confirmation` | `{ email }` で再送。未登録・確認済みも同じ `200` 応答 |
| `POST /api/auth/login` | `{ email, password }` で認証。成功 `200` と `{ id, email, permissions }`、認証失敗 `401` |
| `GET /api/auth/me` | 認証済みなら `200` と `{ id, email, permissions }`、未認証なら `401` |
| `POST /api/auth/logout` | Cookie を削除し `204` |

`permissions` は、ログイン中ユーザーが保持する全ロールの実効権限を `{ アクションキー: レベル }` の形で表したマップ
(レベルは `PermissionLevel` の数値、None=0/Read=1/Write=2。権限を持たないアクションキーはキー自体を省略する)。
フロントエンドはこれを使ってナビゲーションの表示を権限に応じて出し分けるが、あくまでUXのためであり、各APIの
`[Authorize]`/`[RequirePermission]` による保護とは独立している。経緯は
[decisions/0003-expose-effective-permissions-in-me.md](decisions/0003-expose-effective-permissions-in-me.md) を参照する。

## 認可(ロール・権限)

ロールベースの認可方針は [ASP.NET_Core_Identity.md](ASP.NET_Core_Identity.md) の「6. 認証後に何を許可するか」と
[decisions/0001-role-based-authorization-design.md](decisions/0001-role-based-authorization-design.md) を参照する。

ロールの実体・ユーザーへの割り当ては ASP.NET Core Identity の標準ロール機能(`AspNetRoles`/`AspNetUserRoles`)を使う。
1人のユーザーは複数のロールを同時に持て、実効権限は保持する全ロールの権限レベルの最大値になる。
「管理者」という特別なロール名は存在せず、`Admin.Roles`/`Admin.UserRoles` への書き込み権限を持つことがそのまま管理者権限になる。
非管理者は自分自身を含め誰のロールも変更できない(これらの権限を持たないため)。

APIアクションには `[PermissionKey]` で権限キーを、`[RequirePermission]` で必要な権限レベル(`Read`/`Write`、`Write` は
`Read` を含む)を宣言する。権限キーごとのロールの権限レベルは `RolePermissions` テーブルに持ち、リクエストのたびに
DB を参照して判定するため、Cookie に権限情報は載らず、ロール・権限の変更は既存のログインセッションに即時反映される。
起動時に、コード上の `[PermissionKey]` が `PermissionActions` テーブルへ自動同期される(削除されたキーは自動削除しない)。

| API | 動作 |
| --- | --- |
| `GET /api/admin/roles` | ロール一覧とロールごとの権限を取得(`Admin.Roles` の `Read`) |
| `GET /api/admin/roles/permission-actions` | 権限キーのカタログを取得(`Admin.Roles` の `Read`) |
| `POST /api/admin/roles` | `{ name }` でロールを作成(`Admin.Roles` の `Write`) |
| `PUT /api/admin/roles/{roleId}` | ロール名を変更(`Admin.Roles` の `Write`) |
| `DELETE /api/admin/roles/{roleId}` | ロールを削除(`Admin.Roles` の `Write`) |
| `PUT /api/admin/roles/{roleId}/permissions` | `{ permissions: [{ actionKey, level }] }` でロールの権限を一括更新(`Admin.Roles` の `Write`) |
| `GET /api/admin/users/{userId}/roles` | 指定ユーザーの保持ロールを取得(`Admin.UserRoles` の `Read`) |
| `PUT /api/admin/users/{userId}/roles` | `{ roles: [...] }` でユーザーのロールを置き換え(`Admin.UserRoles` の `Write`) |
| `GET /api/admin/users` | ユーザー一覧をメールアドレス部分一致で絞り込み・メールアドレス/登録日時で並び替え・ページングして取得(`Admin.Users` の `Read`) |

フロントエンドの管理画面は `/admin/roles`(ロールの一覧・作成・名称変更・削除・権限マトリクス編集)と、
`/admin/users`(ユーザー一覧の検索・並び替え・ページング)、`/admin/users/{userId}/roles`(個別ユーザーへの
ロール編集)を実装している。トップ画面には対応する `Read` 権限を持つ場合のみ各画面へのリンクを表示するが、
直接URLを開かれた場合に備えて画面側でも `403` 応答を検出し「権限がありません」と案内する。
`/admin/users/{userId}/roles` はロール名の一覧を表示するために `GET /api/admin/roles` も呼ぶため、
利用には `Admin.UserRoles` に加えて `Admin.Roles` の `Read` も必要になる。

### 最初の管理者のブートストラップ

`Admin:Bootstrap:Email`/`Admin:Bootstrap:Password` を設定すると、起動時に最初の管理者アカウントを自動作成する。
`Admin.Roles` への `Write` 権限を持つロールが既に存在する場合は何もしないため、何度起動しても安全。
未設定(空欄)の場合もブートストラップは実行されない。

```sh
dotnet user-secrets set "Admin:Bootstrap:Email" "admin@example.com" --project backend
dotnet user-secrets set "Admin:Bootstrap:Password" "初期パスワード" --project backend
```

`backend/appsettings.Development.json` には `Admin:Bootstrap:Email` の既定値のみを設定しており、パスワードは
設定ファイルに直接書かず、上記のように User Secrets(または環境変数 `Admin__Bootstrap__Password`)で指定する
方針にしている(`Email:Password` の Resend API キーと同じ扱い)。作成されるアカウントはメール確認不要でログインできる。

更新 API は直前に `/api/auth/csrf` を呼び、返却されたトークンを `X-CSRF-TOKEN` ヘッダーに設定する。
Cookie も同時に送信する。CSRF トークンなし・不正なトークンは `400`。
ログイン前後でトークンの対象ユーザーが変わるため、トークンを使い回さない。
認証 API の応答は `Cache-Control: no-store` を返す。

SPA は起動・再読み込み時に `/api/auth/me` を呼ぶ。確認中は待機表示、通信・サーバーエラー時は
再試行画面を表示し、未ログインとは区別する。ユーザー情報はメモリ上に保持し、localStorage は認証に使用しない。
保護された API は `frontend/src/lib/auth.svelte.ts` の `apiFetch` 経由で呼び出す。
`401` は未ログインへ遷移し、`403` は権限不足として認証状態を維持する。
画面表示とは独立して API ごとに `[Authorize]` で認証する（`/api/weatherforecast` も保護対象）。
存在しない `/api` 配下の URL は SPA の HTML ではなく `404` を返す。

認証 Cookie は HttpOnly・SameSite=Lax、有効期間は8時間、スライディング延長なし。
永続 Cookie は発行しない。本番環境では認証・CSRF Cookie に Secure を必須とするため HTTPS で配信する。
開発時は既存の Vite `/api` プロキシ経由で HTTP を利用できる。
通常のログアウトはそのブラウザーの Cookie を削除する。他端末の一括ログアウトは未実装。

本番では専用の DB ユーザーと検証可能な SQL Server 証明書を使用する。
ASP.NET Core Data Protection の鍵は再起動後も保持し、複数インスタンスの場合は共有する。
TLS をリバースプロキシで終端する場合は、信頼するプロキシを限定して転送ヘッダーを設定する。

## 確認

```sh
npm --prefix frontend run check
npm --prefix frontend run test
dotnet build backend
dotnet test backend.Tests
dotnet publish backend -c Release

# マイグレーション適用
dotnet ef database update --project backend

# ワンライナーで起動
npm --prefix frontend run build && dotnet run --project backend
```

`dotnet test backend.Tests` は、各テストの確認内容を日本語の表示名で、成否・所要時間とともに出力する。
テスト作成時のルールは [AGENTS.md の「テストを追加・変更するとき」](AGENTS.md#テストを追加変更するとき) を参照する。
詳細ログが必要な場合は `dotnet test backend.Tests --logger "console;verbosity=detailed"` で上書きできる。

統合テストは SQLite のメモリ DB と実際の Identity・Cookie・CSRF 処理を使用し、
登録・重複・入力検証・認証失敗・ロックアウト・ログイン状態取得・ログアウト・API の認証保護を確認する。
SQL Server 固有のマイグレーション適用は、開発用 SQL Server で別途確認する。

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
