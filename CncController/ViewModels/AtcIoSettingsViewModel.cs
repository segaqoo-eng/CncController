// [2026-03-06] 新增 AtcIoSettingsViewModel：ATC IO 設定分頁 ViewModel
//              設定 DO/DI 腳位映射（斗笠式有更多 IO，排刀式較少）
using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CncController.ViewModels
{
    // [2026-03-06] ATC IO Settings：依刀庫類型顯示不同 DO/DI 腳位設定
    public partial class AtcIoSettingsViewModel : ObservableObject
    {
        // =====================================================================
        // IO Slave 選擇
        // =====================================================================

        // [2026-03-06] 選定的 EtherCAT IO 模組
        [ObservableProperty]
        private DiscoveredSlave _selectedIoSlave;

        // [2026-03-06] 可用 IO Slave 列表（供 ComboBox 綁定）
        public ObservableCollection<DiscoveredSlave> AvailableIoSlaves { get; } = new();

        // [2026-03-06] 可用腳位號（0~15，供 ComboBox 綁定）
        public int[] AvailablePins => Enumerable.Range(0, 16).ToArray();

        // =====================================================================
        // DO 腳位（Digital Output，對應 M64/M65 P-word）
        // =====================================================================

        [ObservableProperty] private int _doCarouselOut = 0;   // [2026-03-06] 刀盤伸出
        [ObservableProperty] private int _doCarouselHome = 1;  // [2026-03-06] 刀盤歸位
        [ObservableProperty] private int _doDrawbar = 2;       // [2026-03-06] 拉刀桿
        [ObservableProperty] private int _doAirBlow = 3;       // [2026-03-06] 吹氣
        [ObservableProperty] private int _doMotorFwd = 4;      // [2026-03-06] 馬達正轉
        [ObservableProperty] private int _doMotorRev = 5;      // [2026-03-06] 馬達反轉

        // =====================================================================
        // DI 腳位（Digital Input，對應 M66 P-word）
        // =====================================================================

        [ObservableProperty] private int _diCarouselHome = 0;    // [2026-03-06] 刀盤歸位感測
        [ObservableProperty] private int _diCarouselOut = 1;     // [2026-03-06] 刀盤伸出感測
        [ObservableProperty] private int _diDrawbarClamp = 2;    // [2026-03-06] 拉刀桿夾緊感測
        [ObservableProperty] private int _diDrawbarUnclamp = 3;  // [2026-03-06] 拉刀桿鬆開感測
        [ObservableProperty] private int _diRotationIndex = 4;   // [2026-03-06] 旋轉定位感測

        // =====================================================================
        // 刀庫類型（控制 View 可見性）
        // =====================================================================

        // [2026-03-06] 當前刀庫類型（切換時通知計算屬性）
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowCarouselIo))]
        [NotifyPropertyChangedFor(nameof(ShowDrawbarIo))]
        private AtcType _currentAtcType = AtcType.None;

        // [2026-03-06] 斗笠式專用 IO（CarouselOut/CarouselHome/MotorFwd/MotorRev/RotationIndex）
        public bool ShowCarouselIo => CurrentAtcType == AtcType.Umbrella;

        // [2026-03-06] 拉刀桿 IO（斗笠 + 排刀共用）
        public bool ShowDrawbarIo => CurrentAtcType == AtcType.Umbrella || CurrentAtcType == AtcType.Turret;

        // =====================================================================
        // 方法
        // =====================================================================

        // [2026-03-06] 更新可用 IO Slave 列表
        public void UpdateSlaves(IEnumerable<DiscoveredSlave> slaves)
        {
            AvailableIoSlaves.Clear();
            foreach (var s in slaves)
                AvailableIoSlaves.Add(s);
        }

        // [2026-03-06] 從 AtcConfig 載入設定
        public void LoadFrom(AtcConfig config, IEnumerable<DiscoveredSlave> allSlaves)
        {
            // 更新 Slave 列表
            UpdateSlaves(allSlaves);

            // IO Slave 選擇
            SelectedIoSlave = AvailableIoSlaves.FirstOrDefault(s => s.Index == config.IoSlaveIndex);

            // DO 腳位
            DoCarouselOut = config.DoCarouselOut;
            DoCarouselHome = config.DoCarouselHome;
            DoDrawbar = config.DoDrawbar;
            DoAirBlow = config.DoAirBlow;
            DoMotorFwd = config.DoMotorFwd;
            DoMotorRev = config.DoMotorRev;

            // DI 腳位
            DiCarouselHome = config.DiCarouselHome;
            DiCarouselOut = config.DiCarouselOut;
            DiDrawbarClamp = config.DiDrawbarClamp;
            DiDrawbarUnclamp = config.DiDrawbarUnclamp;
            DiRotationIndex = config.DiRotationIndex;

            // 刀庫類型
            CurrentAtcType = config.Type;
        }

        // [2026-03-06] 將設定寫回 AtcConfig
        public void SaveTo(AtcConfig config)
        {
            // IO Slave Index
            config.IoSlaveIndex = SelectedIoSlave?.Index ?? -1;

            // DO 腳位
            config.DoCarouselOut = DoCarouselOut;
            config.DoCarouselHome = DoCarouselHome;
            config.DoDrawbar = DoDrawbar;
            config.DoAirBlow = DoAirBlow;
            config.DoMotorFwd = DoMotorFwd;
            config.DoMotorRev = DoMotorRev;

            // DI 腳位
            config.DiCarouselHome = DiCarouselHome;
            config.DiCarouselOut = DiCarouselOut;
            config.DiDrawbarClamp = DiDrawbarClamp;
            config.DiDrawbarUnclamp = DiDrawbarUnclamp;
            config.DiRotationIndex = DiRotationIndex;
        }
    }
}
