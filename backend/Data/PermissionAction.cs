namespace backend.Data;

// [PermissionKey] を持つAPIアクションのカタログ。起動時にコードから同期する。
public class PermissionAction
{
    public Guid Id { get; set; }
    public required string ActionKey { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
}
