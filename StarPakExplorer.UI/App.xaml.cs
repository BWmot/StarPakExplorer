using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Application.Services;
using StarPakExplorer.Infrastructure.Cache;
using StarPakExplorer.Infrastructure.Files;
using StarPakExplorer.Infrastructure.Indexing;
using StarPakExplorer.Infrastructure.Logging;
using StarPakExplorer.Infrastructure.Metadata;
using StarPakExplorer.Infrastructure.Patches;
using StarPakExplorer.Infrastructure.Settings;
using StarPakExplorer.Infrastructure.Translation;
using StarPakExplorer.Infrastructure.Unpacking;
using StarPakExplorer.UI.ViewModels;

namespace StarPakExplorer.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = new FileAppLogger();
        logger.Info($"StarPakExplorer started. BaseDirectory={AppContext.BaseDirectory}");
        var settingsStore = new JsonAppSettingsStore();
        var appSettings = LoadSettings(settingsStore);
        var cacheRepository = new CacheRepository(appSettings);
        var patchStore = new PatchStore(appSettings);
        var translationProjectStore = new TranslationProjectStore(appSettings);
        var service = new PakExplorerService(
            new AssetUnpacker(logger),
            new AssetPacker(logger),
            cacheRepository,
            patchStore,
            new MetadataReader(logger),
            new FileIndexService(),
            new TextFileReader(),
            logger);
        var globalGlossaryStore = new GlobalGlossaryStore(appSettings);
        _ = Task.Run(() => ImportTermBanksAsync(globalGlossaryStore, logger));
        var translationService = new TranslationService(
            translationProjectStore,
            new GoogleTranslationEngine(),
            new OpenAiTranslationEngine(),
            globalGlossaryStore,
            logger);

        var translationSourceReader = new TranslationSourceReader();
        var translationPatchWriter = new TranslationPatchWriter();

        var window = new MainWindow
        {
            DataContext = new MainViewModel(service, logger, settingsStore, patchStore, translationService, cacheRepository, appSettings, globalGlossaryStore)
        };
        window.SetTranslationServices(translationSourceReader, translationPatchWriter, translationService);
        MainWindow = window;
        window.Show();
    }

    private static AppSettings LoadSettings(IAppSettingsStore settingsStore)
    {
        try
        {
            return settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static async Task ImportTermBanksAsync(IGlobalGlossaryStore store, IAppLogger logger)
    {
        try
        {
            // Look for term bank files relative to the executable and workspace
            var baseDir = AppContext.BaseDirectory;
            var candidates = new List<string>();

            // 1. Next to the exe (published layout)
            candidates.Add(Path.Combine(baseDir, "_ref_trans", "doc", "星界边境术语库-英中.txt"));

            // 2. Development layout (bin/Debug/netX/ -> hop up to workspace root)
            var devRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            candidates.Add(Path.Combine(devRoot, "_ref_trans", "doc", "星界边境术语库-英中.txt"));

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    var count = await store.ImportFromFileAsync(path, CancellationToken.None);
                    if (count > 0)
                    {
                        logger.Info($"Imported {count} terms from term bank: {path}");
                    }
                    break; // Only need one successful import
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to import term banks: {ex.Message}", ex);
        }
    }
}
