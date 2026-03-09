// [2026-03-06] CNC 主軸 + 刀具向量圖示控件 code-behind
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CncController.Views.Controls
{
    /// <summary>
    /// SpindleToolControl.xaml 的互動邏輯
    /// 顯示 CNC 主軸 + 刀具的 2D 向量圖示（金屬感漸層）
    /// </summary>
    public partial class SpindleToolControl : UserControl
    {
        // [2026-03-06] ToolNumber：0=無刀具，>0=有刀
        public static readonly DependencyProperty ToolNumberProperty =
            DependencyProperty.Register(
                nameof(ToolNumber),
                typeof(int),
                typeof(SpindleToolControl),
                new PropertyMetadata(0, OnToolNumberChanged));

        // [2026-03-06] ToolLabel：顯示文字（空字串時自動依 ToolNumber 生成）
        public static readonly DependencyProperty ToolLabelProperty =
            DependencyProperty.Register(
                nameof(ToolLabel),
                typeof(string),
                typeof(SpindleToolControl),
                new PropertyMetadata(""));

        public int ToolNumber
        {
            get => (int)GetValue(ToolNumberProperty);
            set => SetValue(ToolNumberProperty, value);
        }

        public string ToolLabel
        {
            get => (string)GetValue(ToolLabelProperty);
            set => SetValue(ToolLabelProperty, value);
        }

        public SpindleToolControl()
        {
            InitializeComponent();
        }

        // [2026-03-06] ToolNumber 變化時自動更新 ToolLabel（若未手動設定）
        private static void OnToolNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SpindleToolControl ctrl)
            {
                // 僅在 ToolLabel 為空時自動生成文字
                if (string.IsNullOrEmpty(ctrl.ToolLabel))
                {
                    int toolNum = (int)e.NewValue;
                    ctrl.SetCurrentValue(ToolLabelProperty,
                        toolNum > 0 ? $"T{toolNum} LOADED" : "NO TOOL LOADED");
                }
            }
        }
    }

    /// <summary>
    /// [2026-03-06] 整數非零轉 Visibility 轉換器（ToolNumber != 0 → Visible）
    /// </summary>
    public class NonZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter is string s && s == "Invert";
            bool isNonZero = value is int n && n != 0;
            if (invert) isNonZero = !isNonZero;
            return isNonZero ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
