using System.Windows;
using System.Windows.Controls;
using CncController.Services;
using CncController.Models; // 引用 UserRole
using CncController.Views.Windows; // 引用 LoginWindow

namespace CncController.Views.Layouts
{
    public partial class HeaderBar : UserControl
    {
        public HeaderBar()
        {
            InitializeComponent();
        }

        // [新增] 按鈕點擊事件
        private void BtnUser_Click(object sender, RoutedEventArgs e)
        {
            var auth = AuthService.Instance;

            // 1. 如果目前是 Operator (未登入)，則開啟登入視窗
            if (auth.CurrentUser.Role == UserRole.Operator)
            {
                var loginWin = new LoginWindow();
                loginWin.Owner = Application.Current.MainWindow; // 設定父視窗，讓它置中

                // ShowDialog 會卡住直到視窗關閉 (回傳 true 代表登入成功)
                if (loginWin.ShowDialog() == true)
                {
                    // 登入成功！MainViewModel 會自動收到通知更新介面
                }
            }
            // 2. 如果已經登入 (Admin, Engineer...)，則詢問是否登出
            else
            {
                var result = MessageBox.Show(
                    $"Current User: {auth.CurrentUser.Username}\nDo you want to logout?",
                    "Logout",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    auth.Logout(); // 執行登出，變回 Operator
                }
            }
        }
    }
}