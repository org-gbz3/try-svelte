namespace backend.Tenancy;

// テナントに属するエンティティ。AuthDbContext がクエリフィルターと保存時の検証を一括適用するため、
// 業務エンティティを追加するときはこのインターフェイスを実装する。
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
