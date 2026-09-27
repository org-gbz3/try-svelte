using System.Net;
using System.Net.Http.Json;
using backend.Authorization;
using backend.Data;
using backend.Services;
using backend.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace backend.Tests;

// テナント権限の判定を、本番コードのテナントAPIが揃う前から検証するためのテスト専用エンドポイント。
[ApiController]
[Route("api/tenants/{tenantId:guid}/test-probe")]
public class TenantProbeController : ControllerBase
{
    public const string ActionKey = "Test.TenantProbe";

    [PermissionKey(ActionKey, "テスト用テナントAPI", PermissionScope.Tenant)]
    [RequireTenantPermission(ActionKey, PermissionLevel.Read)]
    [HttpGet]
    public IActionResult Get() => Ok();
}

public class TenantTests
{
    private const string Password = "Test-password-123!";

    [Fact(DisplayName = "未ログインではテナントAPIに401を返す")]
    public async Task TenantApiRejectsAnonymous()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "所属していないテナントのAPIには403を返す")]
    public async Task TenantApiRejectsNonMember()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "所属メンバーはテナント名と実効権限を取得できる")]
    public async Task MemberCanGetTenantMe()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        await factory.GrantTenantRoleAsync(tenantId, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);

        var me = await client.GetFromJsonAsync<TenantMeResponse>($"/api/tenants/{tenantId}/me");
        Assert.Equal("tenant-a", me!.Name);
        Assert.Equal(PermissionLevel.Read, me.Permissions[TenantProbeController.ActionKey]);
    }

    [Fact(DisplayName = "所属していてもテナントロールの権限がなければテナントAPIを利用できない")]
    public async Task MemberWithoutTenantPermissionIsForbidden()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "テナントロールの権限があればテナントAPIを利用できる")]
    public async Task MemberWithTenantPermissionIsAllowed()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        await factory.GrantTenantRoleAsync(tenantId, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "テナントAの権限ではテナントBのAPIを利用できない")]
    public async Task PermissionDoesNotLeakAcrossTenants()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.AddMemberAsync(tenantA, userId);
        await factory.AddMemberAsync(tenantB, userId);
        await factory.GrantTenantRoleAsync(tenantA, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);
        using var response = await client.GetAsync($"/api/tenants/{tenantB}/test-probe");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "テナントの所属解除は再ログインなしで即時に反映される")]
    public async Task MembershipRemovalReflectsImmediately()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        await factory.GrantTenantRoleAsync(tenantId, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);

        using var before = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // 既存のログインセッション(Cookie)はそのままに、DB側の所属だけを解除する。
        await factory.RemoveMemberAsync(tenantId, userId);

        using var after = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact(DisplayName = "システムロールにテナント用の権限キーを設定できない")]
    public async Task SystemRoleRejectsTenantPermissionKey()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        await factory.GrantSystemRoleAsync(userId, "role-admin", "Admin.Roles", PermissionLevel.Write);
        var roleId = await factory.CreateSystemRoleAsync("editors");

        using var response = await Put(client, $"/api/admin/roles/{roleId}/permissions", new
        {
            Permissions = new[] { new { ActionKey = TenantProbeController.ActionKey, Level = PermissionLevel.Read } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "システムロールの権限キー一覧にテナント用の権限キーを含めない")]
    public async Task SystemPermissionCatalogExcludesTenantKeys()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        await factory.GrantSystemRoleAsync(userId, "role-viewer", "Admin.Roles", PermissionLevel.Read);

        var actions = await client.GetFromJsonAsync<List<PermissionActionResponse>>("/api/admin/roles/permission-actions");
        Assert.Contains(actions!, action => action.ActionKey == "Admin.Roles");
        Assert.DoesNotContain(actions!, action => action.ActionKey == TenantProbeController.ActionKey);
    }

    [Fact(DisplayName = "別のテナントでは同じ名前のテナントロールを作成できる")]
    public async Task SameRoleNameAllowedInDifferentTenants()
    {
        using var factory = new TenantFactory();
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.CreateTenantRoleAsync(tenantA, "editors");
        await factory.CreateTenantRoleAsync(tenantB, "editors");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(2, await db.TenantRoles.IgnoreQueryFilters([AuthDbContext.TenantFilter]).CountAsync());
    }

    [Fact(DisplayName = "クエリフィルターにより対象テナント以外の行を取得しない")]
    public async Task QueryFilterHidesOtherTenantRows()
    {
        using var factory = new TenantFactory();
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.CreateTenantRoleAsync(tenantA, "role-a");
        await factory.CreateTenantRoleAsync(tenantB, "role-b");

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var names = await db.TenantRoles.Select(role => role.Name).ToListAsync();
        Assert.Equal(["role-a"], names);
    }

    [Fact(DisplayName = "テナント文脈がない場合はテナントに属するデータを取得しない")]
    public async Task QueryFilterWithoutTenantReturnsNothing()
    {
        using var factory = new TenantFactory();
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.CreateTenantRoleAsync(tenantId, "role-a");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Empty(await db.TenantRoles.ToListAsync());
    }

    [Fact(DisplayName = "テナント文脈がある場合はTenantId未指定の追加に対象テナントを設定する")]
    public async Task SaveAssignsCurrentTenantId()
    {
        using var factory = new TenantFactory();
        var tenantId = await factory.CreateTenantAsync("tenant-a");

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = new TenantRole { Id = Guid.NewGuid(), Name = "editors" };
        db.TenantRoles.Add(role);
        await db.SaveChangesAsync();
        Assert.Equal(tenantId, role.TenantId);
    }

    [Fact(DisplayName = "対象テナント以外のTenantIdを持つデータは保存できない")]
    public async Task SaveRejectsOtherTenantId()
    {
        using var factory = new TenantFactory();
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantA);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.TenantRoles.Add(new TenantRole { Id = Guid.NewGuid(), TenantId = tenantB, Name = "editors" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact(DisplayName = "テナント文脈がない場合はTenantId未指定のデータを保存できない")]
    public async Task SaveWithoutTenantRequiresTenantId()
    {
        using var factory = new TenantFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.TenantRoles.Add(new TenantRole { Id = Guid.NewGuid(), Name = "editors" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact(DisplayName = "別テナントのロールはメンバーに割り当てられない")]
    public async Task MemberRoleRejectsRoleFromOtherTenant()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantA = await factory.CreateTenantAsync("tenant-a");
        var tenantB = await factory.CreateTenantAsync("tenant-b");
        await factory.AddMemberAsync(tenantA, userId);
        var roleInB = await factory.CreateTenantRoleAsync(tenantB, "editors");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.TenantMemberRoles.Add(new TenantMemberRole { TenantId = tenantA, UserId = userId, TenantRoleId = roleInB });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact(DisplayName = "起動時にテナント用の権限キーをテナントスコープとして同期する")]
    public async Task PermissionSyncStoresTenantScope()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        await client.GetAsync("/api/auth/csrf"); // ホストを起動させる

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var action = await db.PermissionActions.SingleAsync(item => item.ActionKey == TenantProbeController.ActionKey);
        Assert.Equal(PermissionScope.Tenant, action.Scope);
    }

    [Fact(DisplayName = "Admin.Tenants権限がなければテナント一覧を取得できない")]
    public async Task TenantAdminRejectsUserWithoutPermission()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, factory);
        using var response = await client.GetAsync("/api/admin/tenants");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Admin.TenantsのRead権限ではテナントを作成できない")]
    public async Task TenantAdminWriteRejectsReadOnlyPermission()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Read);
        using var response = await Post(client, "/api/admin/tenants", new { Name = "tenant-a" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "テナントを作成すると全テナント権限を持つ既定ロールも作成される")]
    public async Task CreateTenantAddsDefaultAdminRole()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var tenantId = await CreateTenantViaApiAsync(client, "tenant-a");

        var detail = await client.GetFromJsonAsync<TenantDetailResponse>($"/api/admin/tenants/{tenantId}");
        var role = Assert.Single(detail!.Roles);
        Assert.Equal("テナント管理者", role.Name);
        Assert.Equal(PermissionLevel.Write, await factory.GetTenantRolePermissionAsync(role.Id, TenantProbeController.ActionKey));
    }

    [Fact(DisplayName = "作成したテナントは一覧に所属人数とともに表示される")]
    public async Task ListShowsCreatedTenant()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        await CreateTenantViaApiAsync(client, "tenant-a");

        var tenants = await client.GetFromJsonAsync<List<TenantListItemResponse>>("/api/admin/tenants");
        var item = Assert.Single(tenants!);
        Assert.Equal(("tenant-a", 0), (item.Name, item.MemberCount));
    }

    [Fact(DisplayName = "テナント名を変更できる")]
    public async Task RenameTenant()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var tenantId = await CreateTenantViaApiAsync(client, "tenant-a");

        using var response = await Put(client, $"/api/admin/tenants/{tenantId}", new { Name = "tenant-renamed" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<TenantDetailResponse>($"/api/admin/tenants/{tenantId}");
        Assert.Equal("tenant-renamed", detail!.Name);
    }

    [Fact(DisplayName = "ロールを割り当てたメンバーがいるテナントも削除できる")]
    public async Task DeleteTenantWithAssignedMembers()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var memberId = await factory.CreateOtherUserAsync("member@example.com");
        var tenantId = await CreateTenantViaApiAsync(client, "tenant-a");
        await AddMemberWithDefaultRoleAsync(client, tenantId, "member@example.com", memberId);

        using var response = await Delete(client, $"/api/admin/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var detail = await client.GetAsync($"/api/admin/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
    }

    [Fact(DisplayName = "メールアドレスで既存ユーザーをテナントに所属させられる")]
    public async Task AddMemberByEmail()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var memberId = await factory.CreateOtherUserAsync("member@example.com");
        var tenantId = await CreateTenantViaApiAsync(client, "tenant-a");

        using var response = await Post(client, $"/api/admin/tenants/{tenantId}/members", new { Email = "member@example.com" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await client.GetFromJsonAsync<TenantDetailResponse>($"/api/admin/tenants/{tenantId}");
        Assert.Equal(memberId, Assert.Single(detail!.Members).UserId);
    }

    [Fact(DisplayName = "未登録のメールアドレスはテナントに所属させられない")]
    public async Task AddMemberRejectsUnknownEmail()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var tenantId = await CreateTenantViaApiAsync(client, "tenant-a");
        using var response = await Post(client, $"/api/admin/tenants/{tenantId}/members", new { Email = "nobody@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "既に所属しているユーザーは重複して追加できない")]
    public async Task AddMemberRejectsDuplicate()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        await factory.CreateOtherUserAsync("member@example.com");
        var tenantId = await CreateTenantViaApiAsync(client, "tenant-a");
        using var first = await Post(client, $"/api/admin/tenants/{tenantId}/members", new { Email = "member@example.com" });
        first.EnsureSuccessStatusCode();

        using var second = await Post(client, $"/api/admin/tenants/{tenantId}/members", new { Email = "member@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact(DisplayName = "運営者が既定ロールを割り当てたメンバーはテナントAPIを利用できる")]
    public async Task AssignedDefaultRoleGrantsTenantApi()
    {
        using var factory = new TenantFactory();
        using var adminClient = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        using var memberClient = factory.CreateClient();
        var memberId = await RegisterAndLogin(memberClient, factory);
        var memberEmail = await factory.GetEmailAsync(memberId);
        var tenantId = await CreateTenantViaApiAsync(adminClient, "tenant-a");

        await AddMemberWithDefaultRoleAsync(adminClient, tenantId, memberEmail, memberId);

        using var response = await memberClient.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "別テナントのロールはメンバーへの割り当てAPIで指定できない")]
    public async Task SetMemberRolesRejectsOtherTenantRole()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var memberId = await factory.CreateOtherUserAsync("member@example.com");
        var tenantA = await CreateTenantViaApiAsync(client, "tenant-a");
        var tenantB = await CreateTenantViaApiAsync(client, "tenant-b");
        using var add = await Post(client, $"/api/admin/tenants/{tenantA}/members", new { Email = "member@example.com" });
        add.EnsureSuccessStatusCode();
        var roleInB = (await client.GetFromJsonAsync<TenantDetailResponse>($"/api/admin/tenants/{tenantB}"))!.Roles.Single().Id;

        using var response = await Put(client, $"/api/admin/tenants/{tenantA}/members/{memberId}/roles", new { RoleIds = new[] { roleInB } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "テナント詳細には他テナントのロールを含めない")]
    public async Task DetailExcludesOtherTenantRoles()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var tenantA = await CreateTenantViaApiAsync(client, "tenant-a");
        await CreateTenantViaApiAsync(client, "tenant-b");

        var detail = await client.GetFromJsonAsync<TenantDetailResponse>($"/api/admin/tenants/{tenantA}");
        Assert.Single(detail!.Roles);
    }

    [Fact(DisplayName = "所属を解除するとテナント詳細のメンバーから外れる")]
    public async Task RemoveMember()
    {
        using var factory = new TenantFactory();
        using var client = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var memberId = await factory.CreateOtherUserAsync("member@example.com");
        var tenantId = await CreateTenantViaApiAsync(client, "tenant-a");
        await AddMemberWithDefaultRoleAsync(client, tenantId, "member@example.com", memberId);

        using var response = await Delete(client, $"/api/admin/tenants/{tenantId}/members/{memberId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<TenantDetailResponse>($"/api/admin/tenants/{tenantId}");
        Assert.Empty(detail!.Members);
    }

    [Fact(DisplayName = "ログイン中ユーザー情報に所属テナントの一覧を含める")]
    public async Task MeIncludesTenants()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.CreateTenantAsync("tenant-b");
        await factory.AddMemberAsync(tenantId, userId);

        var me = await client.GetFromJsonAsync<AuthMeResponse>("/api/auth/me");
        Assert.Equal([new TenantSummary(tenantId, "tenant-a")], me!.Tenants);
    }

    [Fact(DisplayName = "既定ロールを割り当てられたメンバーはテナントロールを作成できる")]
    public async Task DefaultRoleMemberCanCreateTenantRole()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;

        using var response = await Post(member, $"/api/tenants/{tenantId}/roles", new { Name = "editors" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var roles = await member.GetFromJsonAsync<List<TenantRoleDetailResponse>>($"/api/tenants/{tenantId}/roles");
        Assert.Contains(roles!, role => role.Name == "editors" && !role.IsDefaultAdmin);
    }

    [Fact(DisplayName = "Tenant.Roles権限のないメンバーはテナントロール一覧を取得できない")]
    public async Task TenantRolesRejectsMemberWithoutPermission()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        using var response = await client.GetAsync($"/api/tenants/{tenantId}/roles");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "同じテナント内では同名のテナントロールを作成できない")]
    public async Task CreateTenantRoleRejectsDuplicateName()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;
        using var first = await Post(member, $"/api/tenants/{tenantId}/roles", new { Name = "editors" });
        first.EnsureSuccessStatusCode();

        using var second = await Post(member, $"/api/tenants/{tenantId}/roles", new { Name = "editors" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact(DisplayName = "テナントロールにテナント用の権限を設定できる")]
    public async Task SetTenantRolePermissions()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;
        var roleId = await CreateTenantRoleViaApiAsync(member, tenantId, "viewers");

        using var response = await Put(member, $"/api/tenants/{tenantId}/roles/{roleId}/permissions", new
        {
            Permissions = new[] { new { ActionKey = TenantProbeController.ActionKey, Level = PermissionLevel.Read } }
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(PermissionLevel.Read, await factory.GetTenantRolePermissionAsync(roleId, TenantProbeController.ActionKey));
    }

    [Fact(DisplayName = "テナントロールにシステム用の権限キーを設定できない")]
    public async Task TenantRoleRejectsSystemPermissionKey()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;
        var roleId = await CreateTenantRoleViaApiAsync(member, tenantId, "viewers");

        using var response = await Put(member, $"/api/tenants/{tenantId}/roles/{roleId}/permissions", new
        {
            Permissions = new[] { new { ActionKey = "Admin.Roles", Level = PermissionLevel.Write } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "既定のテナント管理者ロールは削除できない")]
    public async Task DefaultRoleCannotBeDeleted()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;
        var defaultRoleId = await GetDefaultRoleIdAsync(member, tenantId);
        using var response = await Delete(member, $"/api/tenants/{tenantId}/roles/{defaultRoleId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "既定のテナント管理者ロールの権限は変更できない")]
    public async Task DefaultRolePermissionsCannotBeChanged()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;
        var defaultRoleId = await GetDefaultRoleIdAsync(member, tenantId);
        using var response = await Put(member, $"/api/tenants/{tenantId}/roles/{defaultRoleId}/permissions", new
        {
            Permissions = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "メンバーに割り当て済みのテナントロールも削除でき、その権限は失われる")]
    public async Task DeleteAssignedTenantRole()
    {
        using var factory = new TenantFactory();
        using var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        await factory.AddMemberAsync(tenantId, userId);
        await factory.GrantTenantRoleAsync(tenantId, userId, "role-admin", "Tenant.Roles", PermissionLevel.Write);
        var probeRoleId = await factory.GrantTenantRoleAsync(tenantId, userId, "viewer", TenantProbeController.ActionKey, PermissionLevel.Read);

        using var response = await Delete(client, $"/api/tenants/{tenantId}/roles/{probeRoleId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var probe = await client.GetAsync($"/api/tenants/{tenantId}/test-probe");
        Assert.Equal(HttpStatusCode.Forbidden, probe.StatusCode);
    }

    [Fact(DisplayName = "他テナントのロールIDを指定した変更は見つからない扱いになる")]
    public async Task RenameRejectsOtherTenantRole()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;
        var otherTenantId = await factory.CreateTenantAsync("tenant-other");
        var otherRoleId = await factory.CreateTenantRoleAsync(otherTenantId, "editors");

        using var response = await Put(member, $"/api/tenants/{tenantId}/roles/{otherRoleId}", new { Name = "renamed" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "Tenant.MemberRoles権限を持つメンバーはメンバーにテナントロールを割り当てられる")]
    public async Task TenantAdminCanAssignMemberRoles()
    {
        using var factory = new TenantFactory();
        var (member, tenantId) = await CreateTenantWithAdminMemberAsync(factory);
        using var _ = member;
        var otherUserId = await factory.CreateOtherUserAsync("other@example.com");
        await factory.AddMemberAsync(tenantId, otherUserId);
        var roleId = await CreateTenantRoleViaApiAsync(member, tenantId, "viewers");

        using var response = await Put(member, $"/api/tenants/{tenantId}/members/{otherUserId}/roles", new { RoleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var members = await member.GetFromJsonAsync<TenantMembersResponse>($"/api/tenants/{tenantId}/members");
        Assert.Equal([roleId], members!.Members.Single(item => item.UserId == otherUserId).RoleIds);
    }

    [Fact(DisplayName = "起動時の同期で既存テナントの既定ロールに不足している権限を付与する")]
    public async Task SyncGrantsMissingPermissionsToDefaultRoles()
    {
        using var factory = new TenantFactory();
        var tenantId = await factory.CreateTenantAsync("tenant-a");
        var defaultRoleId = await factory.CreateTenantRoleAsync(tenantId, "テナント管理者", isDefaultAdmin: true);

        await PermissionActionSync.RunAsync(factory.Services);

        Assert.Equal(PermissionLevel.Write, await factory.GetTenantRolePermissionAsync(defaultRoleId, "Tenant.Roles"));
    }

    // 運営者APIでテナントを作成し、既定ロールを割り当てたメンバーとしてログインしたクライアントを返す。
    private static async Task<(HttpClient Member, Guid TenantId)> CreateTenantWithAdminMemberAsync(TenantFactory factory)
    {
        using var operatorClient = await CreateTenantAdminClientAsync(factory, PermissionLevel.Write);
        var tenantId = await CreateTenantViaApiAsync(operatorClient, "tenant-a");
        var member = factory.CreateClient();
        var memberId = await RegisterAndLogin(member, factory);
        await AddMemberWithDefaultRoleAsync(operatorClient, tenantId, await factory.GetEmailAsync(memberId), memberId);
        return (member, tenantId);
    }

    private static async Task<Guid> CreateTenantRoleViaApiAsync(HttpClient client, Guid tenantId, string name)
    {
        using var response = await Post(client, $"/api/tenants/{tenantId}/roles", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedTenantResponse>())!.Id;
    }

    private static async Task<Guid> GetDefaultRoleIdAsync(HttpClient client, Guid tenantId)
    {
        var roles = await client.GetFromJsonAsync<List<TenantRoleDetailResponse>>($"/api/tenants/{tenantId}/roles");
        return roles!.Single(role => role.IsDefaultAdmin).Id;
    }

    // Admin.Tenants の権限を持つ運営者としてログインしたクライアントを作る。
    private static async Task<HttpClient> CreateTenantAdminClientAsync(TenantFactory factory, PermissionLevel level)
    {
        var client = factory.CreateClient();
        var userId = await RegisterAndLogin(client, factory);
        await factory.GrantSystemRoleAsync(userId, "tenant-operator", "Admin.Tenants", level);
        return client;
    }

    private static async Task<Guid> CreateTenantViaApiAsync(HttpClient client, string name)
    {
        using var response = await Post(client, "/api/admin/tenants", new { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedTenantResponse>())!.Id;
    }

    // メンバーを所属させ、テナント作成時の既定ロールを割り当てる。
    private static async Task AddMemberWithDefaultRoleAsync(HttpClient client, Guid tenantId, string email, string userId)
    {
        using var add = await Post(client, $"/api/admin/tenants/{tenantId}/members", new { Email = email });
        add.EnsureSuccessStatusCode();
        var roleId = (await client.GetFromJsonAsync<TenantDetailResponse>($"/api/admin/tenants/{tenantId}"))!.Roles.Single().Id;
        using var assign = await Put(client, $"/api/admin/tenants/{tenantId}/members/{userId}/roles", new { RoleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
    }

    private static async Task<string> RegisterAndLogin(HttpClient client, TenantFactory factory)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var credentials = new Credentials(email, Password);
        using var registration = await Post(client, "/api/auth/register", credentials);
        registration.EnsureSuccessStatusCode();
        var mail = factory.Sender.Messages.Last(message => message.Email == email);
        using var confirmation = await Post(client, "/api/auth/confirm-email", new { mail.UserId, mail.Token });
        confirmation.EnsureSuccessStatusCode();
        using var login = await Post(client, "/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        return mail.UserId;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object? body = null) =>
        await Send(client, HttpMethod.Post, url, body);

    private static async Task<HttpResponseMessage> Put(HttpClient client, string url, object? body = null) =>
        await Send(client, HttpMethod.Put, url, body);

    private static async Task<HttpResponseMessage> Delete(HttpClient client, string url) =>
        await Send(client, HttpMethod.Delete, url);

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<Csrf>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private record Credentials(string Email, string Password);
    private record Csrf(string Token);
    private record TenantMeResponse(Guid Id, string Name, Dictionary<string, PermissionLevel> Permissions);
    private record PermissionActionResponse(string ActionKey, string DisplayName);
    private record CreatedTenantResponse(Guid Id);
    private record TenantSummary(Guid Id, string Name);
    private record AuthMeResponse(string Id, string Email, List<TenantSummary> Tenants);
    private record TenantRoleDetailResponse(Guid Id, string Name, bool IsDefaultAdmin);
    private record TenantMembersResponse(List<TenantRoleResponse> Roles, List<TenantMemberResponse> Members);
    private record TenantListItemResponse(Guid Id, string Name, DateTime CreatedAt, int MemberCount);
    private record TenantRoleResponse(Guid Id, string Name);
    private record TenantMemberResponse(string UserId, string Email, List<Guid> RoleIds);
    private record TenantDetailResponse(Guid Id, string Name, DateTime CreatedAt, List<TenantRoleResponse> Roles, List<TenantMemberResponse> Members);

    private sealed class RecordingEmailSender : IConfirmationEmailSender
    {
        public List<(string Email, string UserId, string Token)> Messages { get; } = [];
        public Task SendAsync(string email, string userId, string token)
        {
            Messages.Add((email, userId, token));
            return Task.CompletedTask;
        }
        public Task SendPasswordResetAsync(string email, string userId, string token) => Task.CompletedTask;
    }

    private sealed class TenantFactory : WebApplicationFactory<Program>
    {
        public RecordingEmailSender Sender { get; } = new();
        private readonly SqliteConnection connection = new("Data Source=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.AddFilter<ConsoleLoggerProvider>(
                (_, level) => level >= LogLevel.Error));
            builder.ConfigureServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TenantProbeController).Assembly);
                services.RemoveAll<IConfirmationEmailSender>();
                services.AddSingleton<IConfirmationEmailSender>(Sender);
                services.RemoveAll<DbContextOptions<AuthDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
                connection.Open();
                services.AddDbContext<AuthDbContext>(options => options.UseSqlite(connection));
                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.EnsureCreated();
            });
        }

        public async Task<Guid> CreateTenantAsync(string name)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            return tenant.Id;
        }

        public async Task AddMemberAsync(Guid tenantId, string userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.TenantMemberships.Add(new TenantMembership { TenantId = tenantId, UserId = userId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        public async Task RemoveMemberAsync(Guid tenantId, string userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var membership = await db.TenantMemberships.SingleAsync(item => item.TenantId == tenantId && item.UserId == userId);
            db.TenantMemberships.Remove(membership);
            await db.SaveChangesAsync();
        }

        public async Task<Guid> CreateTenantRoleAsync(Guid tenantId, string name, bool isDefaultAdmin = false)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var role = new TenantRole { Id = Guid.NewGuid(), TenantId = tenantId, Name = name, IsDefaultAdmin = isDefaultAdmin };
            db.TenantRoles.Add(role);
            await db.SaveChangesAsync();
            return role.Id;
        }

        // テナントロールを作成して権限を付与し、所属済みのユーザーに割り当てる。戻り値はテナントロールID。
        public async Task<Guid> GrantTenantRoleAsync(
            Guid tenantId, string userId, string roleName, string actionKey, PermissionLevel level)
        {
            var roleId = await CreateTenantRoleAsync(tenantId, roleName);
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var actionId = await db.PermissionActions
                .Where(action => action.ActionKey == actionKey)
                .Select(action => action.Id)
                .SingleAsync();
            db.TenantRolePermissions.Add(new TenantRolePermission { TenantRoleId = roleId, PermissionActionId = actionId, Level = level });
            db.TenantMemberRoles.Add(new TenantMemberRole { TenantId = tenantId, UserId = userId, TenantRoleId = roleId });
            await db.SaveChangesAsync();
            return roleId;
        }

        public async Task<string> CreateSystemRoleAsync(string roleName)
        {
            using var scope = Services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = new IdentityRole(roleName);
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            return role.Id;
        }

        // システムロールを作成して権限を付与し、ユーザーに割り当てる。
        public async Task GrantSystemRoleAsync(string userId, string roleName, string actionKey, PermissionLevel level)
        {
            var roleId = await CreateSystemRoleAsync(roleName);
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var actionId = await db.PermissionActions
                .Where(action => action.ActionKey == actionKey)
                .Select(action => action.Id)
                .SingleAsync();
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionActionId = actionId, Level = level });
            await db.SaveChangesAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("ユーザーが見つかりません。");
            Assert.True((await users.AddToRoleAsync(user, roleName)).Succeeded);
        }

        public async Task<string> CreateOtherUserAsync(string email)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAt = DateTime.UtcNow };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            return user.Id;
        }

        public async Task<string> GetEmailAsync(string userId)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return (await users.FindByIdAsync(userId))!.Email!;
        }

        public async Task<PermissionLevel?> GetTenantRolePermissionAsync(Guid roleId, string actionKey)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            return await (
                from permission in db.TenantRolePermissions
                join action in db.PermissionActions on permission.PermissionActionId equals action.Id
                where permission.TenantRoleId == roleId && action.ActionKey == actionKey
                select (PermissionLevel?)permission.Level
            ).SingleOrDefaultAsync();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) connection.Dispose();
        }
    }
}
