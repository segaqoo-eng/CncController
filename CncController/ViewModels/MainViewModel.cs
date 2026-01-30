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

        // [修正] 重新命名為 SystemStatusColor 以符合 SystemStatus
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
                // Step 1: 讀取本地設定
                var config = await ConfigurationService.Instance.LoadConfigAsync();

                // 判斷：無設定檔或無 mapping
                if (config == null || config.Mappings == null || config.Mappings.Count == 0)
                {
                    // ✗ 無設定檔
                    System.Diagnostics.Debug.WriteLine("✗ 無設定檔");
                    SystemStatus = "未設定硬體配置 - 請進入 [設定] 頁面進行掃描與綁定";
                    SystemStatusColor = Colors.Orange; // 黃燈
                    IsSystemReady = false;
                    AlarmService.Instance.AddLog("WARN", "No hardware config found. Please configure in Settings.");
                    return; // 不繼續驗證
                }

                // Step 2: 掃描硬體
                System.Diagnostics.Debug.WriteLine("Step 2: 掃描硬體");
                var slaves = await HardwareScanService.Instance.ScanAsync();

                // Step 3: 驗證拓樸
                System.Diagnostics.Debug.WriteLine("Step 3: 驗證拓樸");
                var result = HardwareScanService.Instance.ValidateTopology(slaves, config);

                // Step 4: 根據結果更新 UI
                if (!result.IsValid)
                {
                    // ✗ 驗證失敗：硬體不匹配
                    SystemStatus = $"硬體驗證失敗: {result.Message}";
                    SystemStatusColor = Colors.Red; // 紅燈
                    IsSystemReady = false;
                    AlarmService.Instance.AddLog("WARN", $"Hardware verification failed: {result.Message}");
                }
                else
                {
                    // ✓ 驗證成功
                    SystemStatus = "硬體驗證通過 - 系統就緒";
                    SystemStatusColor = Colors.LimeGreen; // 綠燈
                    IsSystemReady = true;
                    AlarmService.Instance.AddLog("SYS", "Hardware verification passed successfully");

                    // [新增] 觸發硬體驗證完成事件，通知 SettingsViewModel 載入掃描結果
                    HardwareValidationCompleted?.Invoke(slaves, config);
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
        // [修改] Timer 處理邏輯 (解決拔線沒反應的問題)
        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop(); // ★ 暫停：防止網路卡住時，Timer 一直觸發導致堆積
            try
            {
                await PollMachineStatus();
            }
            catch { /* 忽略錯誤，避免 Timer 死掉 */ }
            finally
            {
                _timer.Start(); // ★ 重啟：確保做完才數下一次
            }
        }
        private async Task PollMachineStatus()
        {
            // 呼叫 Service (確保 Service 層已修改為回傳 tuple)
            var (connectionState, data) = await MachineControlService.Instance.GetStatusAsync();

            // 更新版本顯示
            ServerVersionDisplay = MachineControlService.Instance.ServerVersion;

            switch (connectionState)
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

                        // [關鍵邏輯] 決定燈號顏色
                        if (IsEstop)
                        {
                            SystemStatus = "EMERGENCY STOP ACTIVE";
                            SystemStatusColor = Colors.Orange; // 🟠 急停
                        }
                        else if (!IsPower)
                        {
                            SystemStatus = "Machine Power Off";
                            SystemStatusColor = Colors.Orange; // 🟠 斷電
                        }
                        else
                        {
                            SystemStatus = "System Ready";
                            SystemStatusColor = Colors.LimeGreen; // 🟢 就緒
                        }

                        // 更新座標與數據
                        UpdateMachineData(data);
                    }
                    else
                    {
                        // 異常：有連線但無資料
                        SystemStatus = "Data Error";
                        SystemStatusColor = Colors.Orange;
                    }
                    break;

                case MachineControlService.ConnectionState.ServerOnly:
                    // === 2. 僅後端連線 (HTTP 500 / LinuxCNC Offline) ===
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;

                    SystemStatusColor = Colors.Orange; // 🟠 警告
                    SystemStatus = "Backend Connected (LinuxCNC Offline)";
                    break;

                case MachineControlService.ConnectionState.Disconnected:
                default:
                    // === 3. 完全斷線 ===
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;

                    SystemStatusColor = Colors.Red; // 🔴 斷線
                    SystemStatus = "Disconnected from Server";
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

        [RelayCommand] private async Task TogglePower() => await MachineControlService.Instance.ResetMachineAsync();
        [RelayCommand] private async Task ToggleEstop() => await MachineControlService.Instance.TriggerEstopAsync();

        [RelayCommand]
        private async Task JogStart(string args)
        {
            // Debug 檢查 (選擇性)
            // AlarmService.Instance.AddLog("DEBUG", $"Jog Trig: {args}"); 

            if (string.IsNullOrEmpty(args)) return;
            var parts = args.Split(',');

            if (parts.Length == 2 && int.TryParse(parts[0], out int axis) && double.TryParse(parts[1], out double dirSign))
            {
                // [關鍵新增 1] 標記這個軸正在動作 (MouseDown 確實發生)
                _activeJogAxes.Add(axis);

                double finalSpeed = Math.Abs(JogFeedrate) * (dirSign > 0 ? 1 : -1);
                double distance = JogStepDistance > 0 ? JogStepDistance : 0; // 0 代表連續模式

                // AlarmService.Instance.AddLog("JOG", $"Axis:{axis} Spd:{finalSpeed} Dist:{distance}");

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
                // 如果集合裡沒有這個軸，代表使用者只是滑鼠滑過去 (觸發 MouseLeave 但沒按 MouseDown)，直接忽略！
                if (!_activeJogAxes.Contains(axis))
                {
                    return; // 直接離開，不發送網路指令
                }

                // [關鍵新增 3] 確實有按住，現在要放開了 -> 移除標記
                _activeJogAxes.Remove(axis);

                // [原有邏輯] 只有在連續模式 (JogStepDistance == 0) 才發送 Stop
                // 單步模式下，讓機器自己跑完距離停下來，不要發送 Stop 打斷它
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
    }
}