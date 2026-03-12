// [2026-03-04] 新增 ProbingViewModel：探測循環分頁 ViewModel
//              首期實作 Outside Corners（外角/邊緣/中心探測）
//              9 宮格按鈕 → RunProbeAsync → 更新結果 → 可選寫入 WCS
//              對齊 PB 版 Probing_outSide_2.png 參數佈局
// [2026-03-04] 新增 Inside Corners（內角探測）— 對齊 PB 版 Probing_INSide.png
// [2026-03-04] 新增 Boss & Pocket（凸台/口袋探測）— 對齊 PB 版 Probing_Boss.png
// [2026-03-04] 新增 Ridge & Valley / Edge Angle / Calibrate — 對齊 PB 版參考圖
// TODO: 未來可改為 NGC 副程式方案（9 個 .ngc + o<probe_xxx> call）
//       支援 fast/slow 兩段式探測，INI 需加 SUBROUTINE_PATH
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class ProbingViewModel : ObservableObject
    {
        // [2026-03-06] 建構子：載入持久化的探測參數
        public ProbingViewModel()
        {
            LoadProbeSettings();
        }

        // [2026-03-04] 即時機台狀態（從 MainViewModel 傳入）
        [ObservableProperty] private MachineStatus _machineStatus;

        // [2026-03-04] 子頁籤切換（首期僅 OutsideCorners 啟用，預留擴展）
        [ObservableProperty] private string _selectedProbeTab = "OutsideCorners";

        // [2026-03-04] 探測模式：TouchProbe / ToolSetter
        [ObservableProperty] private string _selectedProbeMode = "TouchProbe";

        // [2026-03-04] WCS 選擇（G54~G59.3）
        [ObservableProperty] private string _selectedWcs = "G54";

        // [2026-03-04] 僅顯示結果，不寫入 WCS
        [ObservableProperty] private bool _isProbePositionOnly;

        // =====================================================================
        // [2026-03-04] 探測參數（對齊 PB 版 Probing_outSide_2.png）
        // =====================================================================
        [ObservableProperty] private int _probeToolNumber = 1;
        [ObservableProperty] private double _probeSlowFeed = 0.3;
        [ObservableProperty] private double _probeTraverseFr = 300.0;
        [ObservableProperty] private double _probeFastFeed = 1.0;
        [ObservableProperty] private double _latchDistance = 0.5;
        [ObservableProperty] private double _extraProbeDepth = 2.0;

        // [2026-03-04] 保留原始參數（後端使用）
        [ObservableProperty] private double _maxXyDistance = 20.0;
        [ObservableProperty] private double _maxZDistance = 20.0;
        [ObservableProperty] private double _xyClearance = 5.0;
        [ObservableProperty] private double _zClearance = 5.0;
        // [2026-03-04] STEP OFF WIDTH（對齊 PB 版參數）
        [ObservableProperty] private double _stepOffWidth;

        // [2026-03-04] Boss/Pocket 近似直徑（使用者輸入，決定外移距離）
        [ObservableProperty] private double _bossPocketDiam = 20.0;
        // [2026-03-04] Boss/Pocket 特徵中心相對於當前位置的近似偏移（使用者輸入）
        [ObservableProperty] private double _bossPocketOffsetX;
        [ObservableProperty] private double _bossPocketOffsetY;
        // [2026-03-04] Ridge/Valley 特徵中心相對於當前位置的近似偏移（使用者輸入）
        [ObservableProperty] private double _ridgeValleyOffsetX;
        [ObservableProperty] private double _ridgeValleyOffsetY;
        // =====================================================================
        // [2026-03-04] 探測結果（對齊 PB 版 Probing_Param.PNG 3×4 結果佈局）
        // =====================================================================
        // Row 0: X- PROBED / X+ PROBED / X WIDTH
        [ObservableProperty] private double _resultX;       // X- PROBED
        [ObservableProperty] private double _resultXOffset;  // X+ PROBED
        [ObservableProperty] private double _resultWidthX;   // X WIDTH（moved here）
        // Row 1: Y- PROBED / Y+ PROBED / Y WIDTH
        [ObservableProperty] private double _resultY;       // Y- PROBED
        [ObservableProperty] private double _resultYOffset;  // Y+ PROBED
        [ObservableProperty] private double _resultWidthY;   // Y WIDTH（moved here）
        // Row 2: Z- PROBED / DIAM ∅ / X CENTER
        [ObservableProperty] private double _resultZ;
        [ObservableProperty] private double _resultEdgeWidth; // DIAM ∅
        [ObservableProperty] private double _resultXCenter;
        // Row 3: EDGE Δ / EDGE ∠ / Y CENTER
        [ObservableProperty] private double _resultEdgeDelta;
        [ObservableProperty] private double _resultYCenter;

        // [2026-03-04] 探測狀態
        [ObservableProperty] private string _probeStatusText = "";
        [ObservableProperty] private bool _isProbing;

        // [2026-03-04] MDI 自由輸入
        [ObservableProperty] private string _mdiInput = "";

        // [2026-03-06] 停止探測：呼叫後端 abort + 前端重設 IsProbing
        [RelayCommand]
        private async Task StopProbe()
        {
            AlarmService.Instance.AddLog("WARN", "探測手動停止");
            await MachineControlService.Instance.StopAsync();
            ProbeStatusText = "探測已停止";
            IsProbing = false;
        }

        // =====================================================================
        // [2026-03-04] PROBE HELP 頁面瀏覽（7 張圖片）
        // =====================================================================
        [ObservableProperty] private int _helpPageIndex = 1;  // 1~7
        [ObservableProperty] private string _helpImageSource = "/Resources/Images/HELP/Image(1).png";

        [RelayCommand]
        private void HelpPrevPage()
        {
            // [2026-03-04] 循環：1 再按 PREV → 跳到 7
            HelpPageIndex = HelpPageIndex > 1 ? HelpPageIndex - 1 : 7;
            HelpImageSource = $"/Resources/Images/HELP/Image({HelpPageIndex}).png";
        }

        [RelayCommand]
        private void HelpNextPage()
        {
            // [2026-03-04] 循環：7 再按 NEXT → 跳回 1
            HelpPageIndex = HelpPageIndex < 7 ? HelpPageIndex + 1 : 1;
            HelpImageSource = $"/Resources/Images/HELP/Image({HelpPageIndex}).png";
        }

        // =====================================================================
        // [2026-03-04] Outside Corners 9 宮格探測按鈕 Commands
        // =====================================================================

        [RelayCommand]
        private async Task ProbeNW() => await ExecuteProbe("outside_corner", "NW");

        [RelayCommand]
        private async Task ProbeN() => await ExecuteProbe("edge", "N");

        [RelayCommand]
        private async Task ProbeNE() => await ExecuteProbe("outside_corner", "NE");

        [RelayCommand]
        private async Task ProbeW() => await ExecuteProbe("edge", "W");

        [RelayCommand]
        private async Task ProbeCenter() => await ExecuteProbe("center", "CENTER");

        [RelayCommand]
        private async Task ProbeE() => await ExecuteProbe("edge", "E");

        [RelayCommand]
        private async Task ProbeSW() => await ExecuteProbe("outside_corner", "SW");

        [RelayCommand]
        private async Task ProbeS() => await ExecuteProbe("edge", "S");

        [RelayCommand]
        private async Task ProbeSE() => await ExecuteProbe("outside_corner", "SE");

        // =====================================================================
        // [2026-03-04] Inside Corners 9 宮格探測按鈕 Commands
        // 內角探測：探針在口袋內，向牆壁探測（方向與 Outside 相反）
        // =====================================================================

        [RelayCommand]
        private async Task ProbeInsideNW() => await ExecuteProbe("inside_corner", "NW");

        [RelayCommand]
        private async Task ProbeInsideN() => await ExecuteProbe("inside_edge", "N");

        [RelayCommand]
        private async Task ProbeInsideNE() => await ExecuteProbe("inside_corner", "NE");

        [RelayCommand]
        private async Task ProbeInsideW() => await ExecuteProbe("inside_edge", "W");

        [RelayCommand]
        private async Task ProbeInsideCenter() => await ExecuteProbe("center", "CENTER");

        [RelayCommand]
        private async Task ProbeInsideE() => await ExecuteProbe("inside_edge", "E");

        [RelayCommand]
        private async Task ProbeInsideSW() => await ExecuteProbe("inside_corner", "SW");

        [RelayCommand]
        private async Task ProbeInsideS() => await ExecuteProbe("inside_edge", "S");

        [RelayCommand]
        private async Task ProbeInsideSE() => await ExecuteProbe("inside_corner", "SE");

        // =====================================================================
        // [2026-03-04] Boss & Pocket 探測按鈕 Commands（3×2 佈局）
        // Boss：從外部向內探測（X 兩側 / Y 兩側 / XY 四面）
        // Pocket：從內部向外探（X 兩壁 / Y 兩壁 / XY 四壁）
        // =====================================================================

        [RelayCommand]
        private async Task ProbeBossX() => await ExecuteProbe("boss_x", "CENTER");

        [RelayCommand]
        private async Task ProbeBossY() => await ExecuteProbe("boss_y", "CENTER");

        [RelayCommand]
        private async Task ProbeBossXy() => await ExecuteProbe("boss_xy", "CENTER");

        [RelayCommand]
        private async Task ProbePocketX() => await ExecuteProbe("pocket_x", "CENTER");

        [RelayCommand]
        private async Task ProbePocketY() => await ExecuteProbe("pocket_y", "CENTER");

        [RelayCommand]
        private async Task ProbePocketXy() => await ExecuteProbe("pocket_xy", "CENTER");

        // =====================================================================
        // [2026-03-04] Ridge & Valley 探測按鈕 Commands（3×2 佈局）
        // Ridge：從外部向內探（同 Boss 邏輯）
        // Valley：從內部向外探（同 Pocket 邏輯）
        // =====================================================================

        [RelayCommand]
        private async Task ProbeRidgeX() => await ExecuteProbe("ridge_x", "CENTER");

        [RelayCommand]
        private async Task ProbeRidgeY() => await ExecuteProbe("ridge_y", "CENTER");

        [RelayCommand]
        private async Task ProbeRidgeXy() => await ExecuteProbe("ridge_xy", "CENTER");

        [RelayCommand]
        private async Task ProbeValleyX() => await ExecuteProbe("valley_x", "CENTER");

        [RelayCommand]
        private async Task ProbeValleyY() => await ExecuteProbe("valley_y", "CENTER");

        [RelayCommand]
        private async Task ProbeValleyXy() => await ExecuteProbe("valley_xy", "CENTER");

        // =====================================================================
        // [2026-03-04] Edge Angle 邊角角度探測 Commands（3×2 佈局）
        // 在邊緣上探 2 點，計算角度 → 可設定 WCS 旋轉
        // =====================================================================

        // [2026-03-04] Edge Angle 3×3 九宮格 Commands（對齊 PB 版）
        [RelayCommand]
        private async Task ProbeAngleNW() => await ExecuteProbe("edge_angle", "NW");
        [RelayCommand]
        private async Task ProbeAngleN() => await ExecuteProbe("edge_angle", "N");
        [RelayCommand]
        private async Task ProbeAngleNE() => await ExecuteProbe("edge_angle", "NE");
        [RelayCommand]
        private async Task ProbeAngleW() => await ExecuteProbe("edge_angle", "W");
        [RelayCommand]
        private async Task ProbeAngleCenter() => await ExecuteProbe("edge_angle", "CENTER");
        [RelayCommand]
        private async Task ProbeAngleE() => await ExecuteProbe("edge_angle", "E");
        [RelayCommand]
        private async Task ProbeAngleSW() => await ExecuteProbe("edge_angle", "SW");
        [RelayCommand]
        private async Task ProbeAngleS() => await ExecuteProbe("edge_angle", "S");
        [RelayCommand]
        private async Task ProbeAngleSE() => await ExecuteProbe("edge_angle", "SE");

        // [2026-03-04] Edge Angle 結果角度
        [ObservableProperty] private double _resultAngle;
        // [2026-03-04] Edge Width 輸入（使用者提供，作為探測間距參考）
        [ObservableProperty] private double _edgeWidthInput;

        // [2026-03-04] SET ROTATION WCO — 將角度寫入 WCS 旋轉
        [RelayCommand]
        private async Task SetRotationWco()
        {
            if (ResultAngle == 0) return;
            int wcsIndex = SelectedWcs switch
            {
                "G54" => 1, "G55" => 2, "G56" => 3, "G57" => 4,
                "G58" => 5, "G59" => 6, "G59.1" => 7, "G59.2" => 8, "G59.3" => 9,
                _ => 1
            };
            string cmd = $"G10 L2 P{wcsIndex} R{ResultAngle:F4}";
            bool ok = await MachineControlService.Instance.SendMdiCommandAsync(cmd);
            ProbeStatusText = ok
                ? $"已設定 {SelectedWcs} 旋轉 R={ResultAngle:F4}°"
                : $"設定旋轉失敗：{cmd}";
            AlarmService.Instance.AddLog(ok ? "INFO" : "ERROR", $"SetRotation: {cmd} → {(ok ? "OK" : "FAIL")}");
        }

        // =====================================================================
        // [2026-03-04] Calibrate 校正 Commands + Properties（對齊 PB 版）
        // =====================================================================

        // 頂列：PROBE CALIBRATION OFFSET（單一值）
        [ObservableProperty] private double _probeCalOffset;
        // 中段：CALIBRATION DIAMETER（校正環/棒已知直徑）
        [ObservableProperty] private double _calibrationDiameter;
        // 下段：CALIBRATION WIDTH X / Y（校正方塊已知寬度）
        [ObservableProperty] private double _calibrationWidthX;
        [ObservableProperty] private double _calibrationWidthY;

        [RelayCommand]
        private void ProbeCalReset()
        {
            ProbeCalOffset = 0;
            CalibrationDiameter = 0;
            CalibrationWidthX = 0;
            CalibrationWidthY = 0;
            ProbeStatusText = "校正已重置";
            AlarmService.Instance.AddLog("INFO", "Probe calibration reset");
        }

        // 4 個校正視覺化按鈕：環孔內探 / 環外探 / 方孔內探 / 方外探
        [RelayCommand]
        private async Task CalRingInside() => await ExecuteProbe("cal_ring_inside", "CENTER");
        [RelayCommand]
        private async Task CalRingOutside() => await ExecuteProbe("cal_ring_outside", "CENTER");
        [RelayCommand]
        private async Task CalSquareInside() => await ExecuteProbe("cal_square_inside", "CENTER");
        [RelayCommand]
        private async Task CalSquareOutside() => await ExecuteProbe("cal_square_outside", "CENTER");

        // 3 個動作按鈕：AVG XY / X / Y
        [RelayCommand]
        private async Task CalOnAvgXyError() => await ExecuteProbe("cal_avg_xy", "CENTER");
        [RelayCommand]
        private async Task CalOnXError() => await ExecuteProbe("cal_x_error", "CENTER");
        [RelayCommand]
        private async Task CalOnYError() => await ExecuteProbe("cal_y_error", "CENTER");

        // =====================================================================
        // [2026-03-05] TOOL SETTER 參數（對齊 PB 版 TOOLSET_Param.png）
        // =====================================================================

        // 左面板上段
        [ObservableProperty] private double _tsSpindleZero;         // SPINDLE ZERO
        [ObservableProperty] private double _tsToolSetterX;         // X position
        [ObservableProperty] private double _tsToolSetterY;         // Y position
        [ObservableProperty] private double _tsToolSetterZ;         // Z position
        [ObservableProperty] private double _tsToolDiamProbe;       // TOOL DIAM PROBE
        [ObservableProperty] private double _tsToolDiamOffset;      // TOOL DIAM OFFSET
        [ObservableProperty] private int _tsToolOffsetDirection;    // TOOL OFFSET DIRECTION（0=center）

        // 右面板參數
        [ObservableProperty] private double _tsFastProbeFr;         // FAST PROBE FR
        [ObservableProperty] private double _tsSlowProbeFr;         // SLOW PROBE FR
        [ObservableProperty] private double _tsTraverseFr;          // TRAVERSE FR
        [ObservableProperty] private double _tsZMaxTravel;          // Z MAX TRAVEL
        [ObservableProperty] private double _tsXyMaxTravel;         // XY MAX TRAVEL
        [ObservableProperty] private double _tsRetractDist;         // RETRACT DIST
        [ObservableProperty] private double _tsBreakageTolerance;   // BREAKAGE TOLERANCE
        [ObservableProperty] private double _tsUserParam1;          // USER PARAM 1
        [ObservableProperty] private double _tsUserParam2;          // USER PARAM 2

        // [2026-03-05] TOOL SETTER 頂列模式按鈕（對齊 PB 版 TOOLSET.png 頂列）
        [ObservableProperty] private string _tsSelectedMode = "SpindleZero";

        [RelayCommand]
        private void TsSwitchMode(string mode)
        {
            TsSelectedMode = mode;
        }

        // [2026-03-05] PROBE SPINDLE NOSE ZERO — 探測主軸鼻端歸零
        [RelayCommand]
        private async Task TsProbeSpindleNoseZero()
        {
            if (IsProbing) return;
            IsProbing = true;
            ProbeStatusText = "Tool Setter: Probing Spindle Nose Zero...";
            try
            {
                // G38.2 Z 軸向下探測（使用 Tool Setter 參數）
                var parameters = new ProbeParameters
                {
                    ProbeToolNumber = ProbeToolNumber,  // [2026-03-06] 探針刀號（自動 G43 + 半徑補正）
                    SearchSpeed = TsFastProbeFr > 0 ? TsFastProbeFr : ProbeFastFeed,
                    TraverseSpeed = TsTraverseFr > 0 ? TsTraverseFr : ProbeTraverseFr,
                    MaxZDistance = TsZMaxTravel > 0 ? TsZMaxTravel : MaxZDistance,
                };

                // [2026-03-06] 手動模擬：移除自動觸發位置設定（使用者按 SIM TRIGGER 按鈕觸發 probe-in）

                var result = await MachineControlService.Instance.RunProbeAsync("tool_setter_z", "S", parameters);
                if (result != null && result.Tripped)
                {
                    TsSpindleZero = result.Z;
                    ProbeStatusText = $"Spindle Nose Zero = {result.Z:F4}";
                    AlarmService.Instance.AddLog("INFO", $"ToolSetter SpindleNoseZero: Z={result.Z:F4}");
                }
                else
                {
                    ProbeStatusText = "Tool Setter: Probe not tripped";
                    AlarmService.Instance.AddLog("WARN", "ToolSetter SpindleNoseZero: not tripped");
                }
            }
            catch (Exception ex)
            {
                ProbeStatusText = $"Tool Setter error: {ex.Message}";
                AlarmService.Instance.AddLog("ERROR", $"ToolSetter SpindleNoseZero: {ex.Message}");
            }
            finally { IsProbing = false; }
        }

        // [2026-03-05] SET TOOL TOUCH OFF POS — 記錄 Tool Setter 安裝位置
        [RelayCommand]
        private void TsSetToolTouchOffPos()
        {
            if (MachineStatus == null) return;
            TsToolSetterX = MachineStatus.X;
            TsToolSetterY = MachineStatus.Y;
            TsToolSetterZ = MachineStatus.Z;
            ProbeStatusText = $"Tool Touch Off Pos set: X={TsToolSetterX:F4} Y={TsToolSetterY:F4} Z={TsToolSetterZ:F4}";
            AlarmService.Instance.AddLog("INFO", $"ToolSetter TouchOffPos: X={TsToolSetterX:F4} Y={TsToolSetterY:F4} Z={TsToolSetterZ:F4}");
        }

        // [2026-03-05] TOOL OFFSET DIRECTION 方向按鈕
        [RelayCommand]
        private void TsSetOffsetDirection(string dir)
        {
            TsToolOffsetDirection = dir switch
            {
                "BACK" => 1,
                "LEFT" => 2,
                "RIGHT" => 3,
                "FRONT" => 4,
                _ => 0
            };
        }

        // [2026-03-05] UPDATE TOOL SETTER PARAMETERS — 儲存參數
        [RelayCommand]
        private void TsUpdateParameters()
        {
            AlarmService.Instance.AddLog("INFO",
                $"ToolSetter Params: SpindleZero={TsSpindleZero:F4} FastFR={TsFastProbeFr} SlowFR={TsSlowProbeFr} " +
                $"TraverseFR={TsTraverseFr} ZMax={TsZMaxTravel:F4} XYMax={TsXyMaxTravel:F4} " +
                $"Retract={TsRetractDist:F4} Breakage={TsBreakageTolerance:F4}");
            SaveProbeSettings();
            ProbeStatusText = "對刀儀參數已儲存";
        }

        // =====================================================================
        // 統一探測執行
        // =====================================================================

        // [2026-03-04] 統一探測執行：呼叫後端 /v2/probe/run → 更新結果 → 可選寫入 WCS
        private async Task ExecuteProbe(string probeType, string direction)
        {
            if (IsProbing) return;
            IsProbing = true;
            ProbeStatusText = $"探測中... ({direction})";

            try
            {
                var parameters = new ProbeParameters
                {
                    ProbeToolNumber = ProbeToolNumber,  // [2026-03-06] 探針刀號（自動 G43 + 半徑補正）
                    TraverseSpeed = ProbeTraverseFr,
                    SearchSpeed = ProbeFastFeed,
                    MaxXYDistance = MaxXyDistance,
                    MaxZDistance = MaxZDistance,
                    XYClearance = XyClearance,
                    ZClearance = ZClearance,
                    ExtraDepth = ExtraProbeDepth,
                    Diameter = BossPocketDiam,
                    // [2026-03-04] 特徵中心近似偏移：ridge/valley 用獨立欄位
                    OffsetX = probeType.StartsWith("ridge") || probeType.StartsWith("valley")
                        ? RidgeValleyOffsetX : BossPocketOffsetX,
                    OffsetY = probeType.StartsWith("ridge") || probeType.StartsWith("valley")
                        ? RidgeValleyOffsetY : BossPocketOffsetY,
                    EdgeWidth = EdgeWidthInput  // [2026-03-04] Edge Angle 邊緣寬度
                };

                // [2026-03-06] 探針模擬：改為手動觸發（使用者按 PROBE INPUT 按鈕），不再自動設定目標位置

                // [2026-03-05] DEBUG：記錄探測命令與參數
                AlarmService.Instance.AddLog("DEBUG",
                    $"Probe {probeType}/{direction}: Speed={parameters.SearchSpeed} MaxXY={parameters.MaxXYDistance:F2} " +
                    $"MaxZ={parameters.MaxZDistance:F2} XYClr={parameters.XYClearance:F2} ZClr={parameters.ZClearance:F2} " +
                    $"Depth={parameters.ExtraDepth:F2} Diam={parameters.Diameter:F2} OffX={parameters.OffsetX:F2} OffY={parameters.OffsetY:F2}");

                // [2026-03-06] 傳送 WCS + ProbePositionOnly 給後端，由後端直接寫入 WCS
                var result = await MachineControlService.Instance.RunProbeAsync(
                    probeType, direction, parameters, SelectedWcs, IsProbePositionOnly);

                if (result == null)
                {
                    ProbeStatusText = "探測失敗：無回應";
                    AlarmService.Instance.AddLog("ERROR", $"Probe {direction}: 無回應");
                    return;
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    ProbeStatusText = $"探測錯誤：{result.Error}";
                    AlarmService.Instance.AddLog("ERROR", $"Probe {direction}: {result.Error}");
                    return;
                }

                if (!result.Tripped)
                {
                    ProbeStatusText = "探測未觸發：探針未接觸工件";
                    AlarmService.Instance.AddLog("WARN", $"Probe {direction}: 未觸發");
                    return;
                }

                // [2026-03-05] DEBUG：記錄探測結果
                AlarmService.Instance.AddLog("DEBUG",
                    $"Probe {probeType}/{direction} Result: Tripped={result.Tripped} X={result.X:F4} Y={result.Y:F4} Z={result.Z:F4}");

                // [2026-03-04] 更新 PROBE 結果
                ResultX = result.X;
                ResultY = result.Y;
                ResultZ = result.Z;

                // [2026-03-04] 計算 OFFSET（探測位置 - 目前工件座標）
                if (MachineStatus != null)
                {
                    ResultXOffset = result.X - MachineStatus.WorkX;
                    ResultYOffset = result.Y - MachineStatus.WorkY;
                }

                // [2026-03-04] Edge Angle 結果：更新角度
                if (probeType == "edge_angle")
                {
                    ResultAngle = result.Angle;
                    ResultEdgeWidth = result.EdgeWidth;
                }
                // [2026-03-04] Boss/Pocket/Ridge/Valley 結果：更新實測寬度
                if (probeType.StartsWith("boss") || probeType.StartsWith("pocket") ||
                    probeType.StartsWith("ridge") || probeType.StartsWith("valley"))
                {
                    ResultWidthX = result.WidthX;
                    ResultWidthY = result.WidthY;
                }

                ProbeStatusText = $"探測完成 X={result.X:F4} Y={result.Y:F4} Z={result.Z:F4}";
                AlarmService.Instance.AddLog("INFO",
                    $"Probe {direction}: X={result.X:F4} Y={result.Y:F4} Z={result.Z:F4}");

                // [2026-03-06] WCS 寫入已移至後端（避免 MDI 時序衝突）
                if (!IsProbePositionOnly)
                    ProbeStatusText += $" → 已寫入 {SelectedWcs}";
            }
            catch (System.Exception ex)
            {
                ProbeStatusText = $"探測異常：{ex.Message}";
                AlarmService.Instance.AddLog("ERROR", $"Probe {direction} exception: {ex.Message}");
            }
            finally
            {
                IsProbing = false;
            }
        }

        // [2026-03-12] 探針模擬旗標（供 XAML 綁定，非模擬時隱藏 SIM 區塊）
        public bool IsProbeSimulation => ConfigurationService.IsProbeSimulation;

        // [2026-03-05] 探針模擬：依探測方向計算觸發位置，設定 HAL comp signal
        // 模擬碰觸點 = 當前位置 + 探測方向 * 最大距離 * 70%
        // 非探測軸設為 99999（不觸發）
        // [2026-03-06] 手動模擬：切換 probe-in 訊號（0→1 或 1→0）
        [RelayCommand]
        private async Task ToggleProbeInput()
        {
            if (MachineStatus == null) return;
            bool newState = !MachineStatus.IsProbeInput;
            bool ok = await MachineControlService.Instance.HalSetSignalAsync("probe-in", newState ? 1 : 0);
            if (ok)
            {
                AlarmService.Instance.AddLog("DEBUG", $"Probe input manually set to {(newState ? "HIGH" : "LOW")}");
            }
            else
            {
                AlarmService.Instance.AddLog("ERROR", "Failed to toggle probe-in signal");
            }
        }


        // =====================================================================
        // [2026-03-06] 探測參數持久化（probe_settings.json）
        // TOUCH PROBE + TOOL SETTER 參數存在同一支檔案
        // =====================================================================
        private const string ProbeSettingsFile = "probe_settings.json";
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        public void SaveProbeSettings()
        {
            try
            {
                var data = new
                {
                    // TOUCH PROBE
                    ProbeToolNumber,
                    ProbeSlowFeed,
                    ProbeTraverseFr,
                    ProbeFastFeed,
                    LatchDistance,
                    ExtraProbeDepth,
                    MaxXyDistance,
                    MaxZDistance,
                    XyClearance,
                    ZClearance,
                    StepOffWidth,
                    BossPocketDiam,
                    BossPocketOffsetX,
                    BossPocketOffsetY,
                    RidgeValleyOffsetX,
                    RidgeValleyOffsetY,
                    // TOOL SETTER
                    TsSpindleZero,
                    TsToolSetterX,
                    TsToolSetterY,
                    TsToolSetterZ,
                    TsToolDiamProbe,
                    TsToolDiamOffset,
                    TsToolOffsetDirection,
                    TsFastProbeFr,
                    TsSlowProbeFr,
                    TsTraverseFr,
                    TsZMaxTravel,
                    TsXyMaxTravel,
                    TsRetractDist,
                    TsBreakageTolerance,
                    TsUserParam1,
                    TsUserParam2
                };
                string json = JsonSerializer.Serialize(data, _jsonOpts);
                File.WriteAllText(ProbeSettingsFile, json);
                AlarmService.Instance.AddLog("INFO", "探測參數已儲存至 probe_settings.json");
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERROR", $"儲存探測參數失敗: {ex.Message}");
            }
        }

        public void LoadProbeSettings()
        {
            try
            {
                if (!File.Exists(ProbeSettingsFile)) return;
                string json = File.ReadAllText(ProbeSettingsFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // TOUCH PROBE
                if (root.TryGetProperty("ProbeToolNumber", out var v)) ProbeToolNumber = v.GetInt32();
                if (root.TryGetProperty("ProbeSlowFeed", out v)) ProbeSlowFeed = v.GetDouble();
                if (root.TryGetProperty("ProbeTraverseFr", out v)) ProbeTraverseFr = v.GetDouble();
                if (root.TryGetProperty("ProbeFastFeed", out v)) ProbeFastFeed = v.GetDouble();
                if (root.TryGetProperty("LatchDistance", out v)) LatchDistance = v.GetDouble();
                if (root.TryGetProperty("ExtraProbeDepth", out v)) ExtraProbeDepth = v.GetDouble();
                if (root.TryGetProperty("MaxXyDistance", out v)) MaxXyDistance = v.GetDouble();
                if (root.TryGetProperty("MaxZDistance", out v)) MaxZDistance = v.GetDouble();
                if (root.TryGetProperty("XyClearance", out v)) XyClearance = v.GetDouble();
                if (root.TryGetProperty("ZClearance", out v)) ZClearance = v.GetDouble();
                if (root.TryGetProperty("StepOffWidth", out v)) StepOffWidth = v.GetDouble();
                if (root.TryGetProperty("BossPocketDiam", out v)) BossPocketDiam = v.GetDouble();
                if (root.TryGetProperty("BossPocketOffsetX", out v)) BossPocketOffsetX = v.GetDouble();
                if (root.TryGetProperty("BossPocketOffsetY", out v)) BossPocketOffsetY = v.GetDouble();
                if (root.TryGetProperty("RidgeValleyOffsetX", out v)) RidgeValleyOffsetX = v.GetDouble();
                if (root.TryGetProperty("RidgeValleyOffsetY", out v)) RidgeValleyOffsetY = v.GetDouble();
                // TOOL SETTER
                if (root.TryGetProperty("TsSpindleZero", out v)) TsSpindleZero = v.GetDouble();
                if (root.TryGetProperty("TsToolSetterX", out v)) TsToolSetterX = v.GetDouble();
                if (root.TryGetProperty("TsToolSetterY", out v)) TsToolSetterY = v.GetDouble();
                if (root.TryGetProperty("TsToolSetterZ", out v)) TsToolSetterZ = v.GetDouble();
                if (root.TryGetProperty("TsToolDiamProbe", out v)) TsToolDiamProbe = v.GetDouble();
                if (root.TryGetProperty("TsToolDiamOffset", out v)) TsToolDiamOffset = v.GetDouble();
                if (root.TryGetProperty("TsToolOffsetDirection", out v)) TsToolOffsetDirection = v.GetInt32();
                if (root.TryGetProperty("TsFastProbeFr", out v)) TsFastProbeFr = v.GetDouble();
                if (root.TryGetProperty("TsSlowProbeFr", out v)) TsSlowProbeFr = v.GetDouble();
                if (root.TryGetProperty("TsTraverseFr", out v)) TsTraverseFr = v.GetDouble();
                if (root.TryGetProperty("TsZMaxTravel", out v)) TsZMaxTravel = v.GetDouble();
                if (root.TryGetProperty("TsXyMaxTravel", out v)) TsXyMaxTravel = v.GetDouble();
                if (root.TryGetProperty("TsRetractDist", out v)) TsRetractDist = v.GetDouble();
                if (root.TryGetProperty("TsBreakageTolerance", out v)) TsBreakageTolerance = v.GetDouble();
                if (root.TryGetProperty("TsUserParam1", out v)) TsUserParam1 = v.GetDouble();
                if (root.TryGetProperty("TsUserParam2", out v)) TsUserParam2 = v.GetDouble();

                AlarmService.Instance.AddLog("INFO", "探測參數已從 probe_settings.json 載入");
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("WARN", $"載入探測參數失敗: {ex.Message}");
            }
        }

        // [2026-03-04] 將探測結果寫入選定的 WCS（G10 L20）
        private async Task WriteProbeResultToWcs(ProbeResult result)
        {
            int wcsIndex = SelectedWcs switch
            {
                "G54" => 1,
                "G55" => 2,
                "G56" => 3,
                "G57" => 4,
                "G58" => 5,
                "G59" => 6,
                "G59.1" => 7,
                "G59.2" => 8,
                "G59.3" => 9,
                _ => 1
            };
            string cmd = $"G10 L20 P{wcsIndex} X{result.X:F4} Y{result.Y:F4} Z{result.Z:F4}";
            bool ok = await MachineControlService.Instance.SendMdiCommandAsync(cmd);
            if (ok)
            {
                ProbeStatusText += $" → 已寫入 {SelectedWcs}";
                AlarmService.Instance.AddLog("INFO", $"Probe → {SelectedWcs}: {cmd}");
            }
            else
            {
                ProbeStatusText += $" → 寫入 {SelectedWcs} 失敗";
                AlarmService.Instance.AddLog("ERROR", $"Probe WCS write failed: {cmd}");
            }
        }

        // =====================================================================
        // 其他 Commands
        // =====================================================================

        [RelayCommand]
        private void SwitchProbeTab(string tab)
        {
            SelectedProbeTab = tab;
        }

        [RelayCommand]
        private void SwitchProbeMode(string mode)
        {
            SelectedProbeMode = mode;
        }

        [RelayCommand]
        private void SelectWcs(string wcs)
        {
            SelectedWcs = wcs;
        }

        [RelayCommand]
        private void UpdateProbeParams()
        {
            AlarmService.Instance.AddLog("INFO",
                $"Probe Params: Tool#{ProbeToolNumber} SlowF={ProbeSlowFeed} " +
                $"TravF={ProbeTraverseFr} FastF={ProbeFastFeed} " +
                $"Latch={LatchDistance} Depth={ExtraProbeDepth}");
            SaveProbeSettings();
            ProbeStatusText = "參數已儲存";
        }

        [RelayCommand]
        private void ResetResults()
        {
            ResultX = 0; ResultY = 0; ResultZ = 0;
            ResultXOffset = 0; ResultYOffset = 0;
            ResultEdgeWidth = 0;
            ResultWidthX = 0; ResultWidthY = 0;
            ResultAngle = 0; ResultEdgeDelta = 0;
            ResultXCenter = 0; ResultYCenter = 0;
            ProbeStatusText = "結果已重置";
        }

        // [2026-03-04] 對齊 PB 版：RESET ALL DATA / X DATA RESET / Y DATA RESET
        [RelayCommand]
        private void ResetAllData()
        {
            ResetResults();
            ProbeStatusText = "所有數據已重置";
        }

        [RelayCommand]
        private void XDataReset()
        {
            ResultX = 0; ResultXOffset = 0; ResultWidthX = 0; ResultXCenter = 0;
            ProbeStatusText = "X 軸數據已清零";
        }

        [RelayCommand]
        private void YDataReset()
        {
            ResultY = 0; ResultYOffset = 0; ResultWidthY = 0; ResultYCenter = 0;
            ProbeStatusText = "Y 軸數據已清零";
        }

        [RelayCommand]
        private async Task SendMdi()
        {
            if (string.IsNullOrWhiteSpace(MdiInput)) return;
            AlarmService.Instance.AddLog("INFO", $"Probing MDI: {MdiInput}");
            await MachineControlService.Instance.SendMdiCommandAsync(MdiInput.Trim());
            MdiInput = "";
        }
    }
}
