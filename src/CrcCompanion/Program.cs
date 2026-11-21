using System;
using System.IO;
using System.Threading;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion
{
    public static class Program
    {
        private static Mutex? _single;

        [STAThread]
        public static int Main()
        {
            _single = new Mutex(true, "CRCCompanion.SingleInstance", out bool first);
            if (!first)
            {
                MessageBox.Show("CRC Companion is already running (see the tray icon).", "CRC Companion");
                return 0;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) =>
            {
                LogError(e.Exception);
                e.Handled = true;
            };

            AppController? controller = null;
            app.Startup += (_, _) =>
            {
                try { controller = new AppController(app); }
                catch (Exception ex)
                {
                    LogError(ex);
                    MessageBox.Show("CRC Companion failed to start:\n" + ex.Message, "CRC Companion");
                    app.Shutdown(1);
                }
            };
            app.SessionEnding += (_, _) => controller?.Exit();

            var code = app.Run();
            _single.ReleaseMutex();
            return code;
        }

        public static void LogError(Exception ex)
        {
            try
            {
                var dir = ProfileStore.DefaultRoot();
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), $"[{DateTime.UtcNow:u}] {ex}\n\n");
            }
            catch (Exception) { }
        }
    }
}
