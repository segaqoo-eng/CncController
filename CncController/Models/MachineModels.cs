using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CncController.Models
{
    // 1. 掃描到的 EtherCAT 裝置 (完全對應 26.01.23.scan_report.csv)
    public class DiscoveredSlave
    {
        // CSV: Slave
        [JsonPropertyName("Slave")]
        public int Index { get; set; }

        // CSV: VendorId (新增)
        [JsonPropertyName("VendorId")]
        public string VendorId { get; set; }

        // CSV: ProductCode (新增)
        [JsonPropertyName("ProductCode")]
        public string ProductCode { get; set; }

        // CSV: Name
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        // CSV: Group
        [JsonPropertyName("Group")]
        public string VendorGroup { get; set; }

        // CSV: Model
        [JsonPropertyName("Model")]
        public string ProductModel { get; set; }

        // CSV: Category
        [JsonPropertyName("Category")]
        public string Category { get; set; }

        // CSV: Source
        [JsonPropertyName("Source")]
        public string Source { get; set; }

        // CSV: Mapped PDOs
        [JsonPropertyName("Mapped PDOs")]
        public string Pdos { get; set; }

        // 顯示名稱 (輔助用)
        [JsonIgnore]
        public string DisplayName => $"#{Index}: {Name} ({VendorId})";
    }

    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;
        public List<AxisSetting> Axes { get; set; } = new();
        public List<IoSetting> IoMappings { get; set; } = new();
        public List<HardwareMapping> Mappings { get; set; } = new List<HardwareMapping>();
    }

    public class HardwareMapping
    {
        public string LogicalName { get; set; }
        public string PhysicalAddress { get; set; }
        public int PhysicalIndex { get; set; }
        public string ExpectedVendorId { get; set; }
        public string ExpectedProductCode { get; set; }
    }

    public class AxisSetting
    {
        public int Index { get; set; }
        public string AxisID { get; set; } = "X";
        public string Name { get; set; } = "X Axis";
        public double Pitch { get; set; } = 10.0;
        public double PulsePerRev { get; set; } = 10000;
        public double SoftLimitPos { get; set; } = 100.0;
        public double SoftLimitNeg { get; set; } = -100.0;
        public double HomeSpeed { get; set; } = 20.0;
        public int HomeDirection { get; set; } = 1;
    }

    public class IoSetting
    {
        public int StationIndex { get; set; }
        public int PinIndex { get; set; }
        public string Function { get; set; } = string.Empty;
        public bool Invert { get; set; }
    }
}