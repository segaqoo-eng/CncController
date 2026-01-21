// Models/AxisConfig.cs
public class AxisConfig
{
    public string Name { get; set; }        // 軸名稱 (X, Y, Z)
    public double Pitch { get; set; }       // 導程
    public int PulseRev { get; set; }       // 每轉脈衝
    public double SoftLimit { get; set; }   // 軟限位
    public double HomeSpeed { get; set; }   // 歸原點速度
}