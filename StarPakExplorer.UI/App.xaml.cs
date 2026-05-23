using System.Windows;
using StarPakExplorer.Application.Abstractions;
using StarPakExplorer.Application.Models;
using StarPakExplorer.Application.Services;
using StarPakExplorer.Infrastructure.Cache;
using StarPakExplorer.Infrastructure.Files;
using StarPakExplorer.Infrastructure.Indexing;
using StarPakExplorer.Infrastructure.Logging;
using StarPakExplorer.Infrastructure.Metadata;
using StarPakExplorer.Infrastructure.Settings;
using StarPakExplorer.Infrastructure.Unpacking;
using StarPakExplorer.UI.ViewModels;

namespace StarPakExplorer.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = new FileAppLogger();
        var settingsStore = new JsonAppSettingsStore();
        var appSettings = LoadSettings(settingsStore);
        var service = new PakExplorerService(
            new AssetUnpacker(logger),
            new CacheRepository(),
            new MetadataReader(logger),
            new FileIndexService(),
            new TextFileReader(),
            logger);

        var window = new MainWindow
        {
            DataContext = new MainViewModel(service, logger, settingsStore, appSettings)
        };
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
}
