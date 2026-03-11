// [2026-03-11] 刀具表頁面用：自繪刀具圖示控件（主軸+刀具+刀號/刀長/刀徑標註）
using System.Windows;
using System.Windows.Controls;

namespace CncController.Views.Controls
{
    public partial class ToolDisplayControl : UserControl
    {
        // [2026-03-11] 刀具號
        public static readonly DependencyProperty ToolNumberProperty =
            DependencyProperty.Register(nameof(ToolNumber), typeof(int), typeof(ToolDisplayControl),
                new PropertyMetadata(0, OnPropertyChanged));

        // [2026-03-11] 刀長
        public static readonly DependencyProperty ToolLengthProperty =
            DependencyProperty.Register(nameof(ToolLength), typeof(double), typeof(ToolDisplayControl),
                new PropertyMetadata(0.0, OnPropertyChanged));

        // [2026-03-11] 刀徑
        public static readonly DependencyProperty ToolDiameterProperty =
            DependencyProperty.Register(nameof(ToolDiameter), typeof(double), typeof(ToolDisplayControl),
                new PropertyMetadata(0.0, OnPropertyChanged));

        public int ToolNumber
        {
            get => (int)GetValue(ToolNumberProperty);
            set => SetValue(ToolNumberProperty, value);
        }

        public double ToolLength
        {
            get => (double)GetValue(ToolLengthProperty);
            set => SetValue(ToolLengthProperty, value);
        }

        public double ToolDiameter
        {
            get => (double)GetValue(ToolDiameterProperty);
            set => SetValue(ToolDiameterProperty, value);
        }

        public ToolDisplayControl()
        {
            InitializeComponent();
            UpdateDisplay();
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ToolDisplayControl ctrl)
                ctrl.UpdateDisplay();
        }

        // [2026-03-11] 更新顯示文字
        private void UpdateDisplay()
        {
            if (ToolNumberText != null)
                ToolNumberText.Text = ToolNumber > 0 ? $"T{ToolNumber}" : "T0";
            if (ToolLengthText != null)
                ToolLengthText.Text = ToolLength.ToString("F4");
            if (ToolDiameterText != null)
                ToolDiameterText.Text = ToolDiameter.ToString("F4");
        }
    }
}
