using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
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
        // [ViewModel 實體管理] 全部改為長駐，確保切換頁面時狀態不流失
        // ==============================================================================

        // 1. 設定頁面 (保持原樣)
        public SettingsViewModel SettingsVM { get; } = new SettingsViewModel();

        // 2. ★★★ [補回] 監控頁面 (MonitorVM) - 必須長駐以保留 G-Code ★★★
        public MonitorViewModel MonitorVM { get; } = new MonitorViewModel();

        // 3. ★★★ [補回] 歷史頁面 (HistoryVM) - 必須長駐以保留篩選器狀態 ★★★
        public HistoryViewModel HistoryVM { get; } = new HistoryViewModel();

        // [2026-02-23] 新增 OffsetsVM：Offsets 頁面長駐 ViewModel（保留狀態）
        public OffsetsViewModel OffsetsVM { get; } = new OffsetsViewModel();

        // [2026-03-03] 新增 ToolTableVM：TOOL 分頁長駐 ViewModel
        public ToolTableViewModel ToolTableVM { get; } = new ToolTableViewModel();

        // [2026-03-03] 新增 AtcVM：ATC 自動刀庫分頁長駐 ViewModel
        public AtcViewModel AtcVM { get; } = new AtcViewModel();

        // [2026-03-04] 新增 ProbingVM：探測循環分頁長駐 ViewModel
        public ProbingViewModel ProbingVM { get; } = new ProbingViewModel();

        // [2026-03-04] UI 全域縮放：設計畫布尺寸 = 1920/1080 ÷ UiScale
        public double CanvasWidth => 1920.0 / AppSettings.Instance.UiScale;
        public double CanvasHeight => 1080.0 / AppSettings.Instance.UiScale;

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

        // [新增] 加工時間顯示 (給 CycleControl 用)
        [ObservableProperty] private string _cycleTimeDisplay = "00:00:00";

        [ObservableProperty]
        private User _currentUser = new User { Username = "Operator", Role = UserRole.Operator };

        // === JOG 設定 ===
        [ObservableProperty] private double _jogFeedrate = 1500.0;
        [ObservableProperty] private double _jogStepDistance = 0;
        // [2026-03-05] JOG 速度百分比（0~100%），對齊 PB 版 D_5
        [ObservableProperty] private double _jogSpeedPercent = 100.0;
        // [2026-03-05] JOG 主軸轉速 RPM，對齊 PB 版 D_5
        [ObservableProperty] private double _jogSpindleRpm = 300.0;

        // [2026-03-05] Override 靜態值（V/R 暫無後端連動），對齊 PB 版 D_4
        [ObservableProperty] private double _velocityOverride = 100.0;
        [ObservableProperty] private double _rapidOverride = 100.0;
        private readonly HashSet<int> _activeJogAxes = new();

        private readonly DispatcherTimer _timer;

        // [新增] 加工計時器
        private readonly DispatcherTimer _cycleTimer;
        private DateTime _cycleStartTime;

        // [新增] 跑馬燈與連線狀態控制變數
        private DateTime _lastMarqueeTime = DateTime.MinValue; // 控制跑馬燈切換時間
        private int _marqueeIndex = 0;                         // 目前顯示第幾筆警報
        private MachineControlService.ConnectionState _connectionState = MachineControlService.ConnectionState.Disconnected; // 暫存連線狀態

        // [新增] 暫存目前載入的檔名 (如果有透過 MonitorVM 載入的話)
        // 實務上這應該透過 Messenger 或 Service 共享，這裡先做簡單欄位
        private string _loadedFileName = null;


        [ObservableProperty]
        private bool _isFloodOn; // 用於 UI 顯示按鈕是否被按下 (變色)

        // [2026-02-23] 新增 IsMistOn：MIST 噴霧冷卻狀態（M7=開 / M9=關）
        [ObservableProperty]
        private bool _isMistOn;

        // [2026-02-24] 新增 IsSingleBlock：Single Block 模式（每次 Cycle Start 僅執行一行 G-Code）
        [ObservableProperty]
        private bool _isSingleBlock;

        // [2026-03-04] 新增 Block Delete / Optional Stop 開關
        [ObservableProperty] private bool _isBlockDelete;
        [ObservableProperty] private bool _isOptionalStop;

        // [2026-03-04] 啟動連線等待：寬限期內不報紅燈
        [ObservableProperty] private bool _isStartingUp = true;
        [ObservableProperty] private bool _showRetryButton;
        private DateTime _appStartTime;
        private const int StartupGraceSeconds = 15;

        // [2026-02-24] 動態軸數支援：旋轉軸是否啟用（由 MachineConfig 決定，DRO/JOG/Offsets 依此顯示/隱藏）
        [ObservableProperty] private bool _isAxisAEnabled;
        [ObservableProperty] private bool _isAxisBEnabled;
        [ObservableProperty] private bool _isAxisCEnabled;

        [RelayCommand]
        private async Task ToggleFlood()
        {
            string cmd = IsFloodOn ? "M9" : "M8";
            bool success = await MachineControlService.Instance.SendMdiCommandAsync(cmd);
            if (success)
                IsFloodOn = !IsFloodOn;
        }

        // [2026-02-23] 新增 ToggleMistCommand：M7=噴霧開，M9=全部冷卻關；關閉時同步清除 IsFloodOn
        [RelayCommand]
        private async Task ToggleMist()
        {
            string cmd = IsMistOn ? "M9" : "M7";
            bool success = await MachineControlService.Instance.SendMdiCommandAsync(cmd);
            if (success)
            {
                IsMistOn = !IsMistOn;
                // M9 = ALL coolant off，同步清除 Flood 狀態
                if (!IsMistOn) IsFloodOn = false;
            }
        }

        // [2026-02-23] 新增 HomeAllCommand：呼叫 HomeAsync(-1) 全軸回原點；條件：IsPower=true + IsEstop=false
        // [2026-02-24] 加入樂觀更新：全軸 Homed 先設 false（按鈕變紅），等後端輪詢回報再變綠
        [RelayCommand]
        private async Task HomeAll()
        {
            if (IsEstop)
            {
                AlarmService.Instance.AddLog("WARN", "Home Blocked: E-Stop active");
                return;
            }
            if (!IsPower)
            {
                AlarmService.Instance.AddLog("WARN", "Home Blocked: Machine power off");
                return;
            }
            // [2026-02-24] 樂觀更新：立即設為未復歸
            Status.IsXHomed = false; Status.IsYHomed = false; Status.IsZHomed = false;
            Status.IsAHomed = false; Status.IsBHomed = false; Status.IsCHomed = false;
            Status.IsAllHomed = false;
            AlarmService.Instance.AddLog("INFO", "Homing All Axes...");
            bool ok = await MachineControlService.Instance.HomeAsync(-1);
            if (!ok)
                AlarmService.Instance.AddLog("WARN", "Home command failed");
        }

        // [2026-02-24] 修正 GoToZeroCommand：移至當前 G5X 工件座標零點（G0 X0 Y0 Z0）
        [RelayCommand]
        private async Task GoToZero()
        {
            if (!CanExecuteMotion()) return;
            AlarmService.Instance.AddLog("INFO", $"Go To Zero (Work): G0 X0 Y0 Z0 [{Status.ActiveCoordSystem}]");
            await MachineControlService.Instance.SendMdiCommandAsync("G0 X0 Y0 Z0");
        }

        // [2026-02-24] 新增 GoToHomeCommand：移至機械零點（G53 G0 X0 Y0 Z0）
        [RelayCommand]
        private async Task GoToHome()
        {
            if (!CanExecuteMotion()) return;
            AlarmService.Instance.AddLog("INFO", "Go To Home (Machine): G53 G0 X0 Y0 Z0");
            await MachineControlService.Instance.SendMdiCommandAsync("G53 G0 X0 Y0 Z0");
        }

        // [2026-02-24] 新增 GoToG30Command：移至 G30 第二參考點（ToolInfo 按鈕用）
        [RelayCommand]
        private async Task GoToG30()
        {
            if (!CanExecuteMotion()) return;
            AlarmService.Instance.AddLog("INFO", "Go To G30 Reference Point");
            await MachineControlService.Instance.SendMdiCommandAsync("G30");
        }

        // [2026-02-23] 新增 ExitAppCommand：HeaderBar File 選單 EXIT 項目，記錄 Log 後呼叫 Shutdown
        [RelayCommand]
        private void ExitApp()
        {
            AlarmService.Instance.AddLog("INFO", "User requested application exit.");
            System.Windows.Application.Current.Shutdown();
        }

        // ==============================================================================
        // 2. 建構子
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

            // ★★★ [關鍵修改] 初始化時使用長駐的 MonitorVM ★★★
            CurrentViewModel = MonitorVM;

            // [2026-03-04] 將 Status 傳遞給 MonitorVM，讓行號高亮可追蹤 CurrentLine
            MonitorVM.MachineStatus = Status;

            // [2026-02-24] 將 Status 傳遞給 OffsetsVM，讓 Offsets 右欄可綁定即時座標
            OffsetsVM.MachineStatus = Status;

            // [2026-03-03] 將 Status 傳遞給 ToolTableVM，讓右欄可綁定即時刀具資訊
            ToolTableVM.MachineStatus = Status;

            // [2026-03-03] 將 Status 傳遞給 AtcVM，讓 ATC 頁可綁定即時刀具號
            AtcVM.MachineStatus = Status;
            // [2026-03-04] 將 MonitorVM 傳遞給 AtcVM，讓 PROGRAM TOOLS 可讀取 G-Code
            AtcVM.MonitorVM = MonitorVM;

            // [2026-03-04] 將 Status 傳遞給 ProbingVM，讓探測頁可綁定即時座標
            ProbingVM.MachineStatus = Status;

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

            // 初始化加工計時器
            _cycleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cycleTimer.Tick += (s, e) =>
            {
                // [安全] async void 事件必須有 try/catch，否則例外會直接 unhandled crash
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
                // [安全] 事件處理器加入 try/catch 防止例外向上傳播
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

                // [2026-02-24] 將 MachineConfig 的啟用軸列表傳遞給各 VM + 設定軸可見性
                var enabledAxes = config.GetEnabledAxes();
                OffsetsVM.EnabledAxes = enabledAxes;
                IsAxisAEnabled = enabledAxes.Contains("A");
                IsAxisBEnabled = enabledAxes.Contains("B");
                IsAxisCEnabled = enabledAxes.Contains("C");
                // [2026-02-24] 同步 OffsetsVM 軸可見性（開機載入 + 即時切換共用邏輯）
                OffsetsVM.IsAxisAEnabled = IsAxisAEnabled;
                OffsetsVM.IsAxisBEnabled = IsAxisBEnabled;
                OffsetsVM.IsAxisCEnabled = IsAxisCEnabled;

                // [2026-03-03] 同步 ToolTableVM 軸可見性
                ToolTableVM.IsAxisAEnabled = IsAxisAEnabled;
                ToolTableVM.IsAxisBEnabled = IsAxisBEnabled;
                ToolTableVM.IsAxisCEnabled = IsAxisCEnabled;

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
            finally
            {
                // [2026-03-04] 硬體掃描完成後才結束啟動階段（不再由連線成功提前關閉）
                IsStartingUp = false;
            }
        }

        // ==============================================================================
        // 4. 狀態輪詢 (邏輯修正版)
        // ==============================================================================
        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();
            try
            {
                // 1. 呼叫剛剛改好的 PollMachineStatus，並用變數 data 接住回傳值
                var data = await PollMachineStatus();

                // 2. ★★★ 關鍵：把資料傳給 SettingsVM 的 IO 監控 ★★★
                if (data != null)
                {
                    SettingsVM.UpdateMachineStatus(data);

                    // [2026-02-11] 未來若 MonitorVM 需要即時座標更新，也可在此呼叫
                    // MonitorVM.UpdateStatus(data); 
                }

                // [2026-02-11 新增] 連動加工計時器
                if (data != null && data.Interp_State == "RUNNING" && !_cycleTimer.IsEnabled)
                {
                    _cycleStartTime = DateTime.Now; // 這裡簡化，實際應記錄開始時間
                    _cycleTimer.Start();
                }
                else if (data != null && data.Interp_State == "IDLE" && _cycleTimer.IsEnabled)
                {
                    _cycleTimer.Stop();
                }

                // 3. 抓錯誤 (原有邏輯)
                if (IsConnected)
                {
                    await PollErrors();
                }

                // 4. 更新頂部狀態
                UpdateHeaderStatus();
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Poll error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _timer.Start();
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
        // 1. 修改回傳型別：加上 <MachineStatusData?>
        private async Task<MachineStatusData?> PollMachineStatus()
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
                        // [安全] 先計算所有新狀態快照，再依序套用
                        // 確保 IsSystemReady 永遠由「同批」的 IsEstop/IsPower 決定
                        bool newIsPower = data.Task_State == "ON";
                        bool newIsEstop = data.Task_State == "ESTOP";
                        bool newIsReady = !newIsEstop && newIsPower;

                        IsPower       = newIsPower;
                        IsEstop       = newIsEstop;
                        IsSystemReady = newIsReady;

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

            // 2. ★★★ 新增這行：把資料回傳出去 ★★★
            return data;
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

            if (data.DTG != null)
            {
                if (data.DTG.TryGetValue("X", out double dx)) Status.DtgX = dx;
                if (data.DTG.TryGetValue("Y", out double dy)) Status.DtgY = dy;
                if (data.DTG.TryGetValue("Z", out double dz)) Status.DtgZ = dz;
                // [2026-02-24] 補齊 A/B/C 軸 DTG（多軸機台支援）
                if (data.DTG.TryGetValue("A", out double da)) Status.DtgA = da;
                if (data.DTG.TryGetValue("B", out double db)) Status.DtgB = db;
                if (data.DTG.TryGetValue("C", out double dc)) Status.DtgC = dc;
            }

            // [2026-02-24] 新增：將後端工件座標同步至 MachineStatus（含 A/B/C）
            if (data.Work_Position != null)
            {
                if (data.Work_Position.TryGetValue("X", out double wx)) Status.WorkX = wx;
                if (data.Work_Position.TryGetValue("Y", out double wy)) Status.WorkY = wy;
                if (data.Work_Position.TryGetValue("Z", out double wz)) Status.WorkZ = wz;
                if (data.Work_Position.TryGetValue("A", out double wa)) Status.WorkA = wa;
                if (data.Work_Position.TryGetValue("B", out double wb)) Status.WorkB = wb;
                if (data.Work_Position.TryGetValue("C", out double wc)) Status.WorkC = wc;
            }

            Status.Feedrate = data.Feedrate;
            Status.SpindleSpeed = data.Spindle_Speed;
            Status.File = string.IsNullOrEmpty(data.File) ? "No File Loaded" : data.File;

            // [2026-02-24] 新增：同步 Feed/Spindle Override 百分比
            Status.FeedOverride = data.Feed_Override;
            Status.SpindleOverride = data.Spindle_Override;

            // [2026-02-24] 新增：同步刀具資訊（刀具號、刀長、刀徑）
            Status.ToolNumber = data.Tool_Number;
            Status.ToolLength = data.Tool_Length;
            Status.ToolDiameter = data.Tool_Diameter;

            // [2026-02-24] 映射各軸 Homed 狀態
            if (data.Homed != null)
            {
                Status.IsXHomed = data.Homed.TryGetValue("X", out bool hx) && hx;
                Status.IsYHomed = data.Homed.TryGetValue("Y", out bool hy) && hy;
                Status.IsZHomed = data.Homed.TryGetValue("Z", out bool hz) && hz;
                Status.IsAHomed = data.Homed.TryGetValue("A", out bool ha) && ha;
                Status.IsBHomed = data.Homed.TryGetValue("B", out bool hb) && hb;
                Status.IsCHomed = data.Homed.TryGetValue("C", out bool hc) && hc;
                var enabled = LastValidatedConfig?.GetEnabledAxes() ?? new() { "X", "Y", "Z" };
                Status.IsAllHomed = enabled.All(a => data.Homed.TryGetValue(a, out bool v) && v);
            }

            // [2026-02-24] 新增：G92 偏移量映射（供 Offsets 右欄 G52/G92 OFFSET 欄位）
            if (data.G92_Offset != null)
            {
                if (data.G92_Offset.TryGetValue("X", out double g92x)) Status.G92X = g92x;
                if (data.G92_Offset.TryGetValue("Y", out double g92y)) Status.G92Y = g92y;
                if (data.G92_Offset.TryGetValue("Z", out double g92z)) Status.G92Z = g92z;
                if (data.G92_Offset.TryGetValue("A", out double g92a)) Status.G92A = g92a;
                if (data.G92_Offset.TryGetValue("B", out double g92b)) Status.G92B = g92b;
                if (data.G92_Offset.TryGetValue("C", out double g92c)) Status.G92C = g92c;
            }

            // [2026-02-24] 新增：完整刀具偏移映射（供 Offsets 右欄 TOOL OFFSET 欄位）
            if (data.Tool_Offset_XYZ != null)
            {
                if (data.Tool_Offset_XYZ.TryGetValue("X", out double tox)) Status.ToolOffsetX = tox;
                if (data.Tool_Offset_XYZ.TryGetValue("Y", out double toy)) Status.ToolOffsetY = toy;
                if (data.Tool_Offset_XYZ.TryGetValue("Z", out double toz)) Status.ToolOffsetZ = toz;
            }

            // [2026-02-24] 新增：任務模式映射（供 Offsets 右下角 MAN/AUTO/MDI 按鈕高亮）
            if (!string.IsNullOrEmpty(data.Task_Mode))
                Status.TaskMode = data.Task_Mode;

            // [2026-03-06] 同步 Probe Input 訊號狀態（供 ProbingView 指示燈）
            Status.IsProbeInput = data.Probe_Input;

            // [2026-03-04] 同步 Block Delete / Optional Stop / Current Line
            IsBlockDelete = data.Block_Delete;
            Status.IsBlockDelete = data.Block_Delete;
            IsOptionalStop = data.Optional_Stop;
            Status.IsOptionalStop = data.Optional_Stop;
            Status.CurrentLine = data.Current_Line;

            // 更新 InterpState 供計時器判斷
            Status.InterpState = data.Interp_State;

            // [2026-02-23] 新增：將後端回傳的 Active_WCS 同步至 MachineStatus 與 OffsetsVM，保持 DRO 快選列高亮一致
            if (!string.IsNullOrEmpty(data.Active_WCS))
            {
                Status.ActiveCoordSystem = data.Active_WCS;
                OffsetsVM.ActiveOffset = data.Active_WCS;
            }
        }

        // [修改] 跑馬燈與狀態顯示邏輯 (修正連線判斷與時間控制)
        // [2026-03-04] 加入啟動寬限期 + RETRY 按鈕可見性邏輯
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
                ShowRetryButton = false;
                return;
            }

            // =========================================================
            // 2. [2026-03-04] 啟動寬限期：整個啟動階段顯示黃色等待
            //    IsStartingUp 由 AutoValidateHardware 完成或 15s 計時器關閉
            // =========================================================
            if (IsStartingUp)
            {
                if (_connectionState == MachineControlService.ConnectionState.Connected)
                    SystemStatus = "Scanning Hardware...";
                else
                    SystemStatus = "Connecting to Server...";
                SystemStatusColor = Colors.Yellow;
                ShowRetryButton = false;
                return;
            }

            // =========================================================
            // 3. 連線狀態檢查 (解決斷線時顯示 Power Off 的問題)
            // =========================================================
            if (_connectionState == MachineControlService.ConnectionState.Disconnected)
            {
                SystemStatus = "Disconnected from Server";
                SystemStatusColor = Colors.Red;
                ShowRetryButton = true;
                return;
            }

            if (_connectionState == MachineControlService.ConnectionState.ServerOnly)
            {
                SystemStatus = "Backend OK — LinuxCNC Offline";
                SystemStatusColor = Colors.Orange;
                ShowRetryButton = true;
                return;
            }

            // =========================================================
            // 4. 連線成功後正常狀態：隱藏 RETRY
            // =========================================================
            ShowRetryButton = false;

            // =========================================================
            // 5. 正常狀態：顯示機台邏輯 (Power / Estop / Ready)
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
                // ★★★ [關鍵修改] 使用長駐實體，避免切換頁面後資料遺失 ★★★
                case "Main": CurrentViewModel = MonitorVM; break;

                // ★★★ [關鍵修改] 使用長駐實體，避免切換頁面後資料遺失 ★★★
                case "Settings": CurrentViewModel = SettingsVM; break;

                // ★★★ [關鍵修改] 使用長駐實體，避免切換頁面後篩選狀態遺失 ★★★
                case "History": CurrentViewModel = HistoryVM; break;

                case "Offsets": CurrentViewModel = OffsetsVM; break; // [2026-02-23] 新增 Offsets Tab 導航
                case "Tool": CurrentViewModel = ToolTableVM; break; // [2026-03-03] 新增 TOOL Tab 導航
                // [2026-03-04] ATC Tab 導航：自動載入程式刀具列表
                case "Atc":
                    CurrentViewModel = AtcVM;
                    AtcVM.LoadProgramToolsCommand.Execute(null);
                    break;
                case "Probing": CurrentViewModel = ProbingVM; break; // [2026-03-04] 新增 PROBING Tab 導航
            }
        }
        // [安全] 統一運動指令前置檢查：IsEstop 與 IsPower 雙重驗證
        private bool CanExecuteMotion()
        {
            if (IsEstop)
            {
                AlarmService.Instance.AddLog(LogType.Warning, "Motion blocked: E-Stop active");
                return false;
            }
            if (!IsPower)
            {
                AlarmService.Instance.AddLog(LogType.Warning, "Motion blocked: Machine power off");
                return false;
            }
            return true;
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

        // [2026-03-04] RETRY 連線：重置失敗計數 + 重新進入寬限期 5 秒
        [RelayCommand]
        private async Task RetryConnection()
        {
            MachineControlService.Instance.ResetFailCount();
            IsStartingUp = true;
            ShowRetryButton = false;
            SystemStatus = "Reconnecting...";
            SystemStatusColor = Colors.Yellow;
            AlarmService.Instance.AddLog("INFO", "User triggered RETRY connection");
            await Task.Delay(5000);
            IsStartingUp = false;
        }

        // [2026-03-04] RE-SCAN 硬體：重新執行硬體掃描與驗證
        [RelayCommand]
        private async Task RescanHardware()
        {
            SystemStatus = "Re-scanning Hardware...";
            SystemStatusColor = Colors.Yellow;
            IsSystemReady = false;
            AlarmService.Instance.AddLog("INFO", "User triggered RE-SCAN hardware");
            await AutoValidateHardware();
        }

        // [修改] 急停切換邏輯
        [RelayCommand]
        private async Task ToggleEstop()
        {
            if (IsEstop)
            {
                // 情況 1: 目前是急停 (未解除) -> 解除急停
                await MachineControlService.Instance.ResetMachineAsync();
            }
            else
            {
                // 情況 2: 觸發急停
                // [安全] 樂觀更新：立即設定 IsEstop=true，不等後端回應
                // 讓 UI 立即反映急停狀態，實際狀態由下次輪詢確認
                IsEstop       = true;
                IsSystemReady = false;
                await MachineControlService.Instance.TriggerEstopAsync();
            }
        }
        [RelayCommand]
        private async Task JogStart(string args)
        {
            if (!CanExecuteMotion()) return;
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

        // [Cycle Control] 核心控制指令
        // [2026-02-24] 修改：IsSingleBlock 模式下改用 StepProgramAsync 單節執行
        [RelayCommand]
        private async Task CycleStart()
        {
            if (!CanExecuteMotion()) return;

            if (IsSingleBlock)
            {
                // Single Block 模式：每次僅執行一行
                await MachineControlService.Instance.StepProgramAsync();
            }
            else
            {
                // 正常模式
                _loadedFileName = MonitorVM.CurrentFileName;
                await MachineControlService.Instance.CycleStartAsync(_loadedFileName);
            }

            // 重置計時器 (如果需要從頭開始算)
            if (Status.InterpState == "IDLE")
            {
                _cycleStartTime = DateTime.Now;
                CycleTimeDisplay = "00:00:00";
            }
        }

        [RelayCommand] private async Task FeedHold() => await MachineControlService.Instance.FeedHoldAsync();
        [RelayCommand] private async Task Stop() => await MachineControlService.Instance.StopAsync();

        // [2026-03-03] 新增 SetModeCommand：切換任務模式（MAN/AUTO/MDI），供 JogPanel 底部按鈕使用
        [RelayCommand]
        private async Task SetMode(string mode)
        {
            await MachineControlService.Instance.SetTaskModeAsync(mode);
        }

        // [2026-02-24] 新增 ToggleSingleBlock：切換 Single Block 模式
        [RelayCommand]
        private void ToggleSingleBlock()
        {
            IsSingleBlock = !IsSingleBlock;
            AlarmService.Instance.AddLog("INFO", $"Single Block: {(IsSingleBlock ? "ON" : "OFF")}");
        }

        // [2026-03-04] 新增 ToggleBlockDelete：切換 Block Delete 模式
        [RelayCommand]
        private async Task ToggleBlockDelete()
        {
            bool newVal = !IsBlockDelete;
            IsBlockDelete = newVal;
            Status.IsBlockDelete = newVal;
            AlarmService.Instance.AddLog("INFO", $"Block Delete: {(newVal ? "ON" : "OFF")}");
            await MachineControlService.Instance.SetBlockDeleteAsync(newVal);
        }

        // [2026-03-04] 新增 ToggleOptionalStop：切換 Optional Stop (M01) 模式
        [RelayCommand]
        private async Task ToggleOptionalStop()
        {
            bool newVal = !IsOptionalStop;
            IsOptionalStop = newVal;
            Status.IsOptionalStop = newVal;
            AlarmService.Instance.AddLog("INFO", $"Optional Stop (M01): {(newVal ? "ON" : "OFF")}");
            await MachineControlService.Instance.SetOptionalStopAsync(newVal);
        }

        // [2026-02-24] 新增 AdjustFeedOverride：增減進給率覆蓋百分比（delta = +10 或 -10）
        [RelayCommand]
        private async Task AdjustFeedOverride(string deltaStr)
        {
            if (!double.TryParse(deltaStr, out double delta)) return;
            double newValue = Math.Clamp(Status.FeedOverride + delta, 0, 200);
            await MachineControlService.Instance.SetFeedOverrideAsync(newValue);
        }

        // [2026-02-24] 新增 AdjustSpindleOverride：增減主軸轉速覆蓋百分比（delta = +10 或 -10）
        [RelayCommand]
        private async Task AdjustSpindleOverride(string deltaStr)
        {
            if (!double.TryParse(deltaStr, out double delta)) return;
            double newValue = Math.Clamp(Status.SpindleOverride + delta, 0, 200);
            await MachineControlService.Instance.SetSpindleOverrideAsync(newValue);
        }

        // [2026-03-05] ClearProgram：清除已載入的 G-Code，對齊 PB 版 D_1
        [RelayCommand]
        private async Task ClearProgram()
        {
            // TODO: 清除已載入的 G-Code
            _loadedFileName = null;
            AlarmService.Instance.AddLog("INFO", "Program cleared");
            await Task.CompletedTask;
        }

        // [2026-03-05] 重置 Feed Override 為 100%，對齊 PB 版 D_4
        [RelayCommand]
        private async Task ResetFeedOverride()
        {
            await MachineControlService.Instance.SetFeedOverrideAsync(100);
        }

        // [2026-03-05] 重置 Spindle Override 為 100%，對齊 PB 版 D_4
        [RelayCommand]
        private async Task ResetSpindleOverride()
        {
            await MachineControlService.Instance.SetSpindleOverrideAsync(100);
        }

        // [2026-03-05] 重置 Velocity Override 為 100%（暫靜態），對齊 PB 版 D_4
        [RelayCommand]
        private void ResetVelocityOverride()
        {
            VelocityOverride = 100.0;
        }

        // [2026-03-05] 重置 Rapid Override 為 100%（暫靜態），對齊 PB 版 D_4
        [RelayCommand]
        private void ResetRapidOverride()
        {
            RapidOverride = 100.0;
        }

        // [2026-03-05] 主軸正轉（FWD），對齊 PB 版 D_5
        [RelayCommand]
        private async Task SpindleFwd()
        {
            if (!CanExecuteMotion()) return;
            await MachineControlService.Instance.SendMdiCommandAsync($"M3 S{JogSpindleRpm}");
        }

        // [2026-03-05] 主軸反轉（REV），對齊 PB 版 D_5
        [RelayCommand]
        private async Task SpindleRev()
        {
            if (!CanExecuteMotion()) return;
            await MachineControlService.Instance.SendMdiCommandAsync($"M4 S{JogSpindleRpm}");
        }

        // [2026-03-05] 主軸停止（STOP），對齊 PB 版 D_5
        [RelayCommand]
        private async Task SpindleStop()
        {
            if (!CanExecuteMotion()) return;
            await MachineControlService.Instance.SendMdiCommandAsync("M5");
        }

        // [新增] Reload 指令 (重載當前檔案)
        [RelayCommand]
        private async Task ReloadCommand()
        {
            if (!string.IsNullOrEmpty(_loadedFileName))
            {
                // 重新發送 upload 或 run 指令
                // 這裡暫時只記錄 Log
                AlarmService.Instance.AddLog("INFO", $"Reloading {_loadedFileName}...");
                // await MachineControlService.Instance.UploadGCodeAsync(...)
            }
        }

        // [2026-02-24] DRO 單軸歸零：G10 L20 P<wcs> <axis>0
        // [2026-02-24] 擴充 G59.1-G59.3 支援（P7/P8/P9）
        [RelayCommand]
        private async Task DroZeroAxis(string axisName)
        {
            if (!CanExecuteMotion()) return;
            var wcsMap = new Dictionary<string, int>
                { {"G54",1}, {"G55",2}, {"G56",3}, {"G57",4}, {"G58",5}, {"G59",6},
                  {"G59.1",7}, {"G59.2",8}, {"G59.3",9} };
            if (!wcsMap.TryGetValue(Status.ActiveCoordSystem, out int pNum)) return;
            string cmd = $"G10 L20 P{pNum} {axisName}0";
            AlarmService.Instance.AddLog("INFO", $"DRO Zero {axisName}: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-02-24] DRO 全軸歸零（擴充 G59.1-G59.3）
        [RelayCommand]
        private async Task DroZeroAll()
        {
            if (!CanExecuteMotion()) return;
            var wcsMap = new Dictionary<string, int>
                { {"G54",1}, {"G55",2}, {"G56",3}, {"G57",4}, {"G58",5}, {"G59",6},
                  {"G59.1",7}, {"G59.2",8}, {"G59.3",9} };
            if (!wcsMap.TryGetValue(Status.ActiveCoordSystem, out int pNum)) return;
            var axes = LastValidatedConfig?.GetEnabledAxes() ?? new() { "X", "Y", "Z" };
            string axesPart = string.Join(" ", axes.Select(a => $"{a}0"));
            string cmd = $"G10 L20 P{pNum} {axesPart}";
            AlarmService.Instance.AddLog("INFO", $"DRO Zero All: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-02-24] DRO 單軸原點復歸（樂觀更新：先變紅，等後端回報再變綠）
        [RelayCommand]
        private async Task RefAxis(string axisName)
        {
            var map = new Dictionary<string, int>
                { {"X",0}, {"Y",1}, {"Z",2}, {"A",3}, {"B",4}, {"C",5} };
            if (!map.TryGetValue(axisName, out int idx)) return;
            SetAxisHomed(axisName, false);
            Status.IsAllHomed = false;
            AlarmService.Instance.AddLog("INFO", $"REF {axisName}: HomeAsync({idx})");
            await MachineControlService.Instance.HomeAsync(idx);
        }

        // [2026-02-24] 輔助：依軸名設定 Homed 狀態
        private void SetAxisHomed(string axis, bool value)
        {
            switch (axis)
            {
                case "X": Status.IsXHomed = value; break;
                case "Y": Status.IsYHomed = value; break;
                case "Z": Status.IsZHomed = value; break;
                case "A": Status.IsAHomed = value; break;
                case "B": Status.IsBHomed = value; break;
                case "C": Status.IsCHomed = value; break;
            }
        }

        // [2026-02-24] 機台類型即時連動：由 SettingsVM 呼叫，更新 DRO/JOG/Offsets 軸可見性
        public void ApplyMachineType(MachineType machineType, List<string> enabledAxes)
        {
            // DRO + JOG 自動更新（綁定 MainViewModel.IsAxisA/B/CEnabled）
            IsAxisAEnabled = enabledAxes.Contains("A");
            IsAxisBEnabled = enabledAxes.Contains("B");
            IsAxisCEnabled = enabledAxes.Contains("C");

            // Offsets Tab：G10 指令軸數 + 欄位可見性
            OffsetsVM.EnabledAxes = enabledAxes;
            OffsetsVM.IsAxisAEnabled = IsAxisAEnabled;
            OffsetsVM.IsAxisBEnabled = IsAxisBEnabled;
            OffsetsVM.IsAxisCEnabled = IsAxisCEnabled;

            // [2026-03-03] Tool Tab：刀具表欄位可見性同步
            ToolTableVM.IsAxisAEnabled = IsAxisAEnabled;
            ToolTableVM.IsAxisBEnabled = IsAxisBEnabled;
            ToolTableVM.IsAxisCEnabled = IsAxisCEnabled;

            AlarmService.Instance.AddLog("INFO", $"MachineType changed: {machineType} → axes={string.Join(",", enabledAxes)}");
        }

        // [2026-03-06] ESC = 全機停止（abort）：不分狀態，直接送 cnc_cmd.abort() + 清除警報
        [RelayCommand]
        private async Task EmergencyAbort()
        {
            AlarmService.Instance.AddLog("WARN", "ESC 全機停止");
            await MachineControlService.Instance.StopAsync();
            AlarmService.Instance.ClearActiveAlarms();
            UpdateHeaderStatus();
        }

        // [新增] 清除警報指令
        [RelayCommand]
        private void ClearAlarms()
        {
            AlarmService.Instance.ClearActiveAlarms();
            // 強制立即刷新一次 Header，讓它變回綠燈，不用等 Timer
            UpdateHeaderStatus();
        }

    }
}