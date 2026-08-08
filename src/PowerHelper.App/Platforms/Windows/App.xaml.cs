namespace PowerHelper.App.WinUI;

/// <summary>
/// The WinUI application head. Entry point lives in Program.cs rather than being generated,
/// so the single-instance mutex is taken before any window exists.
/// </summary>
public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
