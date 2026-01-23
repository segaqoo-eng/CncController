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
            AvailableSlaves.Clear();
            foreach (var slave in currentSlaves) AvailableSlaves.Add(slave);

            AxisMaps.Clear();

            if (config.Mappings != null)
            {
                foreach (var map in config.Mappings)
                {
                    var item = new AxisMapItem { AxisName = map.LogicalName };

                    // 尋找符合的硬體 (VendorID + ProductCode + Index)
                    var matchedSlave = currentSlaves.FirstOrDefault(s =>
                        s.VendorId == map.ExpectedVendorId &&
                        s.ProductCode == map.ExpectedProductCode &&
                        s.Index == map.PhysicalIndex);

                    if (matchedSlave != null) item.SelectedSlave = matchedSlave;

                    AxisMaps.Add(item);
                }
            }
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