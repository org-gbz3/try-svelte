# admin-tenants(テナント管理)

## 概要

運営者によるテナントの作成・名称変更・削除と、既存ユーザーの所属・テナントロールの割り当てを扱う。権限キーは `Admin.Tenants`(システムロール用)。
招待は行わず、登録済みユーザーをメールアドレスで直接所属させる([decisions/0010](../../decisions/0010-multi-tenancy-design.md))。
テナント・所属・テナントロールのテーブル構成は [tenants.md](tenants.md) を参照する。

- テナント作成時に、既定ロール「テナント管理者」(`TenantRoles.IsDefaultAdmin`)を同じ `SaveChanges` で作成し、全テナント用権限キー(`PermissionActions.Scope` が `Tenant`)への `Write` を付与する。後から追加された権限キーは起動時の `PermissionActionSync` が全テナントの既定ロールへ補完する([decisions/0011](../../decisions/0011-tenant-default-admin-role-grants.md))。
- 個別テナントのAPIもルートに `{tenantId}` を含むため、テナント文脈が設定され、クエリフィルターと保存時の検証がそのテナントに限定して働く。

## ER図

テーブル構成は [tenants.md の ER図](tenants.md#er図) と同じ。このドメインが操作するのは `Tenants`・`TenantMemberships`・`TenantMemberRoles` と、テナント作成時の `TenantRoles`・`TenantRolePermissions`。

## 画面操作とCRUD対応

| 画面 | 操作 | API | CRUD | 対象テーブル |
|---|---|---|---|---|
| `admin/tenants/` | テナント一覧(所属人数付き)の表示 | `GET /api/admin/tenants` | Read | `Tenants`, `TenantMemberships` |
| `admin/tenants/`(新規テナント名フォーム) | テナント作成 | `POST /api/admin/tenants` | Create | `Tenants`, `TenantRoles`, `TenantRolePermissions`(既定ロール) |
| `admin/tenants/{tenantId}/` | テナント・テナントロール・メンバーの表示 | `GET /api/admin/tenants/{tenantId}` | Read | `Tenants`, `TenantRoles`, `TenantMemberships`, `TenantMemberRoles`, `AspNetUsers` |
| `admin/tenants/{tenantId}/`(「名称を変更」) | テナント名変更 | `PUT /api/admin/tenants/{tenantId}` | Update | `Tenants` |
| `admin/tenants/{tenantId}/`(「テナントを削除」) | テナント削除 | `DELETE /api/admin/tenants/{tenantId}` | Delete | `TenantMemberRoles`(明示削除)、`Tenants`(所属・テナントロール・その権限はカスケード削除) |
| `admin/tenants/{tenantId}/`(メールアドレスフォーム、「追加」) | 既存ユーザーを所属させる | `POST /api/admin/tenants/{tenantId}/members` | Create | `TenantMemberships` |
| `admin/tenants/{tenantId}/`(「所属を解除」) | 所属解除 | `DELETE /api/admin/tenants/{tenantId}/members/{userId}` | Delete | `TenantMemberships`(`TenantMemberRoles` はカスケード削除) |
| `admin/tenants/{tenantId}/`(ロールのチェック、「ロールを保存」) | メンバーのテナントロールを設定(全置換) | `PUT /api/admin/tenants/{tenantId}/members/{userId}/roles` | Delete + Create | `TenantMemberRoles` |
