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

        // 座標屬性（工件座標）
        [ObservableProperty] private double _x;
        [ObservableProperty] private double _y;
        [ObservableProperty] private double _z;
        [ObservableProperty] private double _a;
        [ObservableProperty] private double _b;
        [ObservableProperty] private double _c;

        // DTG（Distance To Go，剩餘距離）
        [ObservableProperty] private double _dtgX;
        [ObservableProperty] private double _dtgY;
        [ObservableProperty] private double _dtgZ;
        // [2026-02-24] 補齊 A/B/C 軸 DTG（多軸機台支援）
        [ObservableProperty] private double _dtgA;
        [ObservableProperty] private double _dtgB;
        [ObservableProperty] private double _dtgC;

        // [2026-02-24] 新增工件座標（Work Coordinate = actual_position - g5x - g92 - tool）
        [ObservableProperty] private double _workX;
        [ObservableProperty] private double _workY;
        [ObservableProperty] private double _workZ;
        // [2026-02-24] 補齊 A/B/C 軸工件座標（多軸機台支援）
        [ObservableProperty] private double _workA;
        [ObservableProperty] private double _workB;
        [ObservableProperty] private double _workC;

        [ObservableProperty] private double _feedrate;
        [ObservableProperty] private double _spindleSpeed;

        // [2026-02-24] 新增 Feed/Spindle Override 百分比（由後端 /v2/status 回傳）
        [ObservableProperty] private double _feedOverride = 100.0;
        [ObservableProperty] private double _spindleOverride = 100.0;
        [ObservableProperty] private string _file = "No File Loaded";

        // [新增] 警報訊息集合 (供 UI 顯示跑馬燈或列表)
        [ObservableProperty] private ObservableCollection<string> _alerts = new();

        // [2026-02-23] 新增 ActiveCoordSystem：追蹤目前 Active 的工件座標系（G54–G59），由後端 Active_WCS 同步更新
        [ObservableProperty] private string _activeCoordSystem = "G54";
    }
}