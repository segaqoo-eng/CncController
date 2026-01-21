using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;

namespace CncController.ViewModels
{
    public partial class AxisConfigViewModel : ObservableObject
    {
        public ObservableCollection<AxisSetting> Axes { get; } = new();

        public AxisConfigViewModel()
        {
            // 預設產生 3 軸
            Axes.Add(new AxisSetting { AxisID = "X", Name = "X Axis", Pitch = 10, PulsePerRev = 10000 });
            Axes.Add(new AxisSetting { AxisID = "Y", Name = "Y Axis", Pitch = 10, PulsePerRev = 10000 });
            Axes.Add(new AxisSetting { AxisID = "Z", Name = "Z Axis", Pitch = 5, PulsePerRev = 10000 });
        }
    }
}