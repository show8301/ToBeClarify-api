using ToBeClarify.Api.Models.Entities;

namespace ToBeClarify.Api.Repositories.Client.Media;

public interface IMediaRepository
{
    Task<MediaAssetRow?> GetByIdAsync(string id, CancellationToken cancellationToken);
}
