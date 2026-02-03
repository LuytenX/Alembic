using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace ACViewer.View
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
                LogException(args.ExceptionObject as Exception, "AppDomain.UnhandledException");

            DispatcherUnhandledException += (s, args) =>
            {
                LogException(args.Exception, "DispatcherUnhandledException");
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                LogException(args.Exception, "TaskScheduler.UnobservedTaskException");
                args.SetObserved();
            };
        }

        private static void LogException(Exception ex, string source)
        {
            if (ex == null) return;

            var msg = $"[{DateTime.Now}] Source: {source}\n{ex}\n\n";
            try
            {
                System.IO.File.AppendAllText("crashlog.txt", msg);
                MessageBox.Show($"A critical error occurred ({source}). Check crashlog.txt for details.\n\n{ex.Message}", "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }
}
