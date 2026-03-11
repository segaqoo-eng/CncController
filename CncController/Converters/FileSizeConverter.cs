using System;
using System.Globalization;
using System.Windows.Data;

namespace CncController.Converters
{
    // [2026-03-11] 檔案大小格式化轉換器（bytes → KB/MB）
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
