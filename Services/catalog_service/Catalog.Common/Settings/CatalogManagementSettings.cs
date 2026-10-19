namespace Catalog.Common.Settings;

public sealed class CatalogManagementSettings
{
    public string[] MenuManagerRoleIds { get; set; } = [];
    public string[] NonSellingItemAvailabilities { get; set; } = [];
    public int IdentityTimeoutSeconds { get; set; }
}
