using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PowerHelper.App.Pages;
using PowerHelper.Core;
using PowerHelper.Services;
using PowerHelper.Tray;

namespace PowerHelper.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Singletons throughout, and that is load-bearing rather than incidental: the engine
        // owns the one AppSettings instance and the one gate that serialises device access,
        // and the tray menu and the settings page are two views of it. A transient here
        // would give each surface a private copy of the settings and let them disagree.
        builder.Services.AddSingleton<SystemThemeService>();
        builder.Services.AddSingleton<PowerHelperEngine>();
        builder.Services.AddSingleton<TrayHost>();
        builder.Services.AddSingleton<SettingsPage>();

        return builder.Build();
    }
}
