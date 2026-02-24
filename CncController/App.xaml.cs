using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace CncController
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // [2026-02-24] 全域例外處理：捕捉未處理例外並寫入 crash log
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DispatcherUnhandledException\n{args.Exception}\n\n";
                File.AppendAllText(logPath, msg);
                MessageBox.Show($"未處理例外：\n{args.Exception.Message}\n\n詳細記錄已寫入 crash.log",
                    "CncController 錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var ex = args.ExceptionObject as Exception;
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UnhandledException\n{ex}\n\n";
                File.AppendAllText(logPath, msg);
            };
        }
    }

}
