using UIKit;

namespace PowerHelper.App;

public static class Program
{
    // The Mac Catalyst entry point. Unlike the Windows one there is nothing to customise
    // here: single-instance is handled by macOS itself, which will not launch a second copy
    // of an app bundle.
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
