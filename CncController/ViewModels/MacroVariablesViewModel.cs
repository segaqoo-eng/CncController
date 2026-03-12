// [2026-03-12] 巨集變數監控 ViewModel（#1~#5999 讀寫）
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class MacroVariablesViewModel : ObservableObject
    {
        // [2026-03-12] 顯示用變數集合
        public ObservableCollection<MacroVariable> Variables { get; } = new();

        // [2026-03-12] 二欄並排顯示用
        public ObservableCollection<MacroVariable> Column1 { get; } = new();
        public ObservableCollection<MacroVariable> Column2 { get; } = new();

        // [2026-03-12] 範圍選擇
        [ObservableProperty] private int _rangeStart = 5220;
        [ObservableProperty] private int _rangeEnd = 5230;
        [ObservableProperty] private string _selectedPreset = "WCS G54-G59";
        [ObservableProperty] private bool _isLoading;

        // [2026-03-12] 單筆寫入
        [ObservableProperty] private int _writeVarId = 5000;
        [ObservableProperty] private string _writeVarValue = "0";

        // [2026-03-12] 預設範圍清單
        public List<MacroPresetRange> PresetRanges { get; } = new()
        {
            new MacroPresetRange("Local #1-#30", 1, 30),
            new MacroPresetRange("Global #31-#100", 31, 100),
            new MacroPresetRange("Global #100-#199", 100, 199),
            new MacroPresetRange("ATC #3990-#4024", 3990, 4024),
            new MacroPresetRange("User #5000-#5100", 5000, 5100),
            new MacroPresetRange("WCS G54-G59", 5220, 5390),
        };

        // [2026-03-12] 系統變數名稱對照表
        private static readonly Dictionary<int, string> _knownNames = new()
        {
            { 3990, "ATC Current Pocket" },
            { 5161, "G28 Home X" }, { 5162, "G28 Home Y" }, { 5163, "G28 Home Z" },
            { 5181, "G30 Home X" }, { 5182, "G30 Home Y" }, { 5183, "G30 Home Z" },
            { 5210, "G92 Enable" },
            { 5211, "G92 X" }, { 5212, "G92 Y" }, { 5213, "G92 Z" },
            { 5214, "G92 A" }, { 5215, "G92 B" }, { 5216, "G92 C" },
            { 5220, "Active WCS (1=G54)" },
            { 5221, "G54 X" }, { 5222, "G54 Y" }, { 5223, "G54 Z" },
            { 5224, "G54 A" }, { 5225, "G54 B" }, { 5226, "G54 C" },
            { 5241, "G55 X" }, { 5242, "G55 Y" }, { 5243, "G55 Z" },
            { 5261, "G56 X" }, { 5262, "G56 Y" }, { 5263, "G56 Z" },
            { 5281, "G57 X" }, { 5282, "G57 Y" }, { 5283, "G57 Z" },
            { 5301, "G58 X" }, { 5302, "G58 Y" }, { 5303, "G58 Z" },
            { 5321, "G59 X" }, { 5322, "G59 Y" }, { 5323, "G59 Z" },
            { 5341, "G59.1 X" }, { 5342, "G59.1 Y" }, { 5343, "G59.1 Z" },
            { 5361, "G59.2 X" }, { 5362, "G59.2 Y" }, { 5363, "G59.2 Z" },
            { 5381, "G59.3 X" }, { 5382, "G59.3 Y" }, { 5383, "G59.3 Z" },
        };

        public MacroVariablesViewModel()
        {
            // [2026-03-12] 補齊 ATC slot 名稱
            for (int i = 1; i <= 24; i++)
                _knownNames.TryAdd(4000 + i, $"ATC Slot {i} Tool#");
        }

        // [2026-03-12] 初始化
        [RelayCommand]
        public async Task Initialize()
        {
            await RefreshVariables();
        }

        // [2026-03-12] 選擇預設範圍
        [RelayCommand]
        private async Task SelectPreset(MacroPresetRange preset)
        {
            if (preset == null) return;
            RangeStart = preset.Start;
            RangeEnd = preset.End;
            SelectedPreset = preset.Name;
            await RefreshVariables();
        }

        // [2026-03-12] 刷新變數列表
        [RelayCommand]
        private async Task RefreshVariables()
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                var data = await MachineControlService.Instance.ReadMacroVariablesAsync(RangeStart, RangeEnd);
                Variables.Clear();
                for (int id = RangeStart; id <= RangeEnd; id++)
                {
                    double val = 0;
                    if (data.TryGetValue(id.ToString(), out double v))
                        val = v;
                    // [2026-03-12] 跳過值為 0 且無名稱的變數（減少雜訊），但保留有名稱的
                    bool hasName = _knownNames.ContainsKey(id);
                    if (val == 0 && !hasName && data.Count > 50)
                        continue;

                    Variables.Add(new MacroVariable
                    {
                        Id = id,
                        Value = val,
                        Name = _knownNames.GetValueOrDefault(id, ""),
                        IsModified = false
                    });
                }
                // [2026-03-12] 分割為三欄並排顯示
                SplitIntoColumns();
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Macro refresh failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // [2026-03-12] 將 Variables 平均分配到二欄
        private void SplitIntoColumns()
        {
            Column1.Clear();
            Column2.Clear();
            var list = Variables.ToList();
            int rows = (int)Math.Ceiling(list.Count / 2.0);
            for (int i = 0; i < list.Count; i++)
            {
                if (i < rows) Column1.Add(list[i]);
                else Column2.Add(list[i]);
            }
        }

        // [2026-03-12] 寫入單筆變數
        [RelayCommand]
        private async Task WriteVariable()
        {
            if (!double.TryParse(WriteVarValue, out double val))
            {
                AlarmService.Instance.AddLog("WARN", $"Invalid value: {WriteVarValue}");
                return;
            }
            bool ok = await MachineControlService.Instance.WriteMacroVariableAsync(WriteVarId, val);
            if (ok)
            {
                AlarmService.Instance.AddLog("INFO", $"Written: #{WriteVarId} = {val}");
                // 更新畫面上對應的值
                var existing = Variables.FirstOrDefault(v => v.Id == WriteVarId);
                if (existing != null)
                {
                    existing.Value = val;
                    existing.IsModified = false;
                }
            }
            else
            {
                AlarmService.Instance.AddLog("ERR", $"Write failed: #{WriteVarId}");
            }
        }

        // [2026-03-12] 批次寫入所有已修改的變數
        [RelayCommand]
        private async Task WriteAllModified()
        {
            var modified = Variables.Where(v => v.IsModified).ToDictionary(v => v.Id, v => v.Value);
            if (modified.Count == 0)
            {
                AlarmService.Instance.AddLog("INFO", "No modified variables");
                return;
            }
            bool ok = await MachineControlService.Instance.WriteMacroVariablesAsync(modified);
            if (ok)
            {
                AlarmService.Instance.AddLog("INFO", $"Written {modified.Count} variables");
                foreach (var v in Variables.Where(v => v.IsModified))
                    v.IsModified = false;
            }
            else
            {
                AlarmService.Instance.AddLog("ERR", $"Batch write failed ({modified.Count} vars)");
            }
        }
    }

    // [2026-03-12] 預設範圍定義
    public class MacroPresetRange
    {
        public string Name { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public MacroPresetRange(string name, int start, int end) { Name = name; Start = start; End = end; }
        public override string ToString() => Name;
    }
}
