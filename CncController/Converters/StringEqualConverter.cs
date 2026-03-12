// [2026-03-12] 新增 StringEqualConverter：字串比對轉換器（RadioButton ↔ ViewModel string 雙向綁定）
using System;
using System.Globalization;
using System.Windows.Data;

namespace CncController.Converters
{
    public class StringEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return parameter?.ToString() ?? "";
            return Binding.DoNothing;
        }
    }
}
