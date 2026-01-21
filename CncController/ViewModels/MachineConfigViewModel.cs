using CommunityToolkit.Mvvm.ComponentModel;

namespace CncController.ViewModels
{
    public partial class MachineConfigViewModel : ObservableObject
    {
        // 基礎三軸 (預設開啟)
        [ObservableProperty] private bool _enableX = true;
        [ObservableProperty] private bool _enableY = true;
        [ObservableProperty] private bool _enableZ = true;

        // 旋轉軸 (預設關閉)
        [ObservableProperty] private bool _enableA = false; // 第4軸
        [ObservableProperty] private bool _enableB = false; // 第5軸
        [ObservableProperty] private bool _enableC = false;

        // 其他設定 (例如是否為主僕軸 Gantry)
        // [ObservableProperty] private bool _isGantryY = false;
    }
}