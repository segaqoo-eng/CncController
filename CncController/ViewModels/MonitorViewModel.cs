using CommunityToolkit.Mvvm.ComponentModel;

namespace CncController.ViewModels
{
    public partial class MonitorViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _gCodeText = "N10 G90 G54\nN20 G00 X0 Y0\nN30 M03 S1000\nN40 G01 X100 F500\n...";

        public MonitorViewModel()
        {
            // 初始化邏輯
        }
    }
}