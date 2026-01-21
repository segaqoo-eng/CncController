using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using CncController.Models; // 記得引用您的 Model 命名空間

namespace CncController.ViewModels
{
    // 這是表格裡的「每一列」資料結構
    public partial class AxisMapItem : ObservableObject
    {
        public string AxisName { get; set; } // 例如：X Axis, Y Axis

        [ObservableProperty]
        private DiscoveredSlave _selectedSlave; // 使用者選中的那個硬體
    }

    public partial class AxisMappingViewModel : ObservableObject
    {
        // 1. 下拉選單的資料來源 (掃描到的所有硬體)
        public ObservableCollection<DiscoveredSlave> AvailableSlaves { get; } = new();

        // 2. 表格的內容 (X, Y, Z... 的對應列)
        public ObservableCollection<AxisMapItem> AxisMaps { get; } = new();

        public AxisMappingViewModel()
        {
            // 建構函式留空
            // 軸的產生改由 GenerateAxisTable 方法動態決定
        }

        // 當使用者在「機台設定」勾選完軸數後，呼叫此方法來產生表格
        public void GenerateAxisTable(MachineConfigViewModel config)
        {
            // (A) 先暫存使用者已經選好的硬體，避免重新產生時資料遺失
            var oldMaps = new Dictionary<string, DiscoveredSlave>();
            foreach (var item in AxisMaps)
            {
                if (item.SelectedSlave != null)
                {
                    oldMaps[item.AxisName] = item.SelectedSlave;
                }
            }

            // (B) 清空舊表格
            AxisMaps.Clear();

            // (C) 根據 Config 加入需要的軸
            if (config.EnableX) AddAxisRow("X Axis", oldMaps);
            if (config.EnableY) AddAxisRow("Y Axis", oldMaps);
            if (config.EnableZ) AddAxisRow("Z Axis", oldMaps);
            if (config.EnableA) AddAxisRow("A Axis", oldMaps);
            if (config.EnableB) AddAxisRow("B Axis", oldMaps);
            if (config.EnableC) AddAxisRow("C Axis", oldMaps);
        }

        // 輔助方法：加入一列，並嘗試還原之前的選擇
        private void AddAxisRow(string name, Dictionary<string, DiscoveredSlave> oldMaps)
        {
            var newItem = new AxisMapItem { AxisName = name };

            // 如果之前已經選過，幫他自動填回去
            if (oldMaps.ContainsKey(name))
            {
                newItem.SelectedSlave = oldMaps[name];
            }

            AxisMaps.Add(newItem);
        }

        // 當硬體掃描完成後，主程式會呼叫這個函式把資料傳進來
        public void UpdateSlaves(IEnumerable<DiscoveredSlave> slaves)
        {
            AvailableSlaves.Clear();
            foreach (var slave in slaves)
            {
                AvailableSlaves.Add(slave);
            }
        }
    }
}