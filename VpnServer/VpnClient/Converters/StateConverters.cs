using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VpnClient.Models;
using Color = System.Windows.Media.Color;

namespace VpnClient.Converters {
    public class StateToColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ConnectionState state) {
                switch (state) {
                    case ConnectionState.Connected:
                        return new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    case ConnectionState.Connecting:
                        return new SolidColorBrush(Color.FromRgb(255, 152, 0));
                    case ConnectionState.Handshaking:
                        return new SolidColorBrush(Color.FromRgb(33, 150, 243));
                    case ConnectionState.Disconnected:
                        return new SolidColorBrush(Color.FromRgb(158, 158, 158));
                    case ConnectionState.Disconnecting:
                        return new SolidColorBrush(Color.FromRgb(255, 152, 0));
                    case ConnectionState.Error:
                        return new SolidColorBrush(Color.FromRgb(244, 67, 54));
                    default:
                        return new SolidColorBrush(Color.FromRgb(158, 158, 158));
                }
            }
            return new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class StateToIconConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ConnectionState state) {
                switch (state) {
                    case ConnectionState.Connected:
                        return "🔒";
                    case ConnectionState.Connecting:
                        return "⏳";
                    case ConnectionState.Handshaking:
                        return "🤝";
                    case ConnectionState.Disconnected:
                        return "🔓";
                    case ConnectionState.Disconnecting:
                        return "⏳";
                    case ConnectionState.Error:
                        return "⚠️";
                    default:
                        return "🔓";
                }
            }
            return "🔓";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }

    public class StateToTextConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is ConnectionState state) {
                switch (state) {
                    case ConnectionState.Connected:
                        return "ПОДКЛЮЧЕНО";
                    case ConnectionState.Connecting:
                        return "ПОДКЛЮЧЕНИЕ...";
                    case ConnectionState.Handshaking:
                        return "РУКОПОЖАТИЕ...";
                    case ConnectionState.Disconnected:
                        return "ОТКЛЮЧЕНО";
                    case ConnectionState.Disconnecting:
                        return "ОТКЛЮЧЕНИЕ...";
                    case ConnectionState.Error:
                        return "ОШИБКА";
                    default:
                        return "ОТКЛЮЧЕНО";
                }
            }
            return "ОТКЛЮЧЕНО";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}