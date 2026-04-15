using System.Windows;
using System.Windows.Input;

namespace VpnClientWPF;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();

        // Обработка закрытия окна
        this.Closing += MainWindow_Closing;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
        // При закрытии окна отключаемся от сервера
        if (DataContext is ViewModels.MainViewModel vm && vm.CanDisconnect) {
            // Вызываем метод напрямую, а не через Command
            await vm.DisconnectAsyncMethod();

            // Даем время на отправку DISCONNECT
            await Task.Delay(100);
        }
    }
}