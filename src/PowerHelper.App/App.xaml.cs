using System.Reflection;
using PowerHelper.App.Pages;
using PowerHelper.App.Services;
using PowerHelper.Core;
#if WINDOWS
using Microsoft.UI.Windowing;
using PowerHelper.App.Platforms.Windows;
using PowerHelper.Tray;
using PowerHelper.Windows;
#endif
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace PowerHelper.App;

/// <summary>
/// The app shell differs by platform more than anything else here, and it is the one place
/// a genuine <c>#if</c> is the right tool rather than a capability flag.
///
/// <para>
/// On Windows this is a tray-first app: the window starts hidden, closing it hides it again,
/// and Exit lives in the notification-area menu. On macOS there is no notification area a
/// Catalyst app can reach — <c>NSStatusItem</c> needs AppKit — so it is an ordinary windowed
/// app that opens on launch and quits when closed. That is a difference in what the app
/// <em>is</em> on each platform, not a capability that can be reported.
/// </para>
/// </summary>
public partial class App : Application
{
    // Long enough after launch that the check isn't competing with the rest of startup for
    // the disk and the network, and it never announces anything unless it finds something.
    private static readonly TimeSpan StartupUpdateCheckDelay = TimeSpan.FromSeconds(5);

    private readonly PowerHelperEngine _engine;
    private readonly SettingsPage _page;

    private MauiWindow? _window;

#if WINDOWS
    private readonly TrayHost _tray;
    private readonly SystemThemeService _themeService;

    private bool _shuttingDown;

    // Distinguishes MAUI's own activation of the first window at launch, which has to be
    // undone, from every later activation, which is the user actually asking for it.
    private bool _windowEverShown;

    public App(PowerHelperEngine engine, SettingsPage page, TrayHost tray, SystemThemeService themeService)
    {
        _engine = engine;
        _page = page;
        _tray = tray;
        _themeService = themeService;

        InitializeComponent();
        FollowSystemTheme();

        _themeService.Changed += OnSystemThemeServiceChanged;

        _tray.OpenSettingsRequested += () => MainThread.BeginInvokeOnMainThread(ShowSettings);
        _tray.ExitRequested += () => MainThread.BeginInvokeOnMainThread(Shutdown);
        _tray.CheckForUpdatesRequested += () => MainThread.BeginInvokeOnMainThread(() => _ = CheckForUpdatesAsync(announce: true));

        _tray.Start();

        _ = ScheduleStartupUpdateCheckAsync();
    }
#else
    public App(PowerHelperEngine engine, SettingsPage page)
    {
        _engine = engine;
        _page = page;

        InitializeComponent();
        FollowSystemTheme();

        _ = ScheduleStartupUpdateCheckAsync();
    }
#endif

    /// <summary>
    /// Follow the OS, full stop. This app deliberately does not offer its own light/dark
    /// override the way pc-cleaner does: that is reasonable for an app you sit inside for an
    /// hour, and noise in a settings surface you open for eight seconds. Unspecified keeps
    /// tracking the OS, so a desktop that switches at sunset takes this window with it.
    /// </summary>
    private void FollowSystemTheme() => UserAppTheme = AppTheme.Unspecified;

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

#if WINDOWS
        _window.Created += OnWindowCreated;
        _window.Activated += OnWindowActivated;
#else
        // No tray to retreat to, so the window is the app and it polls at the active rate
        // for as long as it is open.
        _engine.SetPollInterval(PowerHelperEngine.ActivePollInterval);
#endif

        return _window;
    }

#if WINDOWS

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

    private void OnSystemThemeServiceChanged() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_window is not null)
        {
            NativeWindow.ApplyTitleBarTheme(_window);
        }

        _page.ApplyAccent();
        _tray.ApplyTheme();
    });

#endif

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

#if WINDOWS
        if (update is null && announce)
        {
            _tray.ShowBalloon("Power Helper", "You're up to date.");
        }
#else
        // Nowhere to announce it without a notification-area icon; the footer link in the
        // window is the only surface macOS has for this.
        _ = update;
        _ = announce;
#endif
    }
}
