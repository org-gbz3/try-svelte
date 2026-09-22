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
public class UsersController(UserManager<ApplicationUser> userManager, AuthDbContext db) : ControllerBase
{
    private const int MaxPageSize = 100;

    // SQLite プロバイダの invariant name。大文字小文字を区別する検索の分岐にのみ使う(decisions/0005参照)。
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";

    [PermissionKey("Admin.Users", "ユーザー一覧")]
    [RequirePermission("Admin.Users", PermissionLevel.Read)]
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery, StringLength(254)] string? email,
        [FromQuery] bool prefixMatch = false,
        [FromQuery] bool caseSensitive = false,
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
            var pattern = prefixMatch ? $"{escaped}%" : $"%{escaped}%";

            if (caseSensitive)
            {
                // SQLite の LIKE は case_sensitive_like を設定しない限りASCIIの大文字小文字を常に無視し、
                // COLLATE 句を付けても変わらない仕様のため、プロバイダごとに区別を強制する方法を分ける
                // (decisions/0005参照)。
                if (db.Database.ProviderName == SqliteProviderName)
                {
                    await db.Database.ExecuteSqlRawAsync("PRAGMA case_sensitive_like = ON;");
                    query = query.Where(user => EF.Functions.Like(user.Email!, pattern, "\\"));
                }
                else
                {
                    query = query.Where(user =>
                        EF.Functions.Like(EF.Functions.Collate(user.Email!, "SQL_Latin1_General_CP1_CS_AS"), pattern, "\\"));
                }
            }
            else
            {
                // 大文字小文字を区別しない検索は、DBの既定の照合順序に依存させず、
                // Identity が既に保持している正規化済み(大文字化済み)の NormalizedEmail を使って揃える。
                var normalizedPattern = prefixMatch
                    ? $"{escaped.ToUpperInvariant()}%"
                    : $"%{escaped.ToUpperInvariant()}%";
                query = query.Where(user => EF.Functions.Like(user.NormalizedEmail!, normalizedPattern, "\\"));
            }
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
