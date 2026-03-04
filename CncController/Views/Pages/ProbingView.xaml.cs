// [2026-03-04] 新增 ProbingView code-behind（最小化）
using System.Windows.Controls;
using System.Windows.Input;

namespace CncController.Views.Pages
{
    public partial class ProbingView : UserControl
    {
        public ProbingView()
        {
            InitializeComponent();
        }

        // [2026-03-04] TOUCH PROBE 垂直 Tab 點擊（模式切換）
        private void TouchProbeTab_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.ProbingViewModel vm)
                vm.SwitchProbeModeCommand.Execute("TouchProbe");
        }
    }
}
