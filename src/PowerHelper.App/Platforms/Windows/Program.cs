using Microsoft.UI.Dispatching;

namespace PowerHelper.App.WinUI;

/// <summary>
/// Replaces WinUI's generated entry point (see DISABLE_XAML_GENERATED_MAIN in the csproj).
/// The only reason to hand-write it is the single-instance check: it has to happen before
/// <see cref="Microsoft.UI.Xaml.Application.Start"/> spins up a UI thread, because a tray
/// app started twice ends up with two icons fighting over the same device.
/// </summary>
public static class Program
{
    // Static so it lives as long as the process. Never released explicitly - the handle is
    // freed when the process exits, which is exactly the lifetime the lock should have.
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\PowerHelper.SingleInstance", out var isNewInstance);
        if (!isNewInstance)
        {
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // The parameter is named rather than discarded with _: a discard for the callback
        // parameter puts `_` in scope, so `_ = new App()` then reads as an assignment to it
        // rather than as a discard of the App.
        Microsoft.UI.Xaml.Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);

            // WinUI takes ownership of the instance through Application.Current; nothing
            // here needs to hold a reference to it.
            _ = new App();
        });
    }
}
