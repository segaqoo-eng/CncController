using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    // [2026-03-12] 維護保養提醒 ViewModel（Settings MAINTENANCE 分頁）
    public partial class MaintenanceViewModel : ObservableObject
    {
        [ObservableProperty] private ObservableCollection<MaintenanceItem> _items = new();
        [ObservableProperty] private MaintenanceItem _selectedItem;
        [ObservableProperty] private string _statusText = "";

        // 新增項目用
        [ObservableProperty] private string _newName = "";
        [ObservableProperty] private double _newIntervalHours = 500;

        [RelayCommand]
        private async Task LoadItems()
        {
            try
            {
                var list = await MachineControlService.Instance.GetMaintenanceAsync();
                Items.Clear();
                if (list != null)
                {
                    foreach (var item in list)
                        Items.Add(item);
                }
                StatusText = $"已載入 {Items.Count} 筆保養項目";
            }
            catch (Exception ex)
            {
                StatusText = $"載入失敗：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task AddItem()
        {
            if (string.IsNullOrWhiteSpace(NewName))
            {
                StatusText = "請輸入保養項目名稱";
                return;
            }

            Items.Add(new MaintenanceItem
            {
                Name = NewName,
                IntervalHours = NewIntervalHours,
                AccumulatedHours = 0
            });
            NewName = "";
            await SaveItems();
        }

        [RelayCommand]
        private async Task RemoveItem()
        {
            if (SelectedItem == null) return;
            Items.Remove(SelectedItem);
            await SaveItems();
        }

        [RelayCommand]
        private async Task ResetItem()
        {
            if (SelectedItem == null) return;
            try
            {
                await MachineControlService.Instance.ResetMaintenanceItemAsync(SelectedItem.Name);
                SelectedItem.AccumulatedHours = 0;
                StatusText = $"已重置「{SelectedItem.Name}」累計時數";
                await LoadItems(); // 重新載入
            }
            catch (Exception ex)
            {
                StatusText = $"重置失敗：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SaveItems()
        {
            try
            {
                var list = new System.Collections.Generic.List<MaintenanceItem>(Items);
                await MachineControlService.Instance.SaveMaintenanceAsync(list);
                StatusText = "保養設定已儲存";
            }
            catch (Exception ex)
            {
                StatusText = $"儲存失敗：{ex.Message}";
            }
        }
    }
}
