namespace CncController.Models
{
    public enum SlotType
    {
        MotionAxis,
        DigitalInput,
        DigitalOutput,
        AnalogInput,
        AnalogOutput,
        MPG
    }

    public enum IoModuleType
    {
        DigitalInput,
        DigitalOutput,
        AnalogInput,
        AnalogOutput,
        MPG
    }

    public enum LogType
    {
        Info, Warning, Error, Debug
    }
    // ==========================================
    // 1. 定義 Enums (放在最上面或獨立檔案皆可)
    // ==========================================

    // 硬體極限邏輯
    public enum LimitLogic
    {
        NotUsed = 0, // 不使用 (例如旋轉軸)
        NO = 1,      // 常開 (Normal Open)
        NC = 2       // 常閉 (Normal Close) - 工業推薦
    }

    // 回原點模式
    public enum HomingMode
    {
        Immediate = 0,  // 立即 (原地設為0)
        HomeSwitch = 1, // 使用獨立原點開關
        LimitSwitch = 2 // 使用極限開關當原點 (共用極限)
    }

}