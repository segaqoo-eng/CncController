using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media; // [重要] 必須引用，為了使用 Color 和 Brush
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // ========================================================================
        // [新增] 硬體驗證完成事件委派
        // 用於通知 SettingsViewModel 硬體掃描和驗證已完成，並傳遞掃描結果
        // ========================================================================
        public delegate void HardwareValidationCompletedEventHandler(
            List<DiscoveredSlave> slaves,
            MachineConfig config);

        public event HardwareValidationCompletedEventHandler HardwareValidationCompleted;

        // [新增] 保存最后一次验证的结果，供 SettingsViewModel 初始化时读取
        public List<DiscoveredSlave> LastValidatedSlaves { get; private set; }
        public MachineConfig LastValidatedConfig { get; private set; }

        // ==============================================================================
        // 1. 屬性定義
        // ==============================================================================

        [ObservableProperty] private object _currentViewModel;
        [ObservableProperty] private MachineStatus _status = new();

        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isPower;
        [ObservableProperty] private bool _isEstop;

        // --- 系統狀態與燈號 ---

        [ObservableProperty] private string _systemStatus = "Initializing System...";
        [ObservableProperty] private bool _isSystemReady = false;

        // 重新命名為 SystemStatusColor 以符合 SystemStatus
        // 這裡存 Color 是為了給 DropShadowEffect 用
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SystemStatusBrush))] // 通知 Brush 更新
        private Color _systemStatusColor = Colors.Gray;

        // 這裡轉成 Brush 是為了給 Background 用
        public SolidColorBrush SystemStatusBrush => new SolidColorBrush(SystemStatusColor);

        // [新增] Server 版本顯示
        [ObservableProperty] private string _serverVersionDisplay = "---";

        [ObservableProperty]
        private User _currentUser = new User { Username = "Operator", Role = UserRole.Operator };

        // === JOG 設定 ===
        [ObservableProperty] private double _jogFeedrate = 1500.0;
        [ObservableProperty] private double _jogStepDistance = 0;
        private readonly HashSet<int> _activeJogAxes = new();

        private readonly DispatcherTimer _timer;

        // [新增] 跑馬燈與連線狀態控制變數
        private DateTime _lastMarqueeTime = DateTime.MinValue; // 控制跑馬燈切換時間
        private int _marqueeIndex = 0;                         // 目前顯示第幾筆警報
        private MachineControlService.ConnectionState _connectionState = MachineControlService.ConnectionState.Disconnected; // 暫存連線狀態

        // ==============================================================================
        // 2. 建構子
        // ==============================================================================

        public MainViewModel()
        {
            CurrentViewModel = new MonitorViewModel();

            // [新增] 1. 初始化時，先從 AuthService 抓目前的狀態
            CurrentUser = AuthService.Instance.CurrentUser;

            // [新增] 2. 訂閱事件：當 AuthService 登入/登出時，自動更新這裡的變數
            AuthService.Instance.CurrentUserChanged += (user) =>
            {
                CurrentUser = user;
            };

            // 系統啟動 Log
            AlarmService.Instance.AddLog("LOGIN", "System Started");

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += StatusTimer_Tick; // 改用具名方法
            _timer.Start();

            // [新增] 啟動後非同步執行硬體自動驗證（不阻塞 UI）
            _ = AutoValidateHardware();
        }

        // ==============================================================================
        // 3. 開機自動硬體驗證 (新增方法)
        // ==============================================================================
        private async Task AutoValidateHardware()
        {
            try
            {
                // Step 1: 讀取軟體設定 (MachineConfig)
                var config = await ConfigurationService.Instance.LoadConfigAsync();

                // 若無設定檔，仍需初始化一個空的以防報錯
                if (config == null) config = new MachineConfig();

                // Step 2: 掃描硬體 (Hardware Scan)
                System.Diagnostics.Debug.WriteLine("Step 2: 掃描硬體");
                var slaves = await HardwareScanService.Instance.ScanAsync();

                // [新增] 保存一份在本地 (供 Debug 或其他用途)
                LastValidatedSlaves = slaves;
                LastValidatedConfig = config;

                // ★★★ [關鍵修改] 無論成功失敗，都先廣播數據！ ★★★
                // 這樣 SettingsViewModel 才能收到 slaves 並顯示在列表上
                HardwareValidationCompleted?.Invoke(slaves, config);

                // Step 3: 驗證拓樸 (比較 PID/VID/Index)
                System.Diagnostics.Debug.WriteLine("Step 3: 驗證拓樸");
                var result = HardwareScanService.Instance.ValidateTopology(slaves, config);

                // Step 4: 更新 MainViewModel 自己的 UI 燈號
                if (!result.IsValid)
                {
                    // ✗ 驗證失敗 (只亮紅燈，不跳轉)
                    SystemStatus = $"硬體驗證失敗: {result.Message}";
                    SystemStatusColor = Colors.Red;
                    IsSystemReady = false;

                    // Log
                    AlarmService.Instance.AddLog("WARN", $"Hardware verification failed: {result.Message}");
                }
                else
                {
                    // ✓ 驗證成功
                    SystemStatus = "硬體驗證通過 - 系統就緒";
                    SystemStatusColor = Colors.LimeGreen;
                    IsSystemReady = true;
                    AlarmService.Instance.AddLog("SYS", "Hardware verification passed successfully");
                }
            }
            catch (Exception ex)
            {
                SystemStatus = $"硬體驗證異常: {ex.Message}";
                SystemStatusColor = Colors.Red;
                IsSystemReady = false;
                AlarmService.Instance.AddLog("ERR", $"AutoValidateHardware exception: {ex.Message}");
            }
        }

        // ==============================================================================
        // 4. 狀態輪詢 (邏輯修正版)
        // ==============================================================================
        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop(); // ★ 暫停：防止網路卡住時，Timer 一直觸發導致堆積
            try
            {
                // 1. 抓取數據 (保留原有邏輯)
                await PollMachineStatus();
                // 2. ★★★ 關鍵：必須在這裡主動抓錯誤 ★★★
                // 因為 Service 不再自動抓了，如果您這裡沒寫，就永遠抓不到錯誤
                if (IsConnected)
                {
                    await PollErrors();
                }

                // 2. 更新跑馬燈與頂部狀態 (新邏輯)
                UpdateHeaderStatus();
            }
            catch { /* 忽略錯誤，避免 Timer 死掉 */ }
            finally
            {
                _timer.Start(); // ★ 重啟：確保做完才數下一次
            }
        }
        private async Task PollErrors()
        {
            // 1. 從 Service 取得新訊息
            var errors = await MachineControlService.Instance.GetErrorsAsync();

            if (errors != null && errors.Count > 0)
            {
                foreach (var err in errors)
                {
                    // =========================================================
                    // 1. 雜訊過濾 (Filter Noise) - 讓介面變乾淨
                    // =========================================================

                    // 過濾空字串
                    if (string.IsNullOrWhiteSpace(err.Text)) continue;

                    // 過濾 NML 連線雜訊 [-1]
                    if (err.Kind == "-1") continue;

                    // 過濾 HAL 重複載入警告
                    // 當 HAL 檔寫了多次 loadrt 時會出現，這對操作員無意義，建議隱藏
                    if (err.Text.Contains("already exists")) continue;

                    // 過濾 NML 未知錯誤提示
                    if (err.Text.Contains("unrecognized error")) continue;

                    // =========================================================
                    // 2. 錯誤分類 (Classify) - 決定紅燈還是綠燈
                    // =========================================================
                    LogType type;

                    switch (err.Kind)
                    {
                        case "11": // [11] EMC_OPERATOR_ERROR (最嚴重)
                                   // 包含：急停、極限觸發、追隨誤差
                                   // 動作：跑馬燈變紅、紀錄為 Error
                            type = LogType.Error;
                            break;

                        case "1":  // [1] EMC_OPERATOR_TEXT (普通文字)
                        case "2":  // [2] EMC_OPERATOR_DISPLAY (顯示請求)
                                   // 包含：G-Code 裡的 (MSG,...) 提示
                                   // 動作：紀錄為 Info，不閃紅燈
                            type = LogType.Info;
                            break;

                        default:
                            // 其他未知的系統代碼，預設視為 Info，避免漏看
                            // 但如果您發現還有其他雜訊，可以在這裡加 log 觀察
                            // System.Diagnostics.Debug.WriteLine($"Unknown Kind: {err.Kind}");
                            type = LogType.Info;
                            break;
                    }

                    // =========================================================
                    // 3. 執行動作 (Action)
                    // =========================================================
                    // 加入 Log 後，會自動觸發 UpdateHeaderStatus 更新跑馬燈
                    AlarmService.Instance.AddLog(type, err.Text);
                }
            }
        }
        private async Task PollMachineStatus()
        {
            // 呼叫 Service
            var (state, data) = await MachineControlService.Instance.GetStatusAsync();

            // [關鍵] 記住連線狀態，供 UpdateHeaderStatus 使用
            _connectionState = state;

            // 更新版本顯示
            ServerVersionDisplay = MachineControlService.Instance.ServerVersion;

            switch (state)
            {
                case MachineControlService.ConnectionState.Connected:
                    // === 1. 通訊正常 (HTTP 200) ===
                    IsConnected = true;

                    if (data != null)
                    {
                        // 更新狀態旗標
                        IsPower = (data.Task_State == "ON");
                        IsEstop = (data.Task_State == "ESTOP");
                        IsSystemReady = !IsEstop && IsPower;

                        // 更新座標與數據
                        UpdateMachineData(data);
                    }
                    break;

                case MachineControlService.ConnectionState.ServerOnly:
                    // === 2. 僅後端連線 ===
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;
                    break;

                case MachineControlService.ConnectionState.Disconnected:
                default:
                    // === 3. 完全斷線 ===
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;
                    break;
            }
        }

        private void UpdateMachineData(MachineStatusData data)
        {
            if (data.Position != null)
            {
                if (data.Position.TryGetValue("X", out double x)) Status.X = x;
                if (data.Position.TryGetValue("Y", out double y)) Status.Y = y;
                if (data.Position.TryGetValue("Z", out double z)) Status.Z = z;
                if (data.Position.TryGetValue("A", out double a)) Status.A = a;
                if (data.Position.TryGetValue("B", out double b)) Status.B = b;
                if (data.Position.TryGetValue("C", out double c)) Status.C = c;
            }

            Status.Feedrate = data.Feedrate;
            Status.SpindleSpeed = data.Spindle_Speed;
            Status.File = string.IsNullOrEmpty(data.File) ? "No File Loaded" : data.File;
        }

        // [修改] 跑馬燈與狀態顯示邏輯 (修正連線判斷與時間控制)
        private void UpdateHeaderStatus()
        {
            var alarms = AlarmService.Instance.ActiveAlarms;

            // =========================================================
            // 1. 優先級最高：警報輪播 (Marquee)
            // =========================================================
            if (alarms.Count > 0)
            {
                // [修正] 改用時間差控制，每 1.0 秒切換一次，不依賴 Timer 的頻率
                if ((DateTime.Now - _lastMarqueeTime).TotalSeconds >= 1.0)
                {
                    _lastMarqueeTime = DateTime.Now;
                    _marqueeIndex++;
                }

                // 索引循環保護
                if (_marqueeIndex >= alarms.Count) _marqueeIndex = 0;

                // 安全檢查：防止清單在非 UI 執行緒被清空
                if (_marqueeIndex < alarms.Count)
                {
                    var currentLog = alarms[_marqueeIndex];
                    SystemStatus = $"{_marqueeIndex + 1}/{alarms.Count} {currentLog.DisplayMessage}";
                    SystemStatusColor = currentLog.Type == LogType.Error ? Colors.Red : Colors.Orange;
                }
                return;
            }

            // =========================================================
            // 2. 優先級次高：連線狀態檢查 (解決斷線時顯示 Power Off 的問題)
            // =========================================================
            if (_connectionState == MachineControlService.ConnectionState.Disconnected)
            {
                SystemStatus = "Disconnected from Server";
                SystemStatusColor = Colors.Red;
                return;
            }

            if (_connectionState == MachineControlService.ConnectionState.ServerOnly)
            {
                SystemStatus = "Backend Connected (LinuxCNC Offline)";
                SystemStatusColor = Colors.Orange;
                return;
            }

            // =========================================================
            // 3. 正常狀態：顯示機台邏輯 (Power / Estop / Ready)
            // =========================================================
            if (IsEstop)
            {
                SystemStatus = "EMERGENCY STOP ACTIVE";
                SystemStatusColor = Colors.Red;
            }
            else if (!IsPower)
            {
                SystemStatus = "Machine Power Off";
                SystemStatusColor = Colors.Orange;
            }
            else
            {
                SystemStatus = "System Ready";
                SystemStatusColor = Colors.LimeGreen;
            }
        }

        // ==============================================================================
        // 5. 指令
        // ==============================================================================

        [RelayCommand]
        private void Navigate(string viewName)
        {
            switch (viewName)
            {
                case "Main": CurrentViewModel = new MonitorViewModel(); break;
                case "Settings": CurrentViewModel = new SettingsViewModel(); break;
                case "History": CurrentViewModel = new HistoryViewModel(); break;
            }
        }
        // [修改] 電源切換邏輯
        [RelayCommand]
        private async Task TogglePower()
        {
            // [新增] 安全檢查：如果急停未解除，禁止操作電源
            if (IsEstop)
            {
                // 可以選擇顯示一個 Log 提示使用者
                AlarmService.Instance.AddLog(LogType.Warning, "Cannot toggle Power while E-STOP is active");
                return;
            }

            await MachineControlService.Instance.ResetMachineAsync();
        }

        // [修改] 急停切換邏輯
        [RelayCommand]
        private async Task ToggleEstop()
        {
            if (IsEstop)
            {
                // 情況 1: 目前是急停 (未解除, 灰色) -> 按下解除
                await MachineControlService.Instance.ResetMachineAsync();
            }
            else
            {
                // 情況 2: 目前正常 (已解除, 紅色) -> 按下觸發急停
                await MachineControlService.Instance.TriggerEstopAsync();
            }
        }
        [RelayCommand]
        private async Task JogStart(string args)
        {
            if (string.IsNullOrEmpty(args)) return;
            var parts = args.Split(',');

            if (parts.Length == 2 && int.TryParse(parts[0], out int axis) && double.TryParse(parts[1], out double dirSign))
            {
                // [關鍵新增 1] 標記這個軸正在動作 (MouseDown 確實發生)
                _activeJogAxes.Add(axis);

                double finalSpeed = Math.Abs(JogFeedrate) * (dirSign > 0 ? 1 : -1);
                double distance = JogStepDistance > 0 ? JogStepDistance : 0; // 0 代表連續模式

                await MachineControlService.Instance.JogAsync(axis, finalSpeed, distance);
            }
        }

        [RelayCommand]
        private async Task JogStop(string axisStr)
        {
            if (int.TryParse(axisStr, out int axis))
            {
                // [關鍵新增 2] 防呆檢查
                // 檢查這個軸是否真的處於 JOG 狀態？
                if (!_activeJogAxes.Contains(axis))
                {
                    return; // 直接離開，不發送網路指令
                }

                // [關鍵新增 3] 確實有按住，現在要放開了 -> 移除標記
                _activeJogAxes.Remove(axis);

                // [原有邏輯] 只有在連續模式 (JogStepDistance == 0) 才發送 Stop
                if (JogStepDistance == 0)
                {
                    await MachineControlService.Instance.JogStopAsync(axis);
                }
            }
        }
        [RelayCommand]
        private void SetJogMode(string value)
        {
            if (double.TryParse(value, out double dist))
            {
                JogStepDistance = dist;
            }
        }

        [RelayCommand] private async Task CycleStart() => await MachineControlService.Instance.CycleStartAsync();
        [RelayCommand] private async Task FeedHold() => await MachineControlService.Instance.FeedHoldAsync();
        [RelayCommand] private async Task Stop() => await MachineControlService.Instance.StopAsync();

        // [新增] 清除警報指令 (綁定給 ESC)
        [RelayCommand]
        private void ClearAlarms()
        {
            AlarmService.Instance.ClearActiveAlarms();
            // 強制立即刷新一次 Header，讓它變回綠燈，不用等 Timer
            UpdateHeaderStatus();
        }

    }
}