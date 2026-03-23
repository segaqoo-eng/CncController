using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    // [2026-03-12] 從 MainViewModel.cs 拆分：狀態輪詢
    // [2026-03-13] 重構：Timer 50ms + 分頻計數器（Errors/Stats 每 20 次 tick ≈ 1s）
    public partial class MainViewModel
    {
        // 跑馬燈與連線狀態控制變數
        private DateTime _lastMarqueeTime = DateTime.MinValue;
        private int _marqueeIndex = 0;
        private MachineControlService.ConnectionState _connectionState = MachineControlService.ConnectionState.Disconnected;

        // [2026-03-13] 分頻計數器：低頻任務每 N 次 tick 才執行
        private int _pollTickCount = 0;
        private const int SlowPollInterval = 20; // 每 20 tick（≈1s）執行 Errors/Stats

        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();
            try
            {
                _pollTickCount++;

                // 1. 每次 tick：輪詢機台狀態（後端已快取，<1ms 回應）
                var data = await PollMachineStatus();

                // 2. 傳給 SettingsVM 的 IO 監控
                if (data != null)
                    SettingsVM.UpdateMachineStatus(data);

                // [2026-02-11] 連動加工計時器
                if (data != null && data.Interp_State == "RUNNING" && !_cycleTimer.IsEnabled)
                {
                    _cycleStartTime = DateTime.Now;
                    _cycleTimer.Start();
                }
                else if (data != null && data.Interp_State == "IDLE" && _cycleTimer.IsEnabled)
                {
                    _cycleTimer.Stop();
                }

                // [2026-03-13] 低頻任務：每 20 tick（≈1s）才執行
                if (_pollTickCount >= SlowPollInterval)
                {
                    _pollTickCount = 0;

                    // [2026-03-12] 定期拉取加工統計
                    if (IsConnected) await PollMachiningStats();

                    // 抓錯誤
                    if (IsConnected)
                        await PollErrors();
                }

                // [2026-03-16] 更新按鈕防呆狀態（每 tick 都要跑，確保即時反應）
                UpdateCanExecuteStates();

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
            var errors = await MachineControlService.Instance.GetErrorsAsync();

            if (errors != null && errors.Count > 0)
            {
                foreach (var err in errors)
                {
                    // 雜訊過濾
                    if (string.IsNullOrWhiteSpace(err.Text)) continue;
                    if (err.Kind == "-1") continue;
                    if (err.Text.Contains("already exists")) continue;
                    if (err.Text.Contains("unrecognized error")) continue;

                    // 錯誤分類
                    LogType type;
                    switch (err.Kind)
                    {
                        case "11": // EMC_OPERATOR_ERROR
                            type = LogType.Error;
                            break;
                        case "1":  // EMC_OPERATOR_TEXT
                        case "2":  // EMC_OPERATOR_DISPLAY
                            type = LogType.Info;
                            break;
                        default:
                            type = LogType.Info;
                            break;
                    }

                    AlarmService.Instance.AddLog(type, err.Text);
                }
            }
        }

        private async Task<MachineStatusData?> PollMachineStatus()
        {
            var (state, data) = await MachineControlService.Instance.GetStatusAsync();

            _connectionState = state;
            ServerVersionDisplay = MachineControlService.Instance.ServerVersion;

            switch (state)
            {
                case MachineControlService.ConnectionState.Connected:
                    IsConnected = true;

                    if (data != null)
                    {
                        // [安全] 先計算所有新狀態快照，再依序套用
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
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;
                    break;

                case MachineControlService.ConnectionState.Disconnected:
                default:
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;
                    break;
            }

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
                if (data.DTG.TryGetValue("A", out double da)) Status.DtgA = da;
                if (data.DTG.TryGetValue("B", out double db)) Status.DtgB = db;
                if (data.DTG.TryGetValue("C", out double dc)) Status.DtgC = dc;
            }

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

            Status.FeedOverride = data.Feed_Override;
            Status.SpindleOverride = data.Spindle_Override;

            Status.ToolNumber = data.Tool_Number;
            Status.ToolLength = data.Tool_Length;
            Status.ToolDiameter = data.Tool_Diameter;

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

            if (data.G92_Offset != null)
            {
                if (data.G92_Offset.TryGetValue("X", out double g92x)) Status.G92X = g92x;
                if (data.G92_Offset.TryGetValue("Y", out double g92y)) Status.G92Y = g92y;
                if (data.G92_Offset.TryGetValue("Z", out double g92z)) Status.G92Z = g92z;
                if (data.G92_Offset.TryGetValue("A", out double g92a)) Status.G92A = g92a;
                if (data.G92_Offset.TryGetValue("B", out double g92b)) Status.G92B = g92b;
                if (data.G92_Offset.TryGetValue("C", out double g92c)) Status.G92C = g92c;
            }

            if (data.Tool_Offset_XYZ != null)
            {
                if (data.Tool_Offset_XYZ.TryGetValue("X", out double tox)) Status.ToolOffsetX = tox;
                if (data.Tool_Offset_XYZ.TryGetValue("Y", out double toy)) Status.ToolOffsetY = toy;
                if (data.Tool_Offset_XYZ.TryGetValue("Z", out double toz)) Status.ToolOffsetZ = toz;
            }

            if (!string.IsNullOrEmpty(data.Task_Mode))
                Status.TaskMode = data.Task_Mode;

            // [2026-03-06] Probe Input 訊號狀態
            Status.IsProbeInput = data.Probe_Input;

            // [2026-03-04] Block Delete / Optional Stop / Current Line
            IsBlockDelete = data.Block_Delete;
            Status.IsBlockDelete = data.Block_Delete;
            IsOptionalStop = data.Optional_Stop;
            Status.IsOptionalStop = data.Optional_Stop;
            Status.CurrentLine = data.Current_Line;
            // [2026-03-12] G-Code 總行數
            Status.ProgramTotalLines = data.Program_Total_Lines;

            // [2026-03-12] 更新加工進度預估
            UpdateMachiningProgress();

            Status.InterpState = data.Interp_State;

            // [2026-03-09] 主軸 Encoder 角度
            Status.SpindlePosition = data.Spindle_Position;

            // [2026-03-10] 主軸方向
            Status.SpindleDirection = data.Spindle_Direction;

            if (!string.IsNullOrEmpty(data.Active_WCS))
            {
                Status.ActiveCoordSystem = data.Active_WCS;
                OffsetsVM.ActiveOffset = data.Active_WCS;
            }
        }

        // [2026-03-04] 跑馬燈與狀態顯示邏輯 + 啟動寬限期 + RETRY
        private void UpdateHeaderStatus()
        {
            var alarms = AlarmService.Instance.ActiveAlarms;

            // 1. 優先級最高：警報輪播 (Marquee)
            if (alarms.Count > 0)
            {
                if ((DateTime.Now - _lastMarqueeTime).TotalSeconds >= 1.0)
                {
                    _lastMarqueeTime = DateTime.Now;
                    _marqueeIndex++;
                }

                if (_marqueeIndex >= alarms.Count) _marqueeIndex = 0;

                if (_marqueeIndex < alarms.Count)
                {
                    var currentLog = alarms[_marqueeIndex];
                    SystemStatus = $"{_marqueeIndex + 1}/{alarms.Count} {currentLog.DisplayMessage}";
                    SystemStatusColor = currentLog.Type == LogType.Error ? Colors.Red : Colors.Orange;
                }
                ShowRetryButton = false;
                return;
            }

            // 2. 啟動寬限期
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

            // 3. 連線狀態檢查
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

            // 4. 連線成功：隱藏 RETRY
            ShowRetryButton = false;

            // 5. 正常狀態
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
    }
}
