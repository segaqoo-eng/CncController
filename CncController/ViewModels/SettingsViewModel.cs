using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.ObjectModel; // [新增]
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Services;
using CncController.Models;
using System.Linq;
using System.Windows.Media;

namespace CncController.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        public HardwareDiscoveryViewModel HardwareVM { get; } = new();
        public AxisParameterViewModel AxisVM { get; } = new();
        public MachineConfigViewModel MachineConfigVM { get; } = new();
        public AxisMappingViewModel MappingVM { get; } = new();

        // ★★★ [新增] IO 監控 ViewModel ★★★
        public IoMonitorViewModel IoMonitorVM { get; } = new();

        // [2026-03-06] ATC 設定 3 個子 ViewModel
        public AtcBasicSettingsViewModel AtcBasicVM { get; } = new();
        public AtcAxisSettingsViewModel AtcAxisVM { get; } = new();
        public AtcIoSettingsViewModel AtcIoVM { get; } = new();

        // [2026-03-06] 主軸設定 ViewModel
        public SpindleSettingsViewModel SpindleVM { get; } = new();

        // [2026-03-12] 巨集變數監控 ViewModel
        public MacroVariablesViewModel MacroVM { get; } = new();
        // [2026-03-12] 備份/還原 ViewModel
        public BackupViewModel BackupVM { get; } = new();
        // [2026-03-12] 維護保養 ViewModel
        public MaintenanceViewModel MaintenanceVM { get; } = new();

        // =========================================================
        // ★★★ [新增] IO 映射集合 (綁定到 DataGrid) ★★★
        // =========================================================
        public ObservableCollection<IoMapItem> InMaps { get; } = new();
        public ObservableCollection<IoMapItem> OutMaps { get; } = new();

        // [2026-03-05] 移除 AvailableInputSlaves / AvailableOutputSlaves（改為純列表模式，不再需要下拉選單）

        [ObservableProperty]
        private string _deployStatus = "Ready";

        [ObservableProperty]
        private bool _canEditHardware;

        // 用於顯示錯誤日誌
        [ObservableProperty]
        private string _lastErrorLog;

        // [新增] 掃描驗證狀態
        [ObservableProperty]
        private string _scanResultText = "Not Verified";

        [ObservableProperty]
        private Brush _scanResultColor = Brushes.Gray;

        // [2026-03-04] IN MAP / OUT MAP 自動選取第一個有設備的列（-1 = 無選取）
        [ObservableProperty] private int _selectedInMapIndex = -1;
        [ObservableProperty] private int _selectedOutMapIndex = -1;

        // [2026-03-10] 快取最後載入的設定，供 RebuildIoMapsFromScan 自動還原 pin 名稱
        private MachineConfig _lastConfig;

        // ★★★ [新增] 提供給 UI 綁定的訊號清單 ★★★
        public List<string> CommonOutputSignals => StandardSignals.OutputSignals;

        // (選用) 輸入訊號清單
        public List<string> CommonInputSignals => StandardSignals.InputSignals;

        public SettingsViewModel()
        {
            // [2026-03-05] 移除 InitializeIoMaps()（掃描前不顯示任何列，由 RebuildIoMapsFromScan 動態生成）

            // 2. 內部連動：當 HardwareVM 的 Slaves 變動時（手動 Scan Bus），更新所有下拉選單
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                // [2026-02-24] AXIS MAPPING 下拉
                MappingVM.UpdateSlaves(HardwareVM.Slaves);

                // [2026-03-05] IN MAP / OUT MAP 動態重建（從掃描結果直接生成列表）
                RebuildIoMapsFromScan(HardwareVM.Slaves);

                // [2026-03-06] ATC 軸/IO 下拉更新
                AtcAxisVM.UpdateSlaves(HardwareVM.Slaves);
                AtcIoVM.UpdateSlaves(HardwareVM.Slaves);
                SpindleVM.UpdateSlaves(HardwareVM.Slaves);
            };

            // [2026-03-06] ATC 類型即時連動：BASIC 切換類型/控制模式 → IO 頁顯示對應 DO/DI
            AtcBasicVM.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AtcBasicVM.SelectedAtcType))
                    AtcIoVM.CurrentAtcType = AtcBasicVM.SelectedAtcType;
                // [2026-03-09] 控制模式連動：Servo/IO 切換時同步到 IO 設定頁
                if (e.PropertyName == nameof(AtcBasicVM.ControlMode))
                    AtcIoVM.CurrentControlMode = AtcBasicVM.ControlMode;
            };

            // [2026-02-24] 訂閱機台類型變更事件：即時連動 AxisParameters + DRO/JOG/Offsets
            MachineConfigVM.MachineTypeChanged += OnMachineTypeChanged;

            // 3. 權限管理
            AuthService.Instance.CurrentUserChanged += OnUserChanged;
            OnUserChanged(AuthService.Instance.CurrentUser);

            // 4. 與 MainViewModel 連動
            try
            {
                var app = System.Windows.Application.Current;
                if (app?.MainWindow?.DataContext is MainViewModel mainVM)
                {
                    // [A] 訂閱事件 (處理未來的掃描)
                    mainVM.HardwareValidationCompleted += OnHardwareValidationCompleted;

                    // [B] ★★★ 讀取現有的掃描結果 ★★★
                    if (mainVM.LastValidatedSlaves != null && mainVM.LastValidatedSlaves.Count > 0)
                    {
                        OnHardwareValidationCompleted(mainVM.LastValidatedSlaves, mainVM.LastValidatedConfig);
                    }
                    else
                    {
                        // [2026-03-05] 後端未上線時，從本地存檔載入設定（讓畫面不空白）
                        _ = LoadFromLocalConfigAsync();
                    }
                }
                else
                {
                    // [2026-03-05] MainViewModel 尚未初始化，嘗試讀本地存檔
                    _ = LoadFromLocalConfigAsync();
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Failed to subscribe hardware validation event: {ex.Message}");
            }
        }

        // [2026-03-05] 後端未上線時，從本地 MachineConfig.json 載入已儲存的設定
        private async Task LoadFromLocalConfigAsync()
        {
            try
            {
                var config = await ConfigurationService.Instance.LoadConfigAsync();

                // 判斷是否有資料，沒有則不載入（畫面維持空白）
                if (config.Axes == null || config.Axes.Count == 0)
                    return;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // [2026-03-10] 快取設定，供 RebuildIoMapsFromConfig 內的還原邏輯使用
                    _lastConfig = config;

                    // 1. 同步機台類型
                    MachineConfigVM.SelectedMachineType = config.MachineType;

                    // 2. 填充 AXIS PARAMETERS
                    AxisVM.Axes.Clear();
                    for (int i = 0; i < config.Axes.Count; i++)
                    {
                        config.Axes[i].Index = i;
                        AxisVM.Axes.Add(config.Axes[i]);
                    }

                    // 3. 填充 AXIS MAPPING（從存檔的 Mappings 還原列，無 slaves 下拉選項）
                    MappingVM.LoadMapping(config, new List<DiscoveredSlave>());

                    // 4. 填充 IN MAP / OUT MAP（從存檔的 Mappings 重建，用 placeholder Slave）
                    RebuildIoMapsFromConfig(config);

                    // [2026-03-06] 載入 ATC 設定
                    AtcBasicVM.LoadFrom(config.Atc);
                    AtcIoVM.CurrentAtcType = config.Atc.Type;
                    AtcIoVM.CurrentControlMode = config.Atc.ControlMode; // [2026-03-09]

                    // [2026-03-06] 載入主軸設定
                    SpindleVM.LoadFrom(config.Spindle, new List<DiscoveredSlave>());

                    AlarmService.Instance.AddLog("INFO", "Loaded settings from local config (backend offline)");
                });
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("WARN", $"LoadFromLocalConfig failed: {ex.Message}");
            }
        }

        // [2026-03-05] 從本地存檔重建 IO Maps（後端未上線，用存檔中的 VID/PID/Index 建立 placeholder Slave）
        private void RebuildIoMapsFromConfig(MachineConfig config)
        {
            // [2026-03-10] 快取設定
            _lastConfig = config;

            InMaps.Clear();
            OutMaps.Clear();

            int inIdx = 0, outIdx = 0;

            // 按 PhysicalIndex 排序
            foreach (var mapping in config.Mappings.Where(m => m.Type == MapType.Input).OrderBy(m => m.PhysicalIndex))
            {
                var placeholderSlave = new DiscoveredSlave
                {
                    Index = mapping.PhysicalIndex,
                    Name = mapping.PhysicalAddress ?? $"Slave {mapping.PhysicalIndex}",
                    VendorId = mapping.ExpectedVendorId ?? "",
                    ProductCode = mapping.ExpectedProductCode ?? "",
                    Category = mapping.DeviceCategory ?? ""
                };

                var item = new IoMapItem
                {
                    Index = inIdx++,
                    LogicalName = mapping.LogicalName ?? $"Input_{mapping.PhysicalIndex}",
                    IsEnabled = true
                };
                item.SelectedSlave = placeholderSlave;

                // 還原 Pin 設定
                // [2026-03-10] 修正：空字串不覆蓋預設 "Pin N"，避免 Function Name 顯示空白
                if (mapping.Pins != null)
                {
                    foreach (var savedPin in mapping.Pins)
                    {
                        var existingPin = item.PinSettings.FirstOrDefault(p => p.PinIndex == savedPin.Index);
                        if (existingPin != null)
                        {
                            if (!string.IsNullOrEmpty(savedPin.Function))
                                existingPin.FunctionName = savedPin.Function;
                            existingPin.IsInverted = savedPin.IsInverted;
                        }
                    }
                }

                InMaps.Add(item);
            }

            foreach (var mapping in config.Mappings.Where(m => m.Type == MapType.Output).OrderBy(m => m.PhysicalIndex))
            {
                var placeholderSlave = new DiscoveredSlave
                {
                    Index = mapping.PhysicalIndex,
                    Name = mapping.PhysicalAddress ?? $"Slave {mapping.PhysicalIndex}",
                    VendorId = mapping.ExpectedVendorId ?? "",
                    ProductCode = mapping.ExpectedProductCode ?? "",
                    Category = mapping.DeviceCategory ?? ""
                };

                var item = new IoMapItem
                {
                    Index = outIdx++,
                    LogicalName = mapping.LogicalName ?? $"Output_{mapping.PhysicalIndex}",
                    IsEnabled = true
                };
                item.SelectedSlave = placeholderSlave;

                // [2026-03-10] 修正：空字串不覆蓋預設 "Pin N"
                if (mapping.Pins != null)
                {
                    foreach (var savedPin in mapping.Pins)
                    {
                        var existingPin = item.PinSettings.FirstOrDefault(p => p.PinIndex == savedPin.Index);
                        if (existingPin != null)
                        {
                            if (!string.IsNullOrEmpty(savedPin.Function))
                                existingPin.FunctionName = savedPin.Function;
                            existingPin.IsInverted = savedPin.IsInverted;
                        }
                    }
                }

                OutMaps.Add(item);
            }

            SelectedInMapIndex = InMaps.Count > 0 ? 0 : -1;
            SelectedOutMapIndex = OutMaps.Count > 0 ? 0 : -1;
        }

        // [2026-03-05] 動態生成 IO 列表（取代固定 4 列的 InitializeIoMaps）
        // [2026-03-10] 重建後自動從 _lastConfig 還原 pin 名稱，避免 CollectionChanged 觸發導致名稱遺失
        private void RebuildIoMapsFromScan(IEnumerable<DiscoveredSlave> slaves)
        {
            InMaps.Clear();
            OutMaps.Clear();

            // 按站號排序
            var sorted = slaves.OrderBy(s => s.Index).ToList();
            int inIdx = 0, outIdx = 0;

            foreach (var slave in sorted)
            {
                bool isInput = slave.Category == DeviceCategory.DigIn
                            || slave.Category == DeviceCategory.DiDo
                            || (slave.ProductCode != null && slave.ProductCode.Contains("902"));
                bool isOutput = slave.Category == DeviceCategory.DigOut
                             || slave.Category == DeviceCategory.DiDo
                             || (slave.ProductCode != null && slave.ProductCode.Contains("902"));

                if (isInput)
                {
                    var item = new IoMapItem
                    {
                        Index = inIdx++,
                        LogicalName = $"Input_{slave.Index}",
                        IsEnabled = true
                    };
                    item.SelectedSlave = slave; // 觸發 OnSelectedSlaveChanged → InitializePins
                    InMaps.Add(item);
                }

                if (isOutput)
                {
                    var item = new IoMapItem
                    {
                        Index = outIdx++,
                        LogicalName = $"Output_{slave.Index}",
                        IsEnabled = true
                    };
                    item.SelectedSlave = slave;
                    OutMaps.Add(item);
                }
            }

            // [2026-03-10] 自動還原 pin 名稱（從快取的 _lastConfig）
            RestorePinSettingsFromConfig();

            // 自動選取第一個
            SelectedInMapIndex = InMaps.Count > 0 ? 0 : -1;
            SelectedOutMapIndex = OutMaps.Count > 0 ? 0 : -1;
        }

        // [2026-03-10] 從快取設定還原 pin 名稱（供 RebuildIoMapsFromScan / RebuildIoMapsFromConfig 共用）
        private void RestorePinSettingsFromConfig()
        {
            if (_lastConfig?.Mappings == null) return;

            foreach (var mapping in _lastConfig.Mappings.Where(m => m.Type == MapType.Input))
            {
                var targetRow = InMaps.FirstOrDefault(x =>
                    x.SelectedSlave?.Index == mapping.PhysicalIndex);
                if (targetRow != null && mapping.Pins != null && mapping.Pins.Count > 0)
                {
                    foreach (var savedPin in mapping.Pins)
                    {
                        var existingPin = targetRow.PinSettings.FirstOrDefault(p => p.PinIndex == savedPin.Index);
                        if (existingPin != null)
                        {
                            if (!string.IsNullOrEmpty(savedPin.Function))
                                existingPin.FunctionName = savedPin.Function;
                            existingPin.IsInverted = savedPin.IsInverted;
                        }
                    }
                }
            }
            foreach (var mapping in _lastConfig.Mappings.Where(m => m.Type == MapType.Output))
            {
                var targetRow = OutMaps.FirstOrDefault(x =>
                    x.SelectedSlave?.Index == mapping.PhysicalIndex);
                if (targetRow != null && mapping.Pins != null && mapping.Pins.Count > 0)
                {
                    foreach (var savedPin in mapping.Pins)
                    {
                        var existingPin = targetRow.PinSettings.FirstOrDefault(p => p.PinIndex == savedPin.Index);
                        if (existingPin != null)
                        {
                            if (!string.IsNullOrEmpty(savedPin.Function))
                                existingPin.FunctionName = savedPin.Function;
                            existingPin.IsInverted = savedPin.IsInverted;
                        }
                    }
                }
            }
        }

        // [2026-02-24] 機台類型下拉選單變更 handler
        // 下拉僅更新軸勾選框（由 MachineConfigVM 自動處理）
        // 軸參數 / 軸映射 / DRO / JOG / Offsets / IO Monitor 全部延遲到
        // 使用者按下 UPDATE MAPPING TABLE 按鈕時才連動（見 ApplyMachineConfig）
        private void OnMachineTypeChanged(MachineType machineType, List<string> enabledAxes)
        {
            // 不做任何事 — 勾選框已由 MachineConfigVM.OnSelectedMachineTypeItemChanged 更新
        }

        // [2026-02-24] 依據啟用軸列表重建 AxisVM.Axes，保留已存在軸的參數值
        private void RebuildAxisParameters(List<string> enabledAxes)
        {
            // 快照現有軸參數（以 AxisID 為 Key）
            var existing = new Dictionary<string, AxisSetting>();
            foreach (var ax in AxisVM.Axes)
            {
                if (!string.IsNullOrEmpty(ax.AxisID))
                    existing[ax.AxisID] = ax;
            }

            AxisVM.Axes.Clear();
            int idx = 0;
            foreach (var axId in enabledAxes)
            {
                if (existing.TryGetValue(axId, out var prev))
                {
                    // 保留現有參數值，僅更新 Index
                    prev.Index = idx;
                    AxisVM.Axes.Add(prev);
                }
                else
                {
                    // 新增預設軸參數
                    AxisVM.Axes.Add(new AxisSetting
                    {
                        Index = idx,
                        AxisID = axId,
                        Name = $"{axId} Axis"
                    });
                }
                idx++;
            }
        }

        private void OnUserChanged(User user)
        {
            if (user != null)
            {
                CanEditHardware = (user.Role == UserRole.Admin || user.Role == UserRole.Developer);
            }
        }

        // [新增] 硬體驗證完成事件處理方法
        private void OnHardwareValidationCompleted(List<DiscoveredSlave> slaves, MachineConfig config)
        {
            try
            {
                // 1. 確保 UI 執行緒 (如果是從非 UI 執行緒呼叫)
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // 呼叫統一的初始化入口
                    Initialize(config, slaves);

                    // ★★★ [關鍵修改] 立即執行一次比對，更新 Settings 頁面的狀態文字 ★★★
                    VerifyHardware(config, slaves);
                });
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Error updating settings from validation: {ex.Message}");
            }
        }

        // [2026-03-10] 進入 Settings 頁面時重新從檔案載入設定，丟棄未存檔的修改
        public async Task ReloadFromFileAsync()
        {
            try
            {
                var config = await ConfigurationService.Instance.LoadConfigAsync();
                if (config == null || (config.Axes == null && config.Mappings.Count == 0))
                    return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 取得當前已掃描的 slaves（若有）
                    var slaves = HardwareVM.Slaves.ToList();

                    if (slaves.Count > 0)
                    {
                        // 有掃描結果：用完整 Initialize 流程（保留真實 slave 資訊）
                        Initialize(config, slaves);
                    }
                    else
                    {
                        // 無掃描結果（後端未上線）：從設定檔重建
                        _lastConfig = config;
                        MachineConfigVM.SelectedMachineType = config.MachineType;

                        AxisVM.Axes.Clear();
                        if (config.Axes != null)
                        {
                            for (int i = 0; i < config.Axes.Count; i++)
                            {
                                config.Axes[i].Index = i;
                                AxisVM.Axes.Add(config.Axes[i]);
                            }
                        }

                        MappingVM.LoadMapping(config, new List<DiscoveredSlave>());
                        RebuildIoMapsFromConfig(config);

                        AtcBasicVM.LoadFrom(config.Atc);
                        AtcIoVM.CurrentAtcType = config.Atc.Type;
                        AtcIoVM.CurrentControlMode = config.Atc.ControlMode;
                        SpindleVM.LoadFrom(config.Spindle, new List<DiscoveredSlave>());
                    }

                    AlarmService.Instance.AddLog("INFO", "Settings reloaded from config file");
                });
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("WARN", $"ReloadFromFile failed: {ex.Message}");
            }
        }

        public void Initialize(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            // [2026-03-10] 先快取設定，讓後續 RebuildIoMapsFromScan（含 CollectionChanged 觸發的）都能自動還原 pin 名稱
            _lastConfig = config;

            // Step 1: 填充 HARDWARE SCAN 表格
            HardwareVM.Slaves.Clear();
            foreach (var s in slaves) HardwareVM.Slaves.Add(s);

            // [2026-02-24] 同步機台類型至 MachineConfigVM
            MachineConfigVM.SelectedMachineType = config.MachineType;

            // Step 2: 初始化軸參數頁面
            AxisVM.Axes.Clear();
            if (config.Axes != null && config.Axes.Count > 0)
            {
                for (int i = 0; i < config.Axes.Count; i++)
                {
                    config.Axes[i].Index = i;
                    AxisVM.Axes.Add(config.Axes[i]);
                }
            }
            else
            {
                // 預設軸
                AxisVM.Axes.Add(new AxisSetting { Index = 0, AxisID = "X", Name = "X Axis" });
                AxisVM.Axes.Add(new AxisSetting { Index = 1, AxisID = "Y", Name = "Y Axis" });
                AxisVM.Axes.Add(new AxisSetting { Index = 2, AxisID = "Z", Name = "Z Axis" });
            }

            // Step 3: 載入 AXIS MAPPING
            MappingVM.LoadMapping(config, slaves);

            // [2026-02-24] Step 3.5: 同步 IO Monitor 卡片過濾（依軸映射）
            IoMonitorVM.UpdateAxisMapping(MappingVM.AxisMaps);

            // [2026-03-05] Step 4: 動態生成 IO 列表（自動填充 SelectedSlave + InitializePins）
            // [2026-03-10] pin 名稱還原已內建於 RebuildIoMapsFromScan（透過 _lastConfig）
            RebuildIoMapsFromScan(slaves);

            // [2026-03-06] Step 4.5: 載入 ATC 設定
            AtcBasicVM.LoadFrom(config.Atc);
            AtcAxisVM.UpdateSlaves(slaves);
            AtcAxisVM.LoadFrom(config.Atc, slaves);
            AtcIoVM.UpdateSlaves(slaves);
            AtcIoVM.LoadFrom(config.Atc, slaves);
            AtcIoVM.CurrentAtcType = config.Atc.Type;
            AtcIoVM.CurrentControlMode = config.Atc.ControlMode; // [2026-03-09]

            // [2026-03-06] Step 4.6: 載入主軸設定
            SpindleVM.LoadFrom(config.Spindle, slaves);

            // Step 5: 初始化驗證 (如果已經有 Config)
            if (config.Mappings.Count > 0)
            {
                VerifyHardware(config, slaves);
            }
        }

        // [新增] 驗證邏輯封裝
        private void VerifyHardware(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            var result = HardwareScanService.Instance.ValidateTopology(slaves, config);

            ScanResultText = result.Message;
            if (result.IsValid)
            {
                ScanResultColor = Brushes.LimeGreen;
            }
            else
            {
                ScanResultColor = Brushes.Red;
                AlarmService.Instance.AddLog("WARN", result.Message);
            }
        }

        // [2026-02-24] UPDATE MAPPING TABLE 按鈕：一次性套用所有機台類型連動
        [RelayCommand]
        private void ApplyMachineConfig()
        {
            // 1. 取得目前啟用軸列表
            var machineType = MachineConfigVM.SelectedMachineType;
            var enabledAxes = new MachineConfig { MachineType = machineType }.GetEnabledAxes();

            // 2. 重建 AXIS PARAMETERS（保留現有參數值）
            RebuildAxisParameters(enabledAxes);

            // 3. 重建 AXIS MAPPING
            MappingVM.UpdateSlaves(HardwareVM.Slaves);
            MappingVM.GenerateAxisTable(MachineConfigVM);

            // 4. 同步 IO Monitor 軸名標註
            IoMonitorVM.UpdateAxisMapping(MappingVM.AxisMaps);

            // 5. 通知 MainViewModel 更新 DRO / JOG / Offsets
            try
            {
                var app = System.Windows.Application.Current;
                if (app?.MainWindow?.DataContext is MainViewModel mainVM)
                {
                    mainVM.ApplyMachineType(machineType, enabledAxes);
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"ApplyMachineConfig error: {ex.Message}");
            }
        }

        // [2026-03-05] 移除 RebuildIoSlaveDropdowns()（改為 RebuildIoMapsFromScan 動態生成列表）

        [RelayCommand]
        private async Task GenerateAndDeploy()
        {
            try
            {
                DeployStatus = "Saving Config...";

                var config = new MachineConfig();
                // [2026-02-24] 儲存機台類型
                config.MachineType = MachineConfigVM.SelectedMachineType;
                config.Axes.AddRange(AxisVM.Axes);

                // [修正] 清單重建：先清除再新增（避免重複）
                config.Mappings.Clear();

                // [Item 14] 部署前對 UI 集合取快照，防止迭代期間使用者同時修改 UI 導致 InvalidOperationException
                var axisMapsSnapshot = MappingVM.AxisMaps.ToList();
                var inMapsSnapshot   = InMaps.ToList();
                var outMapsSnapshot  = OutMaps.ToList();

                // 1. 儲存軸映射
                foreach (var mapItem in axisMapsSnapshot)
                {
                    if (mapItem.SelectedSlave != null)
                    {
                        config.Mappings.Add(new HardwareMapping
                        {
                            LogicalName = mapItem.AxisName,
                            PhysicalAddress = mapItem.SelectedSlave.Name,
                            PhysicalIndex = mapItem.SelectedSlave.Index,
                            // [關鍵] 儲存時，將目前的 VID/PID 寫入 Config，作為未來的驗證標準
                            ExpectedVendorId = mapItem.SelectedSlave.VendorId,
                            ExpectedProductCode = mapItem.SelectedSlave.ProductCode,
                            Type = MapType.Axis,
                            // [2026-03-05] 儲存掃描結果的設備類別
                            DeviceCategory = mapItem.SelectedSlave.Category ?? ""
                        });
                    }
                }

                // ★★★ [新增] 儲存 IO 映射 ★★★
                // 這裡我們需要定義 HardwareMapping 結構是否支援 IO，或者使用新的清單
                // 假設 HardwareMapping 通用，我們可以用 MappingType 區分

                // [2026-03-05] 儲存 IN MAP（僅儲存已啟用的模組）
                foreach (var inItem in inMapsSnapshot.Where(m => m.IsEnabled))
                {
                    // 檢查是否有選擇設備
                    if (inItem.SelectedSlave != null)
                    {
                        // 步驟 1: 先建立物件並指派給變數 'mapping'
                        var mapping = new HardwareMapping
                        {
                            LogicalName = inItem.LogicalName,
                            PhysicalAddress = inItem.SelectedSlave.Name,
                            PhysicalIndex = inItem.SelectedSlave.Index,
                            ExpectedVendorId = inItem.SelectedSlave.VendorId,
                            ExpectedProductCode = inItem.SelectedSlave.ProductCode,
                            Type = MapType.Input,
                            ChannelIndex = inItem.Index,
                            // [2026-03-05] 儲存掃描結果的設備類別
                            DeviceCategory = inItem.SelectedSlave.Category ?? ""
                        };

                        // 步驟 2: 現在 'mapping' 變數存在了，可以把 Pin 設定加進去
                        foreach (var pin in inItem.PinSettings)
                        {
                            mapping.Pins.Add(new PinConfig
                            {
                                Index = pin.PinIndex,
                                // 防止 FunctionName 為 null (視需求可加)
                                Function = pin.FunctionName ?? $"Pin {pin.PinIndex}",
                                IsInverted = pin.IsInverted
                            });
                        }

                        // 步驟 3: 設定完成後，將 mapping 物件加入 Config 清單
                        config.Mappings.Add(mapping);
                    }
                }

                // [2026-03-05] 儲存 OUT MAP（僅儲存已啟用的模組）
                foreach (var outItem in outMapsSnapshot.Where(m => m.IsEnabled))
                {
                    // 檢查是否選擇了有效設備
                    if (outItem.SelectedSlave != null)
                    {
                        // 1. 先建立物件並指派給變數 'mapping'
                        var mapping = new HardwareMapping
                        {
                            LogicalName = outItem.LogicalName,
                            PhysicalAddress = outItem.SelectedSlave.Name,
                            PhysicalIndex = outItem.SelectedSlave.Index,
                            ExpectedVendorId = outItem.SelectedSlave.VendorId,
                            ExpectedProductCode = outItem.SelectedSlave.ProductCode,
                            Type = MapType.Output, // 設定為輸出類型
                            ChannelIndex = outItem.Index,
                            // [2026-03-05] 儲存掃描結果的設備類別
                            DeviceCategory = outItem.SelectedSlave.Category ?? ""
                        };

                        // 2. 複製 Pin 設定 (輸出點也可以設定反轉，例如 Active Low)
                        foreach (var pin in outItem.PinSettings)
                        {
                            mapping.Pins.Add(new PinConfig
                            {
                                Index = pin.PinIndex,
                                // 防止 null
                                Function = pin.FunctionName ?? $"Out {pin.PinIndex}",
                                IsInverted = pin.IsInverted // 對於 Output，這代表是否反向 (Active Low)
                            });
                        }

                        // 3. 最後將設定加入 Config
                        config.Mappings.Add(mapping);
                    }
                }

                // [2026-03-06] 儲存 ATC 設定
                AtcBasicVM.SaveTo(config.Atc);
                AtcAxisVM.SaveTo(config.Atc);
                AtcIoVM.SaveTo(config.Atc);
                    SpindleVM.SaveTo(config.Spindle);

                // 1. 存檔並觸發重啟
                // [2026-03-10] 更新快取，確保後續 RebuildIoMapsFromScan 使用最新設定
                _lastConfig = config;
                await ConfigurationService.Instance.SaveConfigAsync(config);

                DeployStatus = "Restarting LinuxCNC...";

                // 2. 開始輪詢確認啟動狀態
                bool isStarted = await WaitForLinuxCNC(20);

                if (isStarted)
                {
                    DeployStatus = "Online (Ready)";
                    // [新增] 部署成功後，重新驗證一次狀態
                    VerifyHardware(config, HardwareVM.Slaves.ToList());
                }
                else
                {
                    DeployStatus = "Startup FAILED";
                    string log = await MachineControlService.Instance.GetStartupLogAsync();
                    LastErrorLog = log;
                    Console.WriteLine("STARTUP ERROR LOG:\n" + log);
                }
            }
            catch (Exception ex)
            {
                DeployStatus = $"Error: {ex.Message}";
            }
        }

        private async Task<bool> WaitForLinuxCNC(int timeoutSeconds)
        {
            for (int i = 0; i < timeoutSeconds; i++)
            {
                await Task.Delay(1000);
                DeployStatus = $"Starting... ({i}/{timeoutSeconds}s)";

                bool connected = await MachineControlService.Instance.CheckConnectionAsync();
                if (connected)
                {
                    return true;
                }
            }
            return false;
        }

        public void UpdateMachineStatus(MachineStatusData data)
        {
            if (data != null)
            {
                IoMonitorVM.UpdateData(data);

                // [2026-03-10] 更新 IN MAP / OUT MAP pin 即時狀態（綠燈/灰燈）
                // IO_Status 格式：{ "13": { "di": { "0": true, ... }, "do": { "5": false, ... } } }
                if (data.IO_Status != null)
                {
                    foreach (var map in InMaps)
                    {
                        if (map.SelectedSlave == null) continue;
                        var slaveKey = map.SelectedSlave.Index.ToString();
                        if (data.IO_Status.TryGetValue(slaveKey, out var slaveIo) &&
                            slaveIo.TryGetValue("di", out var diMap))
                        {
                            foreach (var pin in map.PinSettings)
                                pin.IsActive = diMap.TryGetValue(pin.PinIndex.ToString(), out bool v) && v;
                        }
                    }
                    foreach (var map in OutMaps)
                    {
                        if (map.SelectedSlave == null) continue;
                        var slaveKey = map.SelectedSlave.Index.ToString();
                        if (data.IO_Status.TryGetValue(slaveKey, out var slaveIo) &&
                            slaveIo.TryGetValue("do", out var doMap))
                        {
                            foreach (var pin in map.PinSettings)
                                pin.IsActive = doMap.TryGetValue(pin.PinIndex.ToString(), out bool v) && v;
                        }
                    }
                }
            }
        }
    }
}