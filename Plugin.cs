using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DjScraperPlugin.Windows;

namespace DjScraperPlugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/djscraper";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    internal Configuration Configuration { get; }
    private WindowSystem WindowSystem { get; } = new("DjScraperPlugin");
    private MainWindow MainWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => MainWindow.Toggle())
        {
            HelpMessage = "Open DJ Scraper.",
        });
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += MainWindow.Toggle;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= MainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= MainWindow.Toggle;
        CommandManager.RemoveHandler(CommandName);
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
    }
}
