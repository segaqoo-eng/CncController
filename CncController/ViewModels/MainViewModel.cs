

// 檔案：ViewModels/MainViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CncController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // 為了避免複雜，主畫面我們先用一個簡單的字串代表
        public string MainViewVM { get; } = "MainView";

        public SettingsViewModel SettingsVM { get; } = new SettingsViewModel();

        [ObservableProperty]
        private object _currentViewModel;

        public MainViewModel()
        {
            CurrentViewModel = MainViewVM; // 預設顯示主畫面
        }

        [RelayCommand]
        private void Navigate(string destination)
        {
            if (destination == "Main")
            {
                CurrentViewModel = MainViewVM;
            }
            else if (destination == "Settings")
            {
                CurrentViewModel = SettingsVM;
            }
        }
    }
}