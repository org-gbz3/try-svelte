namespace backend.Tenancy;

// ルート値 {tenantId} を TenantContext に設定する。認可ハンドラーとクエリフィルターが同じ値を参照するよう、
// UseAuthorization より前に配置する。
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string RouteKey = "tenantId";

    public Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        if (context.GetRouteValue(RouteKey) is string value && Guid.TryParse(value, out var tenantId))
            tenant.Set(tenantId);
        return next(context);
    }
}
