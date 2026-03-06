// [2026-03-06] 主軸設定 ViewModel（剛性攻牙 / M19 定向）
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;
using System.Collections.Generic;

namespace CncController.ViewModels
{
    public partial class SpindleSettingsViewModel : ObservableObject
    {
        // [2026-03-06] EtherCAT Slave 選擇
        [ObservableProperty]
        private DiscoveredSlave _selectedSpindleSlave;

        public ObservableCollection<DiscoveredSlave> AvailableSlaves { get; } = new();

        // [2026-03-06] 主軸參數
        [ObservableProperty] private int _encoderPPR = 4096;
        [ObservableProperty] private double _maxRPM = 8000;
        [ObservableProperty] private double _maxAccel = 2000;
        [ObservableProperty] private double _orientAngle = 0.0;
        [ObservableProperty] private bool _rigidTappingEnabled = true;

        // [2026-03-06] 更新 Slave 列表
        public void UpdateSlaves(IEnumerable<DiscoveredSlave> slaves)
        {
            AvailableSlaves.Clear();
            foreach (var s in slaves)
                AvailableSlaves.Add(s);
        }

        // [2026-03-06] 從 SpindleConfig 載入
        public void LoadFrom(SpindleConfig config, IEnumerable<DiscoveredSlave> allSlaves)
        {
            UpdateSlaves(allSlaves);
            SelectedSpindleSlave = config.SlaveIndex >= 0
                ? AvailableSlaves.FirstOrDefault(s => s.Index == config.SlaveIndex)
                : null;
            EncoderPPR = config.EncoderPPR;
            MaxRPM = config.MaxRPM;
            MaxAccel = config.MaxAccel;
            OrientAngle = config.OrientAngle;
            RigidTappingEnabled = config.RigidTappingEnabled;
        }

        // [2026-03-06] 寫回 SpindleConfig
        public void SaveTo(SpindleConfig config)
        {
            config.SlaveIndex = SelectedSpindleSlave?.Index ?? -1;
            config.EncoderPPR = EncoderPPR;
            config.MaxRPM = MaxRPM;
            config.MaxAccel = MaxAccel;
            config.OrientAngle = OrientAngle;
            config.RigidTappingEnabled = RigidTappingEnabled;
        }
    }
}
