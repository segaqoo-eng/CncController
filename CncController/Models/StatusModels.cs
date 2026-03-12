using System.Collections.Generic;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：狀態資料 DTO

    public class MachineStatusData
    {
        public bool Connected { get; set; }
        public string Task_State { get; set; }
        public string Interp_State { get; set; }
        public bool Is_Moving { get; set; }
        public bool Has_Error { get; set; }
        public Dictionary<string, double> Position { get; set; }
        public Dictionary<string, double> DTG { get; set; }
        public double Feedrate { get; set; }
        public double Spindle_Speed { get; set; }
        public string File { get; set; }
        public List<string> Alerts { get; set; }
        public Dictionary<string, ServoIoRawData> Servo_IO { get; set; }
        public string Active_WCS { get; set; } = "G54";
        public Dictionary<string, double> Work_Position { get; set; }
        public double Feed_Override { get; set; } = 100.0;
        public double Spindle_Override { get; set; } = 100.0;
        public int Tool_Number { get; set; }
        public double Tool_Length { get; set; }
        public double Tool_Diameter { get; set; }
        public Dictionary<string, bool> Homed { get; set; }
        public Dictionary<string, double> G92_Offset { get; set; }
        public Dictionary<string, double> Tool_Offset_XYZ { get; set; }
        public string Task_Mode { get; set; }
        public bool Probe_Input { get; set; }
        public bool Block_Delete { get; set; }
        public bool Optional_Stop { get; set; }
        public int Current_Line { get; set; }
        public double Spindle_Position { get; set; }
        public int Spindle_Direction { get; set; }
        public int Program_Total_Lines { get; set; }
        public Dictionary<string, Dictionary<string, Dictionary<string, bool>>> IO_Status { get; set; }
    }

    public class ServoIoRawData
    {
        public string DI { get; set; }
        public string Status { get; set; }
    }
}
