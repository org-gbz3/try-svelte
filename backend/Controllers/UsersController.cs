using System.ComponentModel.DataAnnotations;
using backend.Authorization;
using backend.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

// 数千件規模を想定したユーザー一覧の検索・並び替え・ページング。
// ロール割り当て(Admin.UserRoles)より広い情報(全ユーザーのメールアドレス・登録日時)を開示するため、
// 別の権限キー Admin.Users を用いる(decisions/0004参照)。
[ApiController]
[Route("api/admin/users")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UsersController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    private const int MaxPageSize = 100;

    [PermissionKey("Admin.Users", "ユーザー一覧")]
    [RequirePermission("Admin.Users", PermissionLevel.Read)]
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, StringLength(254)] string? email,
        [FromQuery] SortField sort = SortField.CreatedAt,
        [FromQuery] SortDirection direction = SortDirection.Descending,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaxPageSize)] int pageSize = 20)
    {
        var query = userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(email))
        {
            // LIKE の特殊文字をエスケープし、検索語をそのままリテラルとして扱う。
            var escaped = email.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(user => EF.Functions.Like(user.Email!, $"%{escaped}%", "\\"));
        }

        // CreatedAt が NULL(登録日時不明)の行は、並び順にかかわらず常に末尾に固定する。
        // Id を最終タイブレーカーにし、同値が並んでもページをまたいで安定した順序にする。
        query = (sort, direction) switch
        {
            (SortField.Email, SortDirection.Ascending) => query.OrderBy(u => u.NormalizedEmail).ThenBy(u => u.Id),
            (SortField.Email, SortDirection.Descending) => query.OrderByDescending(u => u.NormalizedEmail).ThenBy(u => u.Id),
            (SortField.CreatedAt, SortDirection.Ascending) =>
                query.OrderBy(u => u.CreatedAt == null).ThenBy(u => u.CreatedAt).ThenBy(u => u.Id),
            _ /* CreatedAt, Descending */ =>
                query.OrderBy(u => u.CreatedAt == null).ThenByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
        };

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new UserListItemResponse(u.Id, u.Email!, u.CreatedAt))
            .ToListAsync();

        return Ok(new UserListResponse(items, totalCount, page, pageSize));
    }

    public enum SortField { Email, CreatedAt }

    public enum SortDirection { Ascending, Descending }

    public sealed record UserListItemResponse(string Id, string Email, DateTime? CreatedAt);

    public sealed record UserListResponse(IReadOnlyList<UserListItemResponse> Items, int TotalCount, int Page, int PageSize);
}
