using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;
using System;
using System.Collections.Generic;

namespace CncController.ViewModels
{
    // [2026-02-24] 機台類型顯示項目（繁體中文）
    public class MachineTypeItem
    {
        public MachineType Value { get; set; }
        public string DisplayName { get; set; } = "";
    }

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

        // [2026-02-24] 機台類型下拉選單（選中項目）
        [ObservableProperty] private MachineTypeItem _selectedMachineTypeItem;

        // [2026-02-24] 提供給 ComboBox 的選項列表（繁體中文）
        public List<MachineTypeItem> MachineTypeOptions { get; } = new()
        {
            new() { Value = MachineType.ThreeAxis,        DisplayName = "三軸 VMC（XYZ）" },
            new() { Value = MachineType.FourAxisA,        DisplayName = "四軸 A（XYZ + A 分度盤）" },
            new() { Value = MachineType.FourAxisB,        DisplayName = "四軸 B（XYZ + B 分度盤）" },
            new() { Value = MachineType.FiveAxisTrunnion, DisplayName = "五軸搖籃式（XYZ + AC）" },
            new() { Value = MachineType.FiveAxisSwivel,   DisplayName = "五軸擺頭式（XYZ + BC）" },
            new() { Value = MachineType.SixAxis,          DisplayName = "六軸（XYZABC）" },
        };

        public MachineConfigViewModel()
        {
            // 預設選三軸
            _selectedMachineTypeItem = MachineTypeOptions[0];
        }

        // [2026-02-24] 選項變更時自動更新勾選框
        partial void OnSelectedMachineTypeItemChanged(MachineTypeItem? value)
        {
            if (value == null) return;
            var axes = new MachineConfig { MachineType = value.Value }.GetEnabledAxes();
            EnableA = axes.Contains("A");
            EnableB = axes.Contains("B");
            EnableC = axes.Contains("C");
        }

        // [2026-02-24] 供外部設定 MachineType（從 config 載入時使用）
        public MachineType SelectedMachineType
        {
            get => SelectedMachineTypeItem?.Value ?? MachineType.ThreeAxis;
            set => SelectedMachineTypeItem = MachineTypeOptions.Find(x => x.Value == value)
                                             ?? MachineTypeOptions[0];
        }

        // 其他設定 (例如是否為主僕軸 Gantry)
        // [ObservableProperty] private bool _isGantryY = false;
    }
}
