using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CncController.ViewModels;

namespace CncController.Views.Layouts
{
    public partial class JogPanel : UserControl
    {
        public JogPanel()
        {
            InitializeComponent();
        }

        // 取得 ViewModel 的捷徑
        private MainViewModel ViewModel => DataContext as MainViewModel
                                        ?? Application.Current.MainWindow.DataContext as MainViewModel;

        // 按下按鈕：開始移動
        private void JogBtn_Down(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
            {
                // Tag 格式: "軸號,方向" (例如 "0,1" 代表 X 正向)
                var parts = tag.Split(',');
                if (parts.Length == 2)
                {
                    string axisIndex = parts[0];
                    string direction = parts[1]; // "1" 或 "-1"

                    // 我們將這裡傳遞的數值改為單純的方向 (1 或 -1)
                    // 這樣 ViewModel 就會使用 JogFeedrate 來移動
                    // 如果您想保留 Slider 功能，可以自行乘上 Slider 的值，但目前我們優先支援 JogConfig 的設定
                    string cmdArgs = $"{axisIndex},{direction}";

                    if (ViewModel.JogStartCommand.CanExecute(cmdArgs))
                    {
                        ViewModel.JogStartCommand.Execute(cmdArgs);
                    }
                }
            }
        }

        // 放開按鈕：停止移動
        private void JogBtn_Up(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && ViewModel != null)
            {
                var parts = tag.Split(',');
                if (parts.Length >= 1)
                {
                    string axisIndex = parts[0];

                    // 呼叫停止指令
                    // ViewModel 內部會判斷：如果是寸動模式，其實這行指令是多餘的(因為會自動停)，但也無害
                    if (ViewModel.JogStopCommand.CanExecute(axisIndex))
                    {
                        ViewModel.JogStopCommand.Execute(axisIndex);
                    }
                }
            }
        }
    }
}