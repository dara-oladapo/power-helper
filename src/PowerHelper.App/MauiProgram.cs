using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PowerHelper.Abstractions;
using PowerHelper.App.Pages;
using PowerHelper.Core;
using PowerHelper.Platform;
#if WINDOWS
using PowerHelper.Tray;
using PowerHelper.Windows;
#endif

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

        // The one place the app asks which operating system it is on. Everything downstream
        // asks the implementation what it can do instead - see CapabilitySupport - so a
        // partially-ported platform produces disabled controls that explain themselves
        // rather than methods that quietly return false.
        builder.Services.AddSingleton(ResolvePlatformServices());

        // Singletons throughout, and that is load-bearing rather than incidental: the engine
        // owns the one AppSettings instance and the one gate that serialises device access,
        // and every surface is a view of it. A transient here would give each surface a
        // private copy of the settings and let them disagree.
        builder.Services.AddSingleton<PowerHelperEngine>();
        builder.Services.AddSingleton<SettingsPage>();

#if WINDOWS
        builder.Services.AddSingleton<SystemThemeService>();
        builder.Services.AddSingleton<TrayHost>();
#endif

        return builder.Build();
    }

    private static PlatformServices ResolvePlatformServices()
    {
#if WINDOWS
        return WindowsPlatformServices.Create();
#else
        // .NET MAUI has no Linux head, so this branch is currently unreachable in a shipped
        // build. It exists so the abstraction is exercised by something other than Windows
        // and so adding a head is a registration rather than a refactor. See issue #3.
        return UnsupportedPlatform.Everything(UnsupportedPlatform.LinuxNotImplementedYet);
#endif
    }
}
