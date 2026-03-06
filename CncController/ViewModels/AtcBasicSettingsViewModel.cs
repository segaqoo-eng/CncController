// [2026-03-06] 新增 AtcBasicSettingsViewModel：ATC 基本設定子頁 ViewModel
//              設定刀庫類型、刀具數量、安全位置、時序、Rack 參數
using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;

namespace CncController.ViewModels
{
    public partial class AtcBasicSettingsViewModel : ObservableObject
    {
        // [2026-03-06] 刀庫類型（預設斗笠式，與 AtcViewModel.ACTIVE_ATC_TYPE 一致）
        [ObservableProperty] private AtcType _selectedAtcType = AtcType.Umbrella;

        // [2026-03-06] 刀具數量
        [ObservableProperty] private int _toolCount = 12;

        // [2026-03-06] 安全位置（機台座標）
        [ObservableProperty] private double _zToolChangeHeight = -3.9;
        [ObservableProperty] private double _zClearanceHeight = 0.0;

        // [2026-03-06] 時序參數（ms）
        [ObservableProperty] private int _clampDwell = 1000;
        [ObservableProperty] private int _unclampDwell = 1000;
        [ObservableProperty] private int _airBlowDwell = 300;
        [ObservableProperty] private int _sensorTimeout = 5000;

        // [2026-03-06] Rack（排刀式）專用參數
        [ObservableProperty] private double _rackTraverseSpeed = 3000;
        [ObservableProperty] private double _rackPocket1X = 0.0;
        [ObservableProperty] private double _rackPocket1Y = 0.0;
        [ObservableProperty] private double _rackPocket2X = 0.0;
        [ObservableProperty] private double _rackPocket2Y = 0.0;
        [ObservableProperty] private double _rackClearanceX = 0.0;
        [ObservableProperty] private double _rackClearanceY = 0.0;

        // [2026-03-06] ComboBox 可選刀庫類型
        public AtcType[] AvailableAtcTypes => new[] { AtcType.None, AtcType.Turret, AtcType.Umbrella };

        // [2026-03-06] 計算屬性：依刀庫類型控制 UI 區塊可見性
        public bool IsUmbrella => SelectedAtcType == AtcType.Umbrella;  // [2026-03-06]
        public bool IsTurret => SelectedAtcType == AtcType.Turret;      // [2026-03-06]
        public bool IsNone => SelectedAtcType == AtcType.None;          // [2026-03-06]
        public bool ShowTimingParams => IsUmbrella || IsTurret;         // [2026-03-06] 斗笠/排刀都需要時序
        public bool ShowRackParams => IsTurret;                         // [2026-03-06] 僅排刀式顯示 Rack 參數

        // [2026-03-06] 刀庫類型變更時通知所有計算屬性
        partial void OnSelectedAtcTypeChanged(AtcType value)
        {
            OnPropertyChanged(nameof(IsUmbrella));
            OnPropertyChanged(nameof(IsTurret));
            OnPropertyChanged(nameof(IsNone));
            OnPropertyChanged(nameof(ShowTimingParams));
            OnPropertyChanged(nameof(ShowRackParams));
        }

        // [2026-03-06] 從 AtcConfig 載入所有參數
        public void LoadFrom(AtcConfig config)
        {
            SelectedAtcType = config.Type;
            ToolCount = config.ToolCount;
            ZToolChangeHeight = config.ZToolChangeHeight;
            ZClearanceHeight = config.ZClearanceHeight;
            ClampDwell = config.ClampDwell;
            UnclampDwell = config.UnclampDwell;
            AirBlowDwell = config.AirBlowDwell;
            SensorTimeout = config.SensorTimeout;
            RackTraverseSpeed = config.RackTraverseSpeed;
            RackPocket1X = config.RackPocket1X;
            RackPocket1Y = config.RackPocket1Y;
            RackPocket2X = config.RackPocket2X;
            RackPocket2Y = config.RackPocket2Y;
            RackClearanceX = config.RackClearanceX;
            RackClearanceY = config.RackClearanceY;
        }

        // [2026-03-06] 將所有參數寫回 AtcConfig
        public void SaveTo(AtcConfig config)
        {
            config.Type = SelectedAtcType;
            config.ToolCount = ToolCount;
            config.ZToolChangeHeight = ZToolChangeHeight;
            config.ZClearanceHeight = ZClearanceHeight;
            config.ClampDwell = ClampDwell;
            config.UnclampDwell = UnclampDwell;
            config.AirBlowDwell = AirBlowDwell;
            config.SensorTimeout = SensorTimeout;
            config.RackTraverseSpeed = RackTraverseSpeed;
            config.RackPocket1X = RackPocket1X;
            config.RackPocket1Y = RackPocket1Y;
            config.RackPocket2X = RackPocket2X;
            config.RackPocket2Y = RackPocket2Y;
            config.RackClearanceX = RackClearanceX;
            config.RackClearanceY = RackClearanceY;
        }
    }
}
