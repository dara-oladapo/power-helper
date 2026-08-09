using System.Reflection;
using Microsoft.UI.Windowing;
using PowerHelper.App.Pages;
using PowerHelper.App.Services;
using PowerHelper.Core;
using PowerHelper.Windows;
using PowerHelper.Tray;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace PowerHelper.App;

public partial class App : Application
{
    // Long enough after launch that the check isn't competing with the rest of startup for
    // the disk and the network, and never announces anything unless it finds something.
    private static readonly TimeSpan StartupUpdateCheckDelay = TimeSpan.FromSeconds(5);

    private readonly PowerHelperEngine _engine;
    private readonly TrayHost _tray;
    private readonly SystemThemeService _themeService;
    private readonly SettingsPage _page;

    private MauiWindow? _window;
    private bool _shuttingDown;

    // Distinguishes MAUI's own activation of the first window at launch, which has to be
    // undone, from every later activation, which is the user actually asking for it.
    private bool _windowEverShown;

    public App(PowerHelperEngine engine, TrayHost tray, SystemThemeService themeService, SettingsPage page)
    {
        _engine = engine;
        _tray = tray;
        _themeService = themeService;
        _page = page;

        InitializeComponent();

        // Follow Windows, full stop. This app deliberately does not offer its own
        // light/dark override the way pc-cleaner does: that is a reasonable feature for an
        // app you sit inside for an hour, and noise in a settings surface you open from the
        // tray for eight seconds. Unspecified keeps tracking the OS, so a desktop that
        // switches at sunset takes this window with it.
        UserAppTheme = AppTheme.Unspecified;

        RequestedThemeChanged += (_, _) => OnPersonalisationChanged();
        _themeService.Changed += OnSystemThemeServiceChanged;

        _tray.OpenSettingsRequested += () => MainThread.BeginInvokeOnMainThread(ShowSettings);
        _tray.ExitRequested += () => MainThread.BeginInvokeOnMainThread(Shutdown);
        _tray.CheckForUpdatesRequested += () => MainThread.BeginInvokeOnMainThread(() => _ = CheckForUpdatesAsync(announce: true));

        _tray.Start();

        _ = ScheduleStartupUpdateCheckAsync();
    }

    protected override MauiWindow CreateWindow(IActivationState? activationState)
    {
        _window = new MauiWindow(_page)
        {
            Title = "Power Helper",
            Width = 560,
            Height = 780,
            MinimumWidth = 420,
            MinimumHeight = 420,
        };

        _window.Created += OnWindowCreated;
        _window.Activated += OnWindowActivated;
        return _window;
    }

    // ---------------------------------------------------------------- window lifetime

    private void OnWindowCreated(object? sender, EventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        NativeWindow.ApplyTitleBarTheme(_window);

        if (NativeWindow.Resolve(_window) is { } appWindow)
        {
            appWindow.Closing += OnAppWindowClosing;
        }

        // A tray app has no window until it is asked for one. MAUI always creates and
        // activates its first window, so the only way to start in the notification area is
        // to create it and immediately put it away.
        NativeWindow.Hide(_window);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // MAUI activates the first window after Created, which can undo the hide above.
        // Hiding once more on the first activation closes that gap; afterwards this is only
        // ever a genuine user-initiated activation, so it must not hide anything.
        if (_window is not null && !_windowEverShown)
        {
            NativeWindow.Hide(_window);
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shuttingDown)
        {
            return;
        }

        // Closing the settings window must not end the process - the tray icon is the app.
        // Exit is an explicit choice from the menu, and it is the only path that restores
        // the hardware.
        args.Cancel = true;
        sender.Hide();
        _engine.SetPollInterval(PowerHelperEngine.IdlePollInterval);
    }

    private void ShowSettings()
    {
        if (_window is null)
        {
            return;
        }

        _windowEverShown = true;
        NativeWindow.ShowAndFocus(_window);
        NativeWindow.ApplyTitleBarTheme(_window);

        // Poll faster while someone is watching, and back off again on close.
        _engine.SetPollInterval(PowerHelperEngine.ActivePollInterval);
        _ = _engine.RefreshStatusAsync();
    }

    private void Shutdown()
    {
        _shuttingDown = true;

        _tray.Dispose();
        _engine.RestoreOnExit();
        _engine.Dispose();
        _themeService.Dispose();

        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    // ---------------------------------------------------------------- personalisation

    private void OnSystemThemeServiceChanged() => MainThread.BeginInvokeOnMainThread(OnPersonalisationChanged);

    private void OnPersonalisationChanged()
    {
        if (_window is not null)
        {
            NativeWindow.ApplyTitleBarTheme(_window);
        }

        _page.ApplyAccent();
        _tray.ApplyTheme();
    }

    // ---------------------------------------------------------------- updates

    private async Task ScheduleStartupUpdateCheckAsync()
    {
        await Task.Delay(StartupUpdateCheckDelay);
        await CheckForUpdatesAsync(announce: false);
    }

    private async Task CheckForUpdatesAsync(bool announce)
    {
        // Already know about one from the background check - take them straight to it
        // rather than hitting the network again.
        if (_engine.AvailableUpdate is { } known)
        {
            BrowserLauncher.Open(known.ReleaseUrl);
            return;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var update = await _engine.CheckForUpdatesAsync(version);

        if (update is null && announce)
        {
            _tray.ShowBalloon("Power Helper", "You're up to date.");
        }
    }
}
