using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：EtherCAT 掃描/裝置相關

    public static class DeviceCategory
    {
        public const string Servo = "Servo";
        public const string PulseGen = "PulseGen";
        public const string DiDo = "DI+DO";
        public const string DigIn = "DigIn";
        public const string DigOut = "DigOut";
        public const string Mpg = "MPG";
        public const string DA = "DA";
        public const string AD = "AD";
        public const string Coupler = "Coupler";
        public const string Unknown = "Unknown";
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MapType
    {
        Axis,
        Input,
        Output
    }

    public class DiscoveredSlave
    {
        [JsonPropertyName("Slave")] public int Index { get; set; }
        [JsonPropertyName("VendorId")] public string VendorId { get; set; }
        [JsonPropertyName("ProductCode")] public string ProductCode { get; set; }
        [JsonPropertyName("Name")] public string Name { get; set; }
        [JsonPropertyName("Group")] public string VendorGroup { get; set; }
        [JsonPropertyName("Model")] public string ProductModel { get; set; }
        [JsonPropertyName("Category")] public string Category { get; set; }
        [JsonPropertyName("Source")] public string Source { get; set; }
        [JsonPropertyName("Mapped PDOs")] public string Pdos { get; set; }
        [JsonIgnore] public string DisplayName => $"#{Index}: {Name} ({VendorId})";
    }

    public enum SlaveDeviceType
    {
        Servo,
        PulseGenerator,
        IoModule,
        DigitalInput,
        DigitalOutput,
        Mpg,
        Coupler,
        Unknown
    }

    public class HardwareMapping
    {
        public string LogicalName { get; set; }
        public string PhysicalAddress { get; set; }
        public int PhysicalIndex { get; set; }
        public string ExpectedVendorId { get; set; }
        public string ExpectedProductCode { get; set; }
        public MapType Type { get; set; } = MapType.Axis;
        public int ChannelIndex { get; set; }
        public List<PinConfig> Pins { get; set; } = new();
        public string DeviceCategory { get; set; } = "";
    }

    public class PinConfig
    {
        public int Index { get; set; }
        public string Function { get; set; }
        public bool IsInverted { get; set; }
    }
}
