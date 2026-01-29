using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace CncController.Models
{
    // ★★★ UI 綁定核心 ★★★
    public partial class MachineStatus : ObservableObject
    {
        [ObservableProperty] private bool _connected;
        [ObservableProperty] private string _taskState = "OFF";
        [ObservableProperty] private string _interpState = "IDLE";

        // [新增] 用於 UI 鎖定 (當 true 時，JOG 按鈕應 disable)
        [ObservableProperty] private bool _isMoving;

        // [新增] 是否有錯誤
        [ObservableProperty] private bool _hasError;

        // 座標屬性
        [ObservableProperty] private double _x;
        [ObservableProperty] private double _y;
        [ObservableProperty] private double _z;
        [ObservableProperty] private double _a;
        [ObservableProperty] private double _b;
        [ObservableProperty] private double _c;

        [ObservableProperty] private double _feedrate;
        [ObservableProperty] private double _spindleSpeed;
        [ObservableProperty] private string _file = "No File Loaded";

        // [新增] 警報訊息集合 (供 UI 顯示跑馬燈或列表)
        [ObservableProperty] private ObservableCollection<string> _alerts = new();
    }
}