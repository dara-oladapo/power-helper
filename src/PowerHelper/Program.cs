namespace PowerHelper;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\PowerHelper.SingleInstance", out var isNewInstance);
        if (!isNewInstance)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.Run(new TrayApplicationContext());
    }
}
