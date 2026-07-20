using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace CodexPulse
{
    internal static class Program
    {
        private const string MutexName = @"Local\CodexPulse_25C731BE_7C39_4E1B_A8F5_994D8EDB49C5";

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                SettingsStore store = new SettingsStore();
                QuotaApiClient client = new QuotaApiClient();
                bool minimized = args.Any(value => string.Equals(value, "--minimized", StringComparison.OrdinalIgnoreCase));
                using (MainWindow window = new MainWindow(store, client, minimized))
                {
                    window.Show();
                    app.Run();
                }
            }
        }
    }
}

