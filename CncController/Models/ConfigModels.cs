using System.Collections.Generic;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：機台設定相關

    public enum MachineType
    {
        ThreeAxis,
        FourAxisA,
        FourAxisB,
        FiveAxisTrunnion,
        FiveAxisSwivel,
        SixAxis
    }

    public enum AtcType
    {
        None,
        Turret,
        Umbrella,
        SideMount
    }

    public enum CarouselControlMode
    {
        Servo,
        IO
    }

    public class SpindleConfig
    {
        public int SlaveIndex { get; set; } = -1;
        public int EncoderPPR { get; set; } = 4096;
        public double MaxRPM { get; set; } = 8000;
        public double MaxAccel { get; set; } = 2000;
        public double OrientAngle { get; set; } = 0.0;
        public bool RigidTappingEnabled { get; set; } = true;
    }

    public class AtcConfig
    {
        public AtcType Type { get; set; } = AtcType.None;
        public int ToolCount { get; set; } = 12;
        public CarouselControlMode ControlMode { get; set; } = CarouselControlMode.Servo;
        public int CarouselSlaveIndex { get; set; } = -1;
        public double CarouselMaxVel { get; set; } = 90.0;
        public double CarouselMaxAccel { get; set; } = 360.0;
        public int CarouselPulsePerRev { get; set; } = 10000;
        public double CarouselPitch { get; set; } = 360.0;
        public int IoSlaveIndex { get; set; } = -1;
        public int DoCarouselOut { get; set; } = 31;
        public int DoCarouselHome { get; set; } = 30;
        public int DoDrawbar { get; set; } = 29;
        public int DoAirBlow { get; set; } = 28;
        public int DoMotorFwd { get; set; } = 27;
        public int DoMotorRev { get; set; } = 26;
        public int DiCarouselHome { get; set; } = 31;
        public int DiCarouselOut { get; set; } = 30;
        public int DiDrawbarClamp { get; set; } = 29;
        public int DiDrawbarUnclamp { get; set; } = 28;
        public int DiRotationIndex { get; set; } = 27;
        public int ClampDwell { get; set; } = 1000;
        public int UnclampDwell { get; set; } = 1000;
        public int AirBlowDwell { get; set; } = 300;
        public int SensorTimeout { get; set; } = 5000;
        public double ZToolChangeHeight { get; set; } = -3.9;
        public double ZClearanceHeight { get; set; } = 0.0;
        public double RackTraverseSpeed { get; set; } = 3000;
        public double RackPocket1X { get; set; } = 0.0;
        public double RackPocket1Y { get; set; } = 0.0;
        public double RackPocket2X { get; set; } = 0.0;
        public double RackPocket2Y { get; set; } = 0.0;
        public double RackClearanceX { get; set; } = 0.0;
        public double RackClearanceY { get; set; } = 0.0;
    }

    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;
        public MachineType MachineType { get; set; } = MachineType.ThreeAxis;
        public List<AxisSetting> Axes { get; set; } = new();
        public List<IoSetting> IoMappings { get; set; } = new();
        public List<HardwareMapping> Mappings { get; set; } = new();
        public AtcConfig Atc { get; set; } = new();
        public SpindleConfig Spindle { get; set; } = new();

        public List<string> GetEnabledAxes()
        {
            return MachineType switch
            {
                MachineType.FourAxisA => new() { "X", "Y", "Z", "A" },
                MachineType.FourAxisB => new() { "X", "Y", "Z", "B" },
                MachineType.FiveAxisTrunnion => new() { "X", "Y", "Z", "A", "C" },
                MachineType.FiveAxisSwivel => new() { "X", "Y", "Z", "B", "C" },
                MachineType.SixAxis => new() { "X", "Y", "Z", "A", "B", "C" },
                _ => new() { "X", "Y", "Z" }
            };
        }
    }

    public class AxisSetting
    {
        public int Index { get; set; }
        public string AxisID { get; set; } = "X";
        public string Name { get; set; } = "X Axis";
        public double Pitch { get; set; } = 5.0;
        public double PulsePerRev { get; set; } = 10000;
        public double MaxVelocity { get; set; } = 100.0;
        public double MaxAcceleration { get; set; } = 500.0;
        public bool InvertMotor { get; set; } = false;
        public double SoftLimitPos { get; set; } = 100.0;
        public double SoftLimitNeg { get; set; } = -100.0;
        public HomingMode HomingMode { get; set; } = HomingMode.HomeSwitch;
        public double HomeSpeed { get; set; } = 10.0;
        public double HomeLatchSpeed { get; set; } = 1.0;
        public int HomeDirection { get; set; } = 1;
        public double HomeOffset { get; set; } = 0.0;
        public int HomeSequence { get; set; } = 0;
        public bool HomeUseIndex { get; set; } = true;
        public int HomeDiIndex { get; set; } = 0;
        public int PosLimitDiIndex { get; set; } = 1;
        public int NegLimitDiIndex { get; set; } = 2;
        public LimitLogic LimitSwitchLogic { get; set; } = LimitLogic.NC;
        public LimitLogic HomeSwitchLogic { get; set; } = LimitLogic.NO;
    }

    public class IoSetting
    {
        public int StationIndex { get; set; }
        public int PinIndex { get; set; }
        public string Function { get; set; } = string.Empty;
        public bool Invert { get; set; }
    }
}
