using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class MonitorViewModel : ObservableObject
    {
        // G-Code 預覽文字
        [ObservableProperty]
        private string _gCodeText = "";

        // [2026-03-04] 空狀態標記（GCodeLines 為空時顯示提示）
        [ObservableProperty] private bool _isGCodeEmpty = true;

        // 目前載入的檔名 (用於顯示與記錄)
        [ObservableProperty]
        private string _currentFileName = "";

        // MDI 輸入框文字
        [ObservableProperty]
        private string _mdiInput = "";

        // MDI 歷史紀錄 (最近 20 筆，最新在最前)
        public ObservableCollection<string> MdiHistory { get; } = new();

        // [2026-03-04] G-Code 逐行集合（供 MonitorView ItemsControl 行號高亮使用）
        public ObservableCollection<GCodeLineItem> GCodeLines { get; } = new();

        // [2026-03-04] MachineStatus 參考（從 MainViewModel 傳入，用於追蹤 CurrentLine）
        private MachineStatus _machineStatus;
        public MachineStatus MachineStatus
        {
            get => _machineStatus;
            set
            {
                if (_machineStatus != null)
                    _machineStatus.PropertyChanged -= OnMachineStatusChanged;
                _machineStatus = value;
                if (_machineStatus != null)
                    _machineStatus.PropertyChanged += OnMachineStatusChanged;
            }
        }

        // [2026-03-04] 記錄上一次高亮的行號，避免每次輪詢都重掃
        private int _lastHighlightedLine = -1;

        public MonitorViewModel()
        {
            // 初始化邏輯
        }

        // [2026-03-04] GCodeText 變更時同步解析為 GCodeLines 集合
        partial void OnGCodeTextChanged(string value)
        {
            GCodeLines.Clear();
            _lastHighlightedLine = -1;

            if (string.IsNullOrEmpty(value))
            {
                IsGCodeEmpty = true;
                return;
            }

            var lines = value.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                GCodeLines.Add(new GCodeLineItem
                {
                    LineNumber = i + 1,
                    Text = lines[i].TrimEnd('\r')
                });
            }
            IsGCodeEmpty = false;
        }

        // [2026-03-04] 當 MachineStatus.CurrentLine 變化時更新高亮行
        private void OnMachineStatusChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Models.MachineStatus.CurrentLine)) return;

            int newLine = _machineStatus?.CurrentLine ?? 0;
            if (newLine == _lastHighlightedLine) return;

            // 清除舊行高亮
            if (_lastHighlightedLine > 0 && _lastHighlightedLine <= GCodeLines.Count)
                GCodeLines[_lastHighlightedLine - 1].IsCurrentLine = false;

            // 設定新行高亮
            if (newLine > 0 && newLine <= GCodeLines.Count)
                GCodeLines[newLine - 1].IsCurrentLine = true;

            _lastHighlightedLine = newLine;
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
