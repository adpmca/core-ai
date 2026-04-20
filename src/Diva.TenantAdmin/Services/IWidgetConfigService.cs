using Diva.Core.Models.Widgets;
using Diva.Infrastructure.Data.Entities;

namespace Diva.TenantAdmin.Services;

public interface IWidgetConfigService
{
    Task<List<WidgetConfigDto>> GetForTenantAsync(int tenantId, CancellationToken ct = default);

    /// <summary>Bypasses tenant query filter — for use by public widget endpoints.</summary>
    Task<WidgetConfigEntity?> GetByIdAsync(string widgetId, CancellationToken ct = default);

    Task<WidgetConfigDto> CreateAsync(int tenantId, CreateWidgetRequest request, CancellationToken ct = default);
    Task<WidgetConfigDto> UpdateAsync(int tenantId, string id, CreateWidgetRequest request, CancellationToken ct = default);
    Task DeleteAsync(int tenantId, string id, CancellationToken ct = default);
}
