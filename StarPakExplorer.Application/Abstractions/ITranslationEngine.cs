using StarPakExplorer.Application.Models;

namespace StarPakExplorer.Application.Abstractions;

public interface ITranslationEngine
{
    TranslationEngineType EngineType { get; }

    Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> sourceTexts,
        TranslationProviderSettings settings,
        IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken);
}
