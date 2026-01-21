namespace CncController.Models
{
    public class MachineStatus
    {
        public bool Connected { get; set; }
        public string State { get; set; } = "UNKNOWN"; // ESTOP, ON, OFF
        // 可以在這裡擴充 PosX, PosY 等屬性
    }
}