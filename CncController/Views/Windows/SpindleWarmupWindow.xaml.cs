// [2026-03-12] 主軸暖機對話框 code-behind（最小化）
using System.Windows;

namespace CncController.Views.Windows
{
    public partial class SpindleWarmupWindow : Window
    {
        public SpindleWarmupWindow()
        {
            InitializeComponent();
        }

        // [2026-03-12] 關閉按鈕
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
