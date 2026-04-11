using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace VpnClient.Converters {
    public class StateToColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is not int state)
                return new SolidColorBrush(Color.FromRgb(244, 67, 54));

            return state switch {
                2 => new SolidColorBrush(Color.FromRgb(76, 175, 80)),   // Connected
                1 => new SolidColorBrush(Color.FromRgb(255, 152, 0)),   // Connecting
                3 => new SolidColorBrush(Color.FromRgb(33, 150, 243)),  // Handshaking
                0 => new SolidColorBrush(Color.FromRgb(244, 67, 54)),   // Disconnected
                4 => new SolidColorBrush(Color.FromRgb(255, 152, 0)),   // Disconnecting
                _ => new SolidColorBrush(Color.FromRgb(244, 67, 54))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StateToIconConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is not int state) return "🔴";

            return state switch {
                2 => "🟢",   // Connected
                1 => "🟡",   // Connecting
                3 => "🔵",   // Handshaking
                0 => "🔴",   // Disconnected
                4 => "🟡",   // Disconnecting
                _ => "🔴"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class StateToTextConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is not int state) return "ОТКЛЮЧЁН";

            return state switch {
                2 => "ПОДКЛЮЧЁН",
                1 => "ПОДКЛЮЧЕНИЕ",
                3 => "РУКОПОЖАТИЕ",
                0 => "ОТКЛЮЧЁН",
                4 => "ОТКЛЮЧЕНИЕ",
                _ => "ОШИБКА"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}