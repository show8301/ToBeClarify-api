using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Services.Logging;

public interface IApiLogService
{
    Task LogAsync(ApiLogEntry entry, CancellationToken cancellationToken = default);
}
