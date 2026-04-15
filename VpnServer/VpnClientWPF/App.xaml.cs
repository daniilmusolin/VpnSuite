using System.Windows;

namespace VpnClientWPF;

public partial class App : Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) => {
            MessageBox.Show($"Ошибка: {args.Exception.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) => {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"Критическая ошибка: {ex?.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }
}