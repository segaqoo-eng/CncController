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
        // [2026-03-10] 主軸方向：0=停止, 1=CW正轉(M3), -1=CCW反轉(M4)
        [ObservableProperty] private int _spindleDirection;

        // [2026-02-24] 新增 Feed/Spindle Override 百分比（由後端 /v2/status 回傳）
        [ObservableProperty] private double _feedOverride = 100.0;
        [ObservableProperty] private double _spindleOverride = 100.0;
        [ObservableProperty] private string _file = "No File Loaded";

        // [新增] 警報訊息集合 (供 UI 顯示跑馬燈或列表)
        [ObservableProperty] private ObservableCollection<string> _alerts = new();

        // [2026-02-23] 新增 ActiveCoordSystem：追蹤目前 Active 的工件座標系（G54–G59），由後端 Active_WCS 同步更新
        [ObservableProperty] private string _activeCoordSystem = "G54";

        // [2026-02-24] 新增刀具資訊：刀具號、刀長（Z 軸補正）、刀徑（供 ToolInfo 元件綁定）
        [ObservableProperty] private int _toolNumber;
        [ObservableProperty] private double _toolLength;
        [ObservableProperty] private double _toolDiameter;

        // [2026-02-24] 新增各軸 Homed 狀態（供 DRO REF 按鈕紅/綠顯示）
        [ObservableProperty] private bool _isXHomed;
        [ObservableProperty] private bool _isYHomed;
        [ObservableProperty] private bool _isZHomed;
        [ObservableProperty] private bool _isAHomed;
        [ObservableProperty] private bool _isBHomed;
        [ObservableProperty] private bool _isCHomed;
        [ObservableProperty] private bool _isAllHomed;

        // [2026-02-24] 新增 G92 偏移量（供 Offsets 右欄 G52/G92 OFFSET 欄位）
        [ObservableProperty] private double _g92X;
        [ObservableProperty] private double _g92Y;
        [ObservableProperty] private double _g92Z;
        [ObservableProperty] private double _g92A;
        [ObservableProperty] private double _g92B;
        [ObservableProperty] private double _g92C;

        // [2026-02-24] 新增完整刀具偏移（供 Offsets 右欄 TOOL OFFSET 欄位）
        [ObservableProperty] private double _toolOffsetX;
        [ObservableProperty] private double _toolOffsetY;
        [ObservableProperty] private double _toolOffsetZ;

        // [2026-02-24] 新增任務模式（供 Offsets 右下角 MAN/AUTO/MDI 按鈕高亮）
        [ObservableProperty] private string _taskMode = "MANUAL";

        // [2026-03-04] 新增 Block Delete / Optional Stop 開關狀態
        [ObservableProperty] private bool _isBlockDelete;
        [ObservableProperty] private bool _isOptionalStop;

        // [2026-03-04] 新增 Current Line：目前執行的 G-Code 行號（motion_line）
        [ObservableProperty] private int _currentLine;

        // [2026-03-05] 新增 Spindle Load：主軸負載百分比，對齊 PB 版 D_4
        [ObservableProperty] private double _spindleLoad;

        // [2026-03-09] 主軸 Encoder 角度（0~360°，從 spindle.0.revs 換算）
        [ObservableProperty] private double _spindlePosition;

        // [2026-03-09] 刀庫 Encoder 角度（斗笠式旋轉軸回授，從 ATC status 更新）
        [ObservableProperty] private double _carouselPosition;

        // [2026-03-06] 新增 Probe Input：探針輸入訊號即時狀態（供 ProbingView 指示燈）
        [ObservableProperty] private bool _isProbeInput;
    }
}