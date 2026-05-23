using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
