using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

        // [2026-03-11] 機台端程式檔案清單
        public ObservableCollection<ProgramFileInfo> ProgramFiles { get; } = new();

        // [2026-03-11] 檔案清單選取項目
        [ObservableProperty] private ProgramFileInfo _selectedProgramFile;

        // [2026-03-11] 檔案面板展開狀態
        [ObservableProperty] private bool _isFilePanelOpen = false;

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

        // [2026-03-11] 開啟舊檔：本地開檔僅預覽，不上傳到後端
        [RelayCommand]
        private async Task OpenLocalFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "G-Code Files (*.nc;*.ngc;*.cnc;*.tap)|*.nc;*.ngc;*.cnc;*.tap|All Files (*.*)|*.*",
                Title = "開啟本機 G-Code 檔案"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string localPath = dialog.FileName;
                    string fileName = Path.GetFileName(localPath);
                    string content = await File.ReadAllTextAsync(localPath);

                    GCodeText = content;
                    CurrentFileName = fileName;
                    AlarmService.Instance.AddLog("INFO", $"File Opened (Local): {fileName}");
                }
                catch (Exception ex)
                {
                    AlarmService.Instance.AddLog("ERR", $"Open Error: {ex.Message}");
                }
            }
        }

        // [情境 3.2] 本機開檔 → 預覽 → 上傳到後端
        [RelayCommand]
        private async Task LoadLocalFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "G-Code Files (*.nc;*.ngc;*.cnc;*.tap)|*.nc;*.ngc;*.cnc;*.tap|All Files (*.*)|*.*",
                Title = "Select G-Code File"
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
                        // [2026-03-11] 上傳成功後刷新檔案清單
                        await RefreshProgramList();
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
                // [2026-03-11] 上傳成功後刷新檔案清單
                await RefreshProgramList();
            }
            else
            {
                AlarmService.Instance.AddLog("ERR", $"CAM Upload Failed: {jobName}");
            }
        }

        // =========================================================
        // [2026-03-11] 程式檔案管理
        // =========================================================

        // 切換檔案面板顯示
        [RelayCommand]
        private async Task ToggleFilePanel()
        {
            IsFilePanelOpen = !IsFilePanelOpen;
            if (IsFilePanelOpen)
                await RefreshProgramList();
        }

        // 刷新機台端檔案清單
        [RelayCommand]
        private async Task RefreshProgramList()
        {
            var files = await MachineControlService.Instance.GetProgramListAsync();
            ProgramFiles.Clear();
            if (files != null)
            {
                foreach (var f in files)
                    ProgramFiles.Add(f);
            }
        }

        // 選取檔案 → 載入到 LinuxCNC
        [RelayCommand]
        private async Task SelectProgramFile()
        {
            if (SelectedProgramFile == null) return;
            string fileName = SelectedProgramFile.Name;

            bool success = await MachineControlService.Instance.LoadProgramAsync(fileName);
            if (success)
            {
                CurrentFileName = fileName;
                // [2026-03-11] 清空預覽（A 模式不回讀內容）
                GCodeText = "";
                AlarmService.Instance.AddLog("INFO", $"Program Loaded: {fileName}");
            }
            else
            {
                AlarmService.Instance.AddLog("ERR", $"Program Load Failed: {fileName}");
            }
        }

        // 刪除選取的檔案
        [RelayCommand]
        private async Task DeleteProgramFile()
        {
            if (SelectedProgramFile == null) return;
            string fileName = SelectedProgramFile.Name;

            bool success = await MachineControlService.Instance.DeleteProgramAsync(fileName);
            if (success)
            {
                AlarmService.Instance.AddLog("INFO", $"Program Deleted: {fileName}");
                // [2026-03-11] 若刪除的是當前載入的檔案，清空顯示
                if (CurrentFileName == fileName)
                {
                    CurrentFileName = "";
                    GCodeText = "";
                }
                await RefreshProgramList();
            }
            else
            {
                AlarmService.Instance.AddLog("ERR", $"Delete Failed: {fileName}");
            }
        }

        // 重命名選取的檔案
        // [2026-03-11] 新檔名由 RenameInput 屬性提供
        [ObservableProperty] private string _renameInput = "";
        [ObservableProperty] private bool _isRenaming = false;

        [RelayCommand]
        private void StartRename()
        {
            if (SelectedProgramFile == null) return;
            RenameInput = SelectedProgramFile.Name;
            IsRenaming = true;
        }

        [RelayCommand]
        private async Task ConfirmRename()
        {
            if (SelectedProgramFile == null || string.IsNullOrWhiteSpace(RenameInput)) return;
            string oldName = SelectedProgramFile.Name;
            string newName = RenameInput.Trim();

            if (oldName == newName)
            {
                IsRenaming = false;
                return;
            }

            bool success = await MachineControlService.Instance.RenameProgramAsync(oldName, newName);
            if (success)
            {
                AlarmService.Instance.AddLog("INFO", $"Renamed: {oldName} → {newName}");
                if (CurrentFileName == oldName)
                    CurrentFileName = newName;
                await RefreshProgramList();
            }
            else
            {
                AlarmService.Instance.AddLog("ERR", $"Rename Failed: {oldName}");
            }
            IsRenaming = false;
        }

        [RelayCommand]
        private void CancelRename()
        {
            IsRenaming = false;
            RenameInput = "";
        }

        // [2026-03-11] 格式化檔案大小（KB/MB）
        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
