// [2026-03-12] 備份/還原 ViewModel
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class BackupViewModel : ObservableObject
    {
        // [2026-03-12] 備份清單
        public ObservableCollection<BackupInfo> Backups { get; } = new();

        [ObservableProperty] private BackupInfo? _selectedBackup;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _statusText = "";

        // [2026-03-12] 載入備份清單
        [RelayCommand]
        private async Task RefreshList()
        {
            IsBusy = true;
            StatusText = "";
            try
            {
                var result = await MachineControlService.Instance.GetBackupListAsync();
                Backups.Clear();
                if (result?.Status == "Success" && result.Data != null)
                {
                    foreach (var b in result.Data)
                        Backups.Add(b);
                    StatusText = $"共 {Backups.Count} 個備份";
                }
                else
                {
                    StatusText = "讀取備份清單失敗";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"錯誤：{ex.Message}";
            }
            finally { IsBusy = false; }
        }

        // [2026-03-12] 建立備份（含前端 JSON）
        [RelayCommand]
        private async Task CreateBackup()
        {
            IsBusy = true;
            StatusText = "建立備份中...";
            try
            {
                // 收集前端設定檔
                var frontendConfigs = new Dictionary<string, string>();
                TryReadFile("MachineConfig.json", frontendConfigs, "MachineConfig");
                TryReadFile("appsettings.json", frontendConfigs, "AppSettings");
                TryReadFile("probe_settings.json", frontendConfigs, "ProbeSettings");
                TryReadFile("tool_life.json", frontendConfigs, "ToolLife");

                var result = await MachineControlService.Instance.CreateBackupAsync(frontendConfigs);
                if (result?.Status == "Success" && result.Data != null)
                {
                    StatusText = $"備份成功：{result.Data.Name}（{result.Data.FileCount} 個檔案）";
                    AlarmService.Instance.AddLog("INFO", $"Backup created: {result.Data.Name}");
                    await RefreshList();
                }
                else
                {
                    StatusText = "備份失敗";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"備份錯誤：{ex.Message}";
            }
            finally { IsBusy = false; }
        }

        // [2026-03-12] 還原備份
        [RelayCommand]
        private async Task RestoreBackup()
        {
            if (SelectedBackup == null) return;

            var confirm = MessageBox.Show(
                $"還原將覆蓋目前所有設定，機台將重新啟動。\n\n確定還原備份 [{SelectedBackup.Name}]？",
                "確認還原",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusText = $"還原中：{SelectedBackup.Name}...";
            try
            {
                var result = await MachineControlService.Instance.RestoreBackupAsync(SelectedBackup.Name);
                if (result?.Status == "Success" && result.Data != null)
                {
                    // 還原前端 JSON 設定檔
                    if (result.Data.FrontendConfigs != null)
                    {
                        foreach (var kv in result.Data.FrontendConfigs)
                        {
                            string fileName = kv.Key + ".json";
                            try
                            {
                                File.WriteAllText(fileName, kv.Value);
                            }
                            catch { /* 忽略前端檔案寫入失敗 */ }
                        }
                    }
                    StatusText = $"還原成功，機台重啟中...（還原 {result.Data.Restored?.Count ?? 0} 個檔案）";
                    AlarmService.Instance.AddLog("WARNING", $"Backup restored: {SelectedBackup.Name}, restarting...");
                }
                else
                {
                    StatusText = "還原失敗";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"還原錯誤：{ex.Message}";
            }
            finally { IsBusy = false; }
        }

        // [2026-03-12] 刪除備份
        [RelayCommand]
        private async Task DeleteBackup()
        {
            if (SelectedBackup == null) return;

            var confirm = MessageBox.Show(
                $"確定刪除備份 [{SelectedBackup.Name}]？",
                "確認刪除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            try
            {
                bool ok = await MachineControlService.Instance.DeleteBackupAsync(SelectedBackup.Name);
                if (ok)
                {
                    StatusText = $"已刪除：{SelectedBackup.Name}";
                    AlarmService.Instance.AddLog("INFO", $"Backup deleted: {SelectedBackup.Name}");
                    await RefreshList();
                }
                else
                {
                    StatusText = "刪除失敗";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"刪除錯誤：{ex.Message}";
            }
            finally { IsBusy = false; }
        }

        // [2026-03-12] 讀取前端本地檔案（若存在）
        private static void TryReadFile(string fileName, Dictionary<string, string> dict, string key)
        {
            try
            {
                if (File.Exists(fileName))
                    dict[key] = File.ReadAllText(fileName);
            }
            catch { /* 忽略 */ }
        }
    }
}
