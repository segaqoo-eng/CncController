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

        // =========================================================
        // ★★★ [新增] IO 映射集合 (綁定到 DataGrid) ★★★
        // =========================================================
        public ObservableCollection<IoMapItem> InMaps { get; } = new();
        public ObservableCollection<IoMapItem> OutMaps { get; } = new();

        // ★★★ [新增] 過濾後的下拉選單選項 ★★★
        public ObservableCollection<DiscoveredSlave> AvailableInputSlaves { get; } = new();
        public ObservableCollection<DiscoveredSlave> AvailableOutputSlaves { get; } = new();

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

        // ★★★ [新增] 提供給 UI 綁定的訊號清單 ★★★
        public List<string> CommonOutputSignals => StandardSignals.OutputSignals;

        // (選用) 輸入訊號清單
        public List<string> CommonInputSignals => StandardSignals.InputSignals;

        public SettingsViewModel()
        {
            // 1. 初始化 IO 映射預設值
            InitializeIoMaps();

            // 2. 內部連動：當 HardwareVM 的 Slaves 變動時（手動 Scan Bus），更新所有下拉選單
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                // [2026-02-24] AXIS MAPPING 下拉
                MappingVM.UpdateSlaves(HardwareVM.Slaves);

                // [2026-02-24] IN MAP / OUT MAP 下拉（重建 IO 設備篩選清單）
                RebuildIoSlaveDropdowns(HardwareVM.Slaves);
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
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Failed to subscribe hardware validation event: {ex.Message}");
            }
        }

        // [新增] 初始化 IO 映射表格 (預設各 4 組)
        private void InitializeIoMaps()
        {
            InMaps.Clear();
            OutMaps.Clear();

            // 這裡未來可以改為讀取設定檔的變數
            int defaultInCount = 4;
            int defaultOutCount = 4;

            for (int i = 0; i < defaultInCount; i++)
            {
                InMaps.Add(new IoMapItem
                {
                    Index = i,
                    LogicalName = $"Input_Group_{i}"
                });
            }

            for (int i = 0; i < defaultOutCount; i++)
            {
                OutMaps.Add(new IoMapItem
                {
                    Index = i,
                    LogicalName = $"Output_Group_{i}"
                });
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

        public void Initialize(MachineConfig config, List<DiscoveredSlave> slaves)
        {
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

            // Step 4: 過濾設備到 IO 下拉選單
            RebuildIoSlaveDropdowns(slaves);

            // Step 5: 初始化驗證 (如果已經有 Config)
            if (config.Mappings.Count > 0)
            {
                VerifyHardware(config, slaves);
            }
            // Step 6: 載入已儲存的 IO 映射 (從 Config 還原到 UI)
            foreach (var mapping in config.Mappings)
            {
                IoMapItem targetRow = null;
                if (mapping.Type == MapType.Input)
                    targetRow = InMaps.FirstOrDefault(x => x.Index == mapping.ChannelIndex);
                else if (mapping.Type == MapType.Output)
                    targetRow = OutMaps.FirstOrDefault(x => x.Index == mapping.ChannelIndex);

                if (targetRow != null)
                {
                    // 1. 先設定 Slave，這會觸發 OnSelectedSlaveChanged 並執行 InitializePins
                    targetRow.SelectedSlave = slaves.FirstOrDefault(s =>
                        s.VendorId == mapping.ExpectedVendorId &&
                        s.ProductCode == mapping.ExpectedProductCode &&
                        s.Index == mapping.PhysicalIndex);

                    // 2. ★ 關鍵：現在 PinSettings 已經產生了，把存檔裡的詳細設定填回去 ★
                    if (mapping.Pins != null && mapping.Pins.Count > 0)
                    {
                        // 這裡不直接 Clear，而是更新現有的 Pin 物件屬性
                        foreach (var savedPin in mapping.Pins)
                        {
                            var existingPin = targetRow.PinSettings.FirstOrDefault(p => p.PinIndex == savedPin.Index);
                            if (existingPin != null)
                            {
                                existingPin.FunctionName = savedPin.Function;
                                existingPin.IsInverted = savedPin.IsInverted;
                            }
                        }
                    }
                }
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

        // [2026-02-24] 重建 IN MAP / OUT MAP 的 IO Slave 下拉選單
        // 共用於 Initialize（開機載入）及手動 Scan Bus
        private void RebuildIoSlaveDropdowns(IEnumerable<DiscoveredSlave> slaves)
        {
            AvailableInputSlaves.Clear();
            AvailableOutputSlaves.Clear();

            var noneSlave = new DiscoveredSlave
            {
                Name = "--- None ---",
                VendorId = "",
                ProductCode = "",
                Category = ""
            };

            AvailableInputSlaves.Add(noneSlave);
            AvailableOutputSlaves.Add(noneSlave);

            foreach (var slave in slaves)
            {
                System.Diagnostics.Debug.WriteLine($"[Scan] Slave #{slave.Index} ({slave.Name}): Category='{slave.Category}', ProductCode='{slave.ProductCode}'");

                // 輸入裝置：DigIn / DiDo / 台達 902
                if (slave.Category == DeviceCategory.DigIn ||
                    slave.Category == DeviceCategory.DiDo ||
                    (slave.ProductCode != null && slave.ProductCode.Contains("902")))
                {
                    AvailableInputSlaves.Add(slave);
                }

                // 輸出裝置：DigOut / DiDo / 台達 902
                if (slave.Category == DeviceCategory.DigOut ||
                    slave.Category == DeviceCategory.DiDo ||
                    (slave.ProductCode != null && slave.ProductCode.Contains("902")))
                {
                    AvailableOutputSlaves.Add(slave);
                }
            }
        }

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
                            Type = MapType.Axis
                        });
                    }
                }

                // ★★★ [新增] 儲存 IO 映射 ★★★
                // 這裡我們需要定義 HardwareMapping 結構是否支援 IO，或者使用新的清單
                // 假設 HardwareMapping 通用，我們可以用 MappingType 區分

                // 儲存 IN MAP
                foreach (var inItem in inMapsSnapshot)
                {
                    // 檢查是否有選擇設備
                    if (inItem.SelectedSlave != null && inItem.SelectedSlave.Name != "--- None ---")
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
                            ChannelIndex = inItem.Index
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

                // 儲存 OUT MAP
                foreach (var outItem in outMapsSnapshot)
                {
                    // 檢查是否選擇了有效設備
                    if (outItem.SelectedSlave != null && outItem.SelectedSlave.Name != "--- None ---")
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
                            ChannelIndex = outItem.Index
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

                // 1. 存檔並觸發重啟
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
            }
        }
    }
}