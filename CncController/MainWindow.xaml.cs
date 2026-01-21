using System.Windows;
using CncController.Views.Windows;

namespace CncController
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            var loginWin = new LoginWindow();
            loginWin.Owner = this;
            loginWin.ShowDialog();
        }
    }
}