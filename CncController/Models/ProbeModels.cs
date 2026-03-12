namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：探測相關

    public class ProbeResult
    {
        public bool Tripped { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Error { get; set; }
        public double Angle { get; set; }
        public double EdgeWidth { get; set; }
        public double WidthX { get; set; }
        public double WidthY { get; set; }
    }

    public class ProbeParameters
    {
        public int ProbeToolNumber { get; set; } = 0;
        public double TraverseSpeed { get; set; } = 300.0;
        public double SearchSpeed { get; set; } = 50.0;
        public double MaxXYDistance { get; set; } = 20.0;
        public double MaxZDistance { get; set; } = 20.0;
        public double XYClearance { get; set; } = 5.0;
        public double ZClearance { get; set; } = 5.0;
        public double ExtraDepth { get; set; } = 2.0;
        public double Diameter { get; set; } = 20.0;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double EdgeWidth { get; set; }
    }
}
