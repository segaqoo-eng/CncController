using System.Windows;
using CncController.Services;

namespace CncController.Views.Windows
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            TxtPassword.Focus();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            // 使用 AuthService 驗證密碼
            if (AuthService.Instance.Login(TxtPassword.Password))
            {
                this.DialogResult = true; // 成功，關閉視窗
            }
            else
            {
                MessageBox.Show("Invalid Password!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtPassword.Clear();
                TxtPassword.Focus();
            }
        }
    }
}