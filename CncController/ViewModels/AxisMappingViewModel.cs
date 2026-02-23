using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;
using CncController.Models;

namespace CncController.ViewModels
{
    public partial class AxisMapItem : ObservableObject
    {
        public string AxisName { get; set; } = string.Empty;
        [ObservableProperty]
        private DiscoveredSlave _selectedSlave;
    }

    public partial class AxisMappingViewModel : ObservableObject
    {
        public ObservableCollection<DiscoveredSlave> AvailableSlaves { get; } = new();
        public ObservableCollection<AxisMapItem> AxisMaps { get; } = new();

        public AxisMappingViewModel() { }

        // ★★★ 還原設定檔的對應 ★★★
        public void LoadMapping(MachineConfig config, List<DiscoveredSlave> currentSlaves)
        {
            // 1. 更新可用硬體清單 (AvailableSlaves)
            AvailableSlaves.Clear();
            // 加入一個空的選項，讓使用者可以取消選擇
            AvailableSlaves.Add(new DiscoveredSlave { Name = "--- None ---", VendorId = "", Category = "" });

            if (currentSlaves != null) 
            {
                foreach (var slave in currentSlaves)
                {
                    // 這裡可以選擇性過濾，例如只顯示伺服驅動器 (Servo)
                    // 目前先全部加入，讓使用者自己選
                    AvailableSlaves.Add(slave);
                }
            }

            // 2. 清空目前的映射表 (AxisMaps)
            AxisMaps.Clear();

            // 3. 載入設定檔中的映射 (僅限 MapType.Axis)
            if (config.Mappings != null)
            {
                foreach (var map in config.Mappings)
                {
                    // ★★★ [關鍵修改] 加入過濾條件 ★★★
                    // 只處理類型為 "Axis" 的映射設定
                    // 如果這行沒加，IN MAP 和 OUT MAP 的設定也會跑進來變成軸
                    if (map.Type != MapType.Axis)
                    {
                        continue;
                    }

                    var item = new AxisMapItem
                    {
                        AxisName = map.LogicalName
                    };

                    // 尋找符合的硬體 (VendorID + ProductCode + Index)
                    // 這是為了在下拉選單中自動選取正確的項目
                    var matchedSlave = currentSlaves.FirstOrDefault(s =>
                        s.VendorId == map.ExpectedVendorId &&
                        s.ProductCode == map.ExpectedProductCode &&
                        s.Index == map.PhysicalIndex);

                    // 如果找到了，就設定選取項目
                    if (matchedSlave != null)
                    {
                        item.SelectedSlave = matchedSlave;
                    }

                    AxisMaps.Add(item);
                }
            }

            // [補充邏輯] 如果 Config 裡完全沒有軸映射 (例如第一次啟動)，
            // 我們應該根據 AxisSettings (軸參數) 來產生預設的空列，
            // 否則畫面會是空的，使用者沒辦法開始設定。
            // 這段邏輯視您的需求而定，若您希望「沒設定就顯示空表」則保留。
            /*
            if (AxisMaps.Count == 0 && config.Axes != null)
            {
                foreach(var axis in config.Axes)
                {
                     AxisMaps.Add(new AxisMapItem { AxisName = axis.AxisID });
                }
            }
            */
        }

        public void GenerateAxisTable(MachineConfigViewModel config)
        {
            var oldMaps = new Dictionary<string, DiscoveredSlave>();
            foreach (var item in AxisMaps)
            {
                if (item.SelectedSlave != null) oldMaps[item.AxisName] = item.SelectedSlave;
            }

            AxisMaps.Clear();

            if (config.EnableX) AddAxisRow("X Axis", oldMaps);
            if (config.EnableY) AddAxisRow("Y Axis", oldMaps);
            if (config.EnableZ) AddAxisRow("Z Axis", oldMaps);
            if (config.EnableA) AddAxisRow("A Axis", oldMaps);
            if (config.EnableB) AddAxisRow("B Axis", oldMaps);
            if (config.EnableC) AddAxisRow("C Axis", oldMaps);
        }

        private void AddAxisRow(string name, Dictionary<string, DiscoveredSlave> oldMaps)
        {
            var newItem = new AxisMapItem { AxisName = name };
            if (oldMaps.ContainsKey(name)) newItem.SelectedSlave = oldMaps[name];
            AxisMaps.Add(newItem);
        }

        public void UpdateSlaves(IEnumerable<DiscoveredSlave> slaves)
        {
            AvailableSlaves.Clear();
            foreach (var slave in slaves) AvailableSlaves.Add(slave);
        }
    }
}