// [2026-03-11] 新增 FileManagerViewModel：FILE 分頁 — 後端檔案管理
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class FileManagerViewModel : ObservableObject
    {
        // [2026-03-11] 後端檔案清單
        public ObservableCollection<ProgramFileInfo> ProgramFiles { get; } = new();

        [ObservableProperty] private ProgramFileInfo _selectedFile;
        [ObservableProperty] private string _previewText = "";
        [ObservableProperty] private string _previewFileName = "";
        [ObservableProperty] private bool _isPreviewEmpty = true;

        // [2026-03-11] 重命名
        [ObservableProperty] private string _renameInput = "";
        [ObservableProperty] private bool _isRenaming = false;

        // [2026-03-11] G-Code 逐行集合（供行號顯示）
        public ObservableCollection<GCodeLineItem> PreviewLines { get; } = new();

        public FileManagerViewModel()
        {
        }

        // [2026-03-11] 初始化（進入頁面時呼叫）
        [RelayCommand]
        public async Task Initialize()
        {
            await RefreshFileList();
        }

        // [2026-03-11] 刷新檔案清單
        [RelayCommand]
        private async Task RefreshFileList()
        {
            var files = await MachineControlService.Instance.GetProgramListAsync();
            ProgramFiles.Clear();
            if (files != null)
            {
                foreach (var f in files)
                    ProgramFiles.Add(f);
            }
        }

        // [2026-03-11] 選取檔案時自動預覽
        partial void OnSelectedFileChanged(ProgramFileInfo value)
        {
            if (value != null)
                _ = PreviewFile(value.Name);
            else
            {
                PreviewText = "";
                PreviewFileName = "";
                IsPreviewEmpty = true;
                PreviewLines.Clear();
            }
        }

        // [2026-03-11] 回讀並預覽檔案內容
        private async Task PreviewFile(string fileName)
        {
            var content = await MachineControlService.Instance.ReadProgramAsync(fileName);
            if (content != null)
            {
                PreviewText = content;
                PreviewFileName = fileName;
                IsPreviewEmpty = false;

                // 解析為逐行集合
                PreviewLines.Clear();
                var lines = content.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    PreviewLines.Add(new GCodeLineItem
                    {
                        LineNumber = i + 1,
                        Text = lines[i].TrimEnd('\r')
                    });
                }
            }
            else
            {
                PreviewText = "";
                PreviewFileName = fileName;
                IsPreviewEmpty = true;
                PreviewLines.Clear();
            }
        }

        // [2026-03-11] 設定為加工程式（load 到 LinuxCNC）
        [RelayCommand]
        private async Task LoadProgram()
        {
            if (SelectedFile == null) return;
            bool success = await MachineControlService.Instance.LoadProgramAsync(SelectedFile.Name);
            if (success)
                AlarmService.Instance.AddLog("INFO", $"Program Set: {SelectedFile.Name}");
            else
                AlarmService.Instance.AddLog("ERR", $"Program Load Failed: {SelectedFile.Name}");
        }

        // [2026-03-11] 刪除
        [RelayCommand]
        private async Task DeleteFile()
        {
            if (SelectedFile == null) return;
            string name = SelectedFile.Name;
            bool success = await MachineControlService.Instance.DeleteProgramAsync(name);
            if (success)
            {
                AlarmService.Instance.AddLog("INFO", $"Deleted: {name}");
                if (PreviewFileName == name)
                {
                    PreviewText = "";
                    PreviewFileName = "";
                    IsPreviewEmpty = true;
                    PreviewLines.Clear();
                }
                await RefreshFileList();
            }
            else
                AlarmService.Instance.AddLog("ERR", $"Delete Failed: {name}");
        }

        // [2026-03-11] 開始重命名
        [RelayCommand]
        private void StartRename()
        {
            if (SelectedFile == null) return;
            RenameInput = SelectedFile.Name;
            IsRenaming = true;
        }

        // [2026-03-11] 確認重命名
        [RelayCommand]
        private async Task ConfirmRename()
        {
            if (SelectedFile == null || string.IsNullOrWhiteSpace(RenameInput)) return;
            string oldName = SelectedFile.Name;
            string newName = RenameInput.Trim();
            if (oldName == newName) { IsRenaming = false; return; }

            bool success = await MachineControlService.Instance.RenameProgramAsync(oldName, newName);
            if (success)
            {
                AlarmService.Instance.AddLog("INFO", $"Renamed: {oldName} -> {newName}");
                if (PreviewFileName == oldName)
                    PreviewFileName = newName;
                await RefreshFileList();
            }
            else
                AlarmService.Instance.AddLog("ERR", $"Rename Failed: {oldName}");
            IsRenaming = false;
        }

        // [2026-03-11] 取消重命名
        [RelayCommand]
        private void CancelRename()
        {
            IsRenaming = false;
            RenameInput = "";
        }
    }
}
