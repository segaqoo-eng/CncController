using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class MonitorViewModel : ObservableObject
    {
        // G-Code 預覽文字
        [ObservableProperty]
        private string _gCodeText = "; No Program Loaded";

        // 目前載入的檔名 (用於顯示與記錄)
        [ObservableProperty]
        private string _currentFileName = "";

        // MDI 輸入框文字
        [ObservableProperty]
        private string _mdiInput = "";

        // MDI 歷史紀錄 (最近 20 筆，最新在最前)
        public ObservableCollection<string> MdiHistory { get; } = new();

        public MonitorViewModel()
        {
            // 初始化邏輯
        }

        // =========================================================
        // 檔案載入功能 (滿足 3.1 & 3.2 需求)
        // =========================================================

        // [情境 3.2] 本機測試用：開啟檔案選擇器
        // 用法：在 UI 上綁定 Command="{Binding LoadLocalFileCommand}"
        [RelayCommand]
        private async Task LoadLocalFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "G-Code Files (*.nc;*.ngc;*.tap)|*.nc;*.ngc;*.tap|All Files (*.*)|*.*",
                Title = "Select G-Code File (Test Mode)"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string localPath = dialog.FileName;
                    string fileName = Path.GetFileName(localPath);
                    string content = await File.ReadAllTextAsync(localPath);

                    // 1. 顯示在 UI (僅預覽，記憶體不落地原則是指 Server 端不存檔，這裡只是前端顯示)
                    GCodeText = content;
                    CurrentFileName = fileName;

                    // 2. 上傳到 Server (記憶體不落地，直接發送內容)
                    // 這裡呼叫 Service 的上傳方法
                    bool success = await MachineControlService.Instance.UploadGCodeAsync(fileName, content);

                    if (success)
                    {
                        AlarmService.Instance.AddLog("INFO", $"File Loaded: {fileName}");

                        // [重要] 通知 MainViewModel (如果有的話) 更新當前檔案狀態
                        // 這裡可以透過 Messenger 或直接依賴 Service 的狀態
                    }
                    else
                    {
                        AlarmService.Instance.AddLog("ERR", "File Upload Failed");
                    }
                }
                catch (Exception ex)
                {
                    AlarmService.Instance.AddLog("ERR", $"Load Error: {ex.Message}");
                }
            }
        }

        // MDI 送出指令
        [RelayCommand]
        private async Task SendMdi()
        {
            string cmd = MdiInput.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            // [安全] 前置狀態守衛：透過 ValidateAction 確認機台允許 MDI
            var (allowed, reason) = MachineControlService.Instance.ValidateAction(
                MachineControlService.MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"MDI Blocked: {reason}");
                return;
            }

            bool success = await MachineControlService.Instance.SendMdiCommandAsync(cmd);

            if (success)
            {
                // 加入歷史紀錄：若與最後一筆相同則不重複
                if (MdiHistory.Count == 0 || MdiHistory[0] != cmd)
                {
                    MdiHistory.Insert(0, cmd);
                    if (MdiHistory.Count > 20)
                        MdiHistory.RemoveAt(MdiHistory.Count - 1);
                }
                AlarmService.Instance.AddLog("INFO", $"MDI: {cmd}");
                MdiInput = "";
            }
        }

        // [情境 3.1] CAM 整合用：直接接收字串 (不讓使用者選檔)
        public async Task LoadFromCam(string camGCode, string jobName)
        {
            GCodeText = camGCode;
            CurrentFileName = jobName;

            // 靜默上傳
            bool success = await MachineControlService.Instance.UploadGCodeAsync(jobName, camGCode);
            if (success)
            {
                AlarmService.Instance.AddLog("INFO", $"CAM Job Loaded: {jobName}");
            }
            else
            {
                AlarmService.Instance.AddLog("ERR", $"CAM Upload Failed: {jobName}");
            }
        }
    }
}