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
using System.Threading.Tasks;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class ProbingViewModel : ObservableObject
    {
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

                var result = await MachineControlService.Instance.RunProbeAsync(
                    probeType, direction, parameters);

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

                // [2026-03-04] 非僅顯示模式 → 自動寫入 WCS
                if (!IsProbePositionOnly)
                {
                    await WriteProbeResultToWcs(result);
                }
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
            ProbeStatusText = "參數已更新";
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
