using CommunityToolkit.Mvvm.Input;

namespace CncController.ViewModels
{
    // [2026-03-12] 從 MainViewModel.cs 拆分：頁面導航
    public partial class MainViewModel
    {
        [RelayCommand]
        private void Navigate(string viewName)
        {
            // [2026-03-10] 離開 ATC 頁面時停止輪詢
            if (CurrentViewModel == AtcVM && viewName != "Atc")
                AtcVM.StopPolling();

            switch (viewName)
            {
                case "Main": CurrentViewModel = MonitorVM; break;

                // [2026-03-10] 進入 Settings 時重新從檔案載入，丟棄未存檔的修改
                case "Settings":
                    CurrentViewModel = SettingsVM;
                    _ = SettingsVM.ReloadFromFileAsync();
                    break;

                case "History": CurrentViewModel = HistoryVM; break;
                case "Offsets": CurrentViewModel = OffsetsVM; break;
                case "Tool": CurrentViewModel = ToolTableVM; break;
                // [2026-03-04] ATC Tab 導航：自動載入程式刀具列表 + 啟動輪詢
                case "Atc":
                    CurrentViewModel = AtcVM;
                    AtcVM.LoadProgramToolsCommand.Execute(null);
                    AtcVM.StartPolling();
                    break;
                case "Probing": CurrentViewModel = ProbingVM; break;
                // [2026-03-11] FILE 分頁導航：進入時自動刷新檔案清單
                case "File":
                    CurrentViewModel = FileManagerVM;
                    FileManagerVM.InitializeCommand.Execute(null);
                    break;
            }
        }
    }
}
