using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;

namespace CncController.ViewModels
{
    public partial class AxisParameterViewModel : ObservableObject
    {
        // 管理所有軸的參數清單
        public ObservableCollection<AxisSetting> Axes { get; } = new();

        public AxisParameterViewModel()
        {
            // 預設初始化 3 軸 (X, Y, Z)，防止畫面一片空白
            Axes.Add(new AxisSetting { AxisID = "X", Name = "X Axis", Pitch = 10, PulsePerRev = 10000 });
            Axes.Add(new AxisSetting { AxisID = "Y", Name = "Y Axis", Pitch = 10, PulsePerRev = 10000 });
            Axes.Add(new AxisSetting { AxisID = "Z", Name = "Z Axis", Pitch = 5, PulsePerRev = 10000 });
        }
    }
}