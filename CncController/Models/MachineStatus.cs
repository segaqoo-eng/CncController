using CommunityToolkit.Mvvm.ComponentModel;

namespace CncController.Models
{
    // ★★★ 關鍵：必須是 partial 且繼承 ObservableObject ★★★
    public partial class MachineStatus : ObservableObject
    {
        [ObservableProperty] private bool _connected;
        [ObservableProperty] private string _taskState = "OFF";

        // 座標屬性 (UI 綁定的是這些大寫屬性，如 Status.X)
        [ObservableProperty] private double _x;
        [ObservableProperty] private double _y;
        [ObservableProperty] private double _z;
        [ObservableProperty] private double _a;
        [ObservableProperty] private double _b;
        [ObservableProperty] private double _c;

        [ObservableProperty] private double _feedrate;
        [ObservableProperty] private double _spindleSpeed;
        [ObservableProperty] private string _file = "No File Loaded";
    }
}