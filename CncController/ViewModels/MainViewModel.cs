using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media;
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    // [2026-03-12] 拆分為 partial class：Core / Navigation / Polling / Jog / Safety / CycleControl / Dro / Stats
    public partial class MainViewModel : ObservableObject
    {
        // ========================================================================
        // 硬體驗證完成事件委派
        // ========================================================================
        public delegate void HardwareValidationCompletedEventHandler(
            List<DiscoveredSlave> slaves,
            MachineConfig config);

        public event HardwareValidationCompletedEventHandler HardwareValidationCompleted;

        public List<DiscoveredSlave> LastValidatedSlaves { get; private set; }
        public MachineConfig LastValidatedConfig { get; private set; }

        // ==============================================================================
        // [ViewModel 實體管理] 全部改為長駐，確保切換頁面時狀態不流失
        // ==============================================================================
        public SettingsViewModel SettingsVM { get; } = new SettingsViewModel();
        public MonitorViewModel MonitorVM { get; } = new MonitorViewModel();
        public HistoryViewModel HistoryVM { get; } = new HistoryViewModel();
        public OffsetsViewModel OffsetsVM { get; } = new OffsetsViewModel();
        public ToolTableViewModel ToolTableVM { get; } = new ToolTableViewModel();
        public AtcViewModel AtcVM { get; } = new AtcViewModel();
        public ProbingViewModel ProbingVM { get; } = new ProbingViewModel();
        public FileManagerViewModel FileManagerVM { get; } = new FileManagerViewModel();

        // [2026-03-04] UI 全域縮放
        public double CanvasWidth => 1920.0 / AppSettings.Instance.UiScale;
        public double CanvasHeight => 1080.0 / AppSettings.Instance.UiScale;

        // ==============================================================================
        // 核心狀態屬性
        // ==============================================================================
        [ObservableProperty] private object _currentViewModel;
        [ObservableProperty] private MachineStatus _status = new();

        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isPower;
        [ObservableProperty] private bool _isEstop;

        // --- 系統狀態與燈號 ---
        [ObservableProperty] private string _systemStatus = "Initializing System...";
        [ObservableProperty] private bool _isSystemReady = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SystemStatusBrush))]
        private Color _systemStatusColor = Colors.Gray;

        public SolidColorBrush SystemStatusBrush => new SolidColorBrush(SystemStatusColor);

        [ObservableProperty] private string _serverVersionDisplay = "---";
        [ObservableProperty] private string _cycleTimeDisplay = "00:00:00";

        [ObservableProperty]
        private User _currentUser = new User { Username = "Operator", Role = UserRole.Operator };

        // [2026-03-04] 啟動連線等待
        [ObservableProperty] private bool _isStartingUp = true;
        [ObservableProperty] private bool _showRetryButton;
        private DateTime _appStartTime;
        private const int StartupGraceSeconds = 15;

        // [2026-02-24] 動態軸數支援
        [ObservableProperty] private bool _isAxisAEnabled;
        [ObservableProperty] private bool _isAxisBEnabled;
        [ObservableProperty] private bool _isAxisCEnabled;

        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _cycleTimer;
        private DateTime _cycleStartTime;

        // ==============================================================================
        // 建構子
        // ==============================================================================
        public MainViewModel()
        {
            // [2026-03-04] 記錄啟動時間，供寬限期計算
            _appStartTime = DateTime.Now;

            // [2026-03-04] 啟動寬限期結束後自動關閉 IsStartingUp
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(StartupGraceSeconds));
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (IsStartingUp) IsStartingUp = false;
                });
            });

            // [2026-03-12] 啟動時還原上次的語言 & 主題設定
            var savedLang = AppSettings.Instance.Language;
            if (savedLang != "zh-TW")
            {
                LocalizationService.Instance.SwitchLanguage(savedLang);
            }
            IsLangZhTW = savedLang == "zh-TW";
            IsLangEnUS = savedLang == "en-US";

            var savedTheme = AppSettings.Instance.Theme;
            if (savedTheme != "Default")
            {
                ThemeService.Instance.SwitchTheme(savedTheme);
            }
            IsThemeDefault = savedTheme == "Default";
            IsThemeIndustrial = savedTheme == "Industrial";
            IsThemeCyber = savedTheme == "Cyber";

            CurrentViewModel = MonitorVM;

            // [2026-03-04] 將 Status 傳遞給各 VM
            MonitorVM.MachineStatus = Status;
            OffsetsVM.MachineStatus = Status;
            ToolTableVM.MachineStatus = Status;
            AtcVM.MachineStatus = Status;
            AtcVM.MonitorVM = MonitorVM;
            ProbingVM.MachineStatus = Status;

            CurrentUser = AuthService.Instance.CurrentUser;
            AuthService.Instance.CurrentUserChanged += (user) =>
            {
                CurrentUser = user;
            };

            AlarmService.Instance.AddLog("LOGIN", "System Started");

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += StatusTimer_Tick;
            _timer.Start();

            _cycleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cycleTimer.Tick += (s, e) =>
            {
                try
                {
                    if (Status.InterpState == "RUNNING")
                    {
                        var span = DateTime.Now - _cycleStartTime;
                        CycleTimeDisplay = span.ToString(@"hh\:mm\:ss");
                    }
                }
                catch (Exception ex)
                {
                    AlarmService.Instance.AddLog("ERR", $"CycleTimer error: {ex.Message}");
                }
            };

            this.HardwareValidationCompleted += (slaves, config) =>
            {
                try
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        SettingsVM.Initialize(config, slaves);
                    });
                }
                catch (Exception ex)
                {
                    AlarmService.Instance.AddLog("ERR", $"HardwareValidationCompleted handler error: {ex.Message}");
                }
            };

            _ = AutoValidateHardware();

            // [2026-03-12] 開機延遲 5 秒後檢查斷電續切狀態
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
                System.Windows.Application.Current.Dispatcher.Invoke(async () => await CheckResumeState()));
        }

        // ==============================================================================
        // 開機自動硬體驗證
        // ==============================================================================
        private async Task AutoValidateHardware()
        {
            try
            {
                var config = await ConfigurationService.Instance.LoadConfigAsync();
                if (config == null) config = new MachineConfig();

                System.Diagnostics.Debug.WriteLine("Step 2: 掃描硬體");
                var slaves = await HardwareScanService.Instance.ScanAsync();

                LastValidatedSlaves = slaves;
                LastValidatedConfig = config;

                // [2026-02-24] 傳遞啟用軸列表 + 設定軸可見性
                var enabledAxes = config.GetEnabledAxes();
                OffsetsVM.EnabledAxes = enabledAxes;
                IsAxisAEnabled = enabledAxes.Contains("A");
                IsAxisBEnabled = enabledAxes.Contains("B");
                IsAxisCEnabled = enabledAxes.Contains("C");
                OffsetsVM.IsAxisAEnabled = IsAxisAEnabled;
                OffsetsVM.IsAxisBEnabled = IsAxisBEnabled;
                OffsetsVM.IsAxisCEnabled = IsAxisCEnabled;

                // [2026-03-03] 同步 ToolTableVM 軸可見性
                ToolTableVM.IsAxisAEnabled = IsAxisAEnabled;
                ToolTableVM.IsAxisBEnabled = IsAxisBEnabled;
                ToolTableVM.IsAxisCEnabled = IsAxisCEnabled;

                HardwareValidationCompleted?.Invoke(slaves, config);

                System.Diagnostics.Debug.WriteLine("Step 3: 驗證拓樸");
                var result = HardwareScanService.Instance.ValidateTopology(slaves, config);

                if (!result.IsValid)
                {
                    SystemStatus = $"硬體驗證失敗: {result.Message}";
                    SystemStatusColor = Colors.Red;
                    IsSystemReady = false;
                    AlarmService.Instance.AddLog("WARN", $"Hardware verification failed: {result.Message}");
                }
                else
                {
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
            finally
            {
                // [2026-03-04] 硬體掃描完成後才結束啟動階段
                IsStartingUp = false;
            }
        }
    }
}
