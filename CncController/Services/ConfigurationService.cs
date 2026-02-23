using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using CncController.Models;

namespace CncController.Services
{
    public class ConfigurationService
    {
        public static ConfigurationService Instance { get; } = new ConfigurationService();

        private readonly HttpClient _http;
        private const string ConfigFileName = "MachineConfig.json";
        private readonly JsonSerializerOptions _jsonOptions;

        private ConfigurationService()
        {
            // 請根據實際後端 IP 修改
            _http = new HttpClient { BaseAddress = new Uri("http://192.168.0.137:5000") };
            _jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        }

        public async Task<MachineConfig> LoadConfigAsync()
        {
            try
            {
                if (!File.Exists(ConfigFileName)) return new MachineConfig();
                string json = await File.ReadAllTextAsync(ConfigFileName);
                return JsonSerializer.Deserialize<MachineConfig>(json, _jsonOptions) ?? new MachineConfig();
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Config load failed: {ex.GetType().Name}: {ex.Message}");
                return new MachineConfig();
            }
        }

        public async Task SaveConfigAsync(MachineConfig config)
        {
            try
            {
                // 1. 儲存 JSON 設定檔
                string json = JsonSerializer.Serialize(config, _jsonOptions);
                await File.WriteAllTextAsync(ConfigFileName, json);

                // 2. 生成 LinuxCNC 所需檔案
                string iniContent = GenerateIni(config);
                string xmlContent = GenerateXml(config);
                string halContent = GenerateHal(config);
                string postGuiContent = GeneratePostGuiHal(config);

                var payload = new
                {
                    IniContent = iniContent,
                    HalContent = halContent,
                    XmlContent = xmlContent,
                    PostGuiContent = postGuiContent
                };

                // 3. 傳送至後端並重啟
                await _http.PostAsJsonAsync("/api/config/update", payload);
                await _http.PostAsync("/api/machine/restart", null);
            }
            catch (Exception ex)
            {
                // [安全] 改為寫入 AlarmService（跑馬燈可見），並重新拋出讓呼叫端知道部署失敗
                AlarmService.Instance.AddLog("ERR", $"SaveConfig failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        // ========================================================================================
        // 1. 生成 INI
        // ========================================================================================
        private string GenerateIni(MachineConfig config)
        {
            var sb = new StringBuilder();
            int axesCount = config.Axes.Count;
            string coordinates = string.Join(" ", config.Axes.Select(a => a.AxisID));
            string geometry = string.Join("", config.Axes.Select(a => a.AxisID));

            sb.AppendLine("[EMC]");
            sb.AppendLine("VERSION = 1.1");
            sb.AppendLine("MACHINE = SGCAM_PB_ATC");
            sb.AppendLine("DEBUG = 0");
            sb.AppendLine();

            sb.AppendLine("[DISPLAY]");
            sb.AppendLine("DISPLAY = probe_basic");
            sb.AppendLine("OPEN_FILE = ~/linuxcnc/nc_files/probe_basic/examples/blank.ngc");
            sb.AppendLine("CONFIG_FILE = custom_config.yml");
            sb.AppendLine("CYCLE_TIME = 0.200");
            sb.AppendLine("POSITION_OFFSET = RELATIVE");
            sb.AppendLine("POSITION_FEEDBACK = ACTUAL");
            sb.AppendLine("MAX_FEED_OVERRIDE = 2.0");
            sb.AppendLine("MAX_SPINDLE_OVERRIDE = 2.0");
            sb.AppendLine("MIN_SPINDLE_OVERRIDE = 0.5");
            sb.AppendLine("DEFAULT_SPINDLE_SPEED = 300");
            sb.AppendLine("PROGRAM_PREFIX = ~/linuxcnc/nc_files");
            sb.AppendLine("INTRO_GRAPHIC = pbsplash.png");
            sb.AppendLine("INTRO_TIME = 3");
            sb.AppendLine("EDITOR = gedit");
            sb.AppendLine("INCREMENTS = JOG 0.1 0.01 0.001");
            sb.AppendLine($"GEOMETRY = {geometry}");
            sb.AppendLine($"DRO_DISPLAY = {geometry}");
            sb.AppendLine("ATC_TAB_DISPLAY = 2");
            sb.AppendLine("USER_BUTTONS_PATH = user_buttons/");
            sb.AppendLine("USER_ATC_BUTTONS_PATH = user_atc_buttons/");
            sb.AppendLine("USER_DROS_PATH = user_dro_display/");
            sb.AppendLine();

            sb.AppendLine("[FILTER]");
            sb.AppendLine("PROGRAM_EXTENSION = .nc,.txt,.tap Other NC files");
            sb.AppendLine("png = image-to-gcode");
            sb.AppendLine();

            sb.AppendLine("[PYTHON]");
            sb.AppendLine("TOPLEVEL = ./python/toplevel.py");
            sb.AppendLine("PATH_APPEND = ./python/");
            sb.AppendLine();

            sb.AppendLine("[ATC]");
            sb.AppendLine("POCKETS = 12");
            sb.AppendLine();

            sb.AppendLine("[RS274NGC]");
            sb.AppendLine("RS274NGC_STARTUP_CODE = F10 S300 G21 G17 G40 G49 G54 G64 P0.001 G80 G90 G91.1 G92.1 G94 G97 G98");
            sb.AppendLine("PARAMETER_FILE = vmc_metric.var");
            sb.AppendLine("SUBROUTINE_PATH = macros_metric_sim");
            sb.AppendLine("REMAP=M6  modalgroup=6 prolog=change_prolog ngc=toolchange epilog=change_epilog");
            sb.AppendLine("REMAP=M10 modalgroup=6 argspec=P ngc=m10");
            sb.AppendLine("REMAP=M11 modalgroup=6 argspec=p ngc=m11");
            sb.AppendLine("REMAP=M12 modalgroup=6 argspec=p ngc=m12");
            sb.AppendLine("REMAP=M13 modalgroup=6 ngc=m13");
            sb.AppendLine("REMAP=M21 modalgroup=6 ngc=m21");
            sb.AppendLine("REMAP=M22 modalgroup=6 ngc=m22");
            sb.AppendLine("REMAP=M23 modalgroup=6 ngc=m23");
            sb.AppendLine("REMAP=M24 modalgroup=6 ngc=m24");
            sb.AppendLine("REMAP=M25 modalgroup=6 ngc=m25");
            sb.AppendLine("REMAP=M26 modalgroup=6 ngc=m26");
            sb.AppendLine();

            sb.AppendLine("[EMCMOT]");
            sb.AppendLine("EMCMOT = motmod");
            sb.AppendLine("COMM_TIMEOUT = 1.0");
            sb.AppendLine("BASE_PERIOD = 100000");
            sb.AppendLine("SERVO_PERIOD = 1000000");
            sb.AppendLine();

            sb.AppendLine("[TASK]");
            sb.AppendLine("TASK = milltask");
            sb.AppendLine("CYCLE_TIME = 0.010");
            sb.AppendLine();

            sb.AppendLine("[HAL]");
            sb.AppendLine("HALUI = halui");
            sb.AppendLine("HALFILE = 3axis.hal");
            sb.AppendLine("POSTGUI_HALFILE = probe_basic_postgui.hal");
            sb.AppendLine();

            sb.AppendLine("[TRAJ]");
            sb.AppendLine($"AXES = {axesCount}");
            sb.AppendLine("SPINDLES = 1");
            sb.AppendLine($"COORDINATES = {coordinates}");
            sb.AppendLine("LINEAR_UNITS = mm");
            sb.AppendLine("ANGULAR_UNITS = degree");
            sb.AppendLine("DEFAULT_LINEAR_VELOCITY = 50");
            sb.AppendLine("MAX_LINEAR_VELOCITY = 125");
            sb.AppendLine();

            sb.AppendLine("[EMCIO]");
            sb.AppendLine("EMCIO = io");
            sb.AppendLine("CYCLE_TIME = 0.100");
            sb.AppendLine("TOOL_TABLE = tool_metric.tbl");
            sb.AppendLine("RANDOM_TOOLCHANGER = 0");
            sb.AppendLine();

            sb.AppendLine("[KINS]");
            sb.AppendLine($"JOINTS = {axesCount}");
            sb.AppendLine($"KINEMATICS = trivkins coordinates={geometry}");
            sb.AppendLine();

            sb.AppendLine("[SPINDLE]");
            sb.AppendLine("PGAIN_V = 0");
            sb.AppendLine("IGAIN_V = 0.01");
            sb.AppendLine("DGAIN_V = 0");
            sb.AppendLine("FF0_V = 1");
            sb.AppendLine("FF1_V = 0");
            sb.AppendLine("PGAIN_P = 100");
            sb.AppendLine("IGAIN_P = 1");
            sb.AppendLine("DGAIN_P = 0");
            sb.AppendLine("FF0_P = 0");
            sb.AppendLine("FF1_P = 1");
            sb.AppendLine();

            for (int i = 0; i < axesCount; i++)
            {
                var axis = config.Axes[i];
                string axisName = axis.AxisID.ToUpper();
                double pitch = axis.Pitch == 0 ? 5.0 : axis.Pitch;
                double scale = axis.PulsePerRev / pitch;

                sb.AppendLine($"# --- Axis {axisName} ---");
                sb.AppendLine($"[AXIS_{axisName}]");
                sb.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                sb.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                sb.AppendLine($"MAX_VELOCITY = {axis.MaxVelocity}");
                sb.AppendLine($"MAX_ACCELERATION = {axis.MaxAcceleration}");
                sb.AppendLine();

                sb.AppendLine($"[JOINT_{i}]");
                sb.AppendLine("TYPE = LINEAR");
                sb.AppendLine("HOME = 0.0");
                sb.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                sb.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                sb.AppendLine($"MAX_VELOCITY = {axis.MaxVelocity}");
                sb.AppendLine($"MAX_ACCELERATION = {axis.MaxAcceleration}");
                sb.AppendLine($"STEP_SCALE = {scale:0.0}");
                sb.AppendLine("FERROR = 50.0");
                sb.AppendLine("MIN_FERROR = 5.0");

                sb.AppendLine("P_GAIN = 1000");
                sb.AppendLine("I_GAIN = 0");
                sb.AppendLine("D_GAIN = 0");
                sb.AppendLine("FF0 = 0");
                sb.AppendLine("FF1 = 1.0");

                double searchVel = 0;
                double latchVel = 0;
                string useIndex = "NO";
                string ignoreLimits = "NO";

                switch (axis.HomingMode)
                {
                    case HomingMode.Immediate:
                        searchVel = 0; latchVel = 0; useIndex = "NO";
                        break;
                    case HomingMode.HomeSwitch:
                        searchVel = Math.Abs(axis.HomeSpeed) * axis.HomeDirection;
                        latchVel = Math.Abs(axis.HomeLatchSpeed) * axis.HomeDirection;
                        useIndex = axis.HomeUseIndex ? "YES" : "NO";
                        ignoreLimits = "YES";
                        break;
                    case HomingMode.LimitSwitch:
                        searchVel = Math.Abs(axis.HomeSpeed) * axis.HomeDirection;
                        latchVel = Math.Abs(axis.HomeLatchSpeed) * axis.HomeDirection;
                        useIndex = axis.HomeUseIndex ? "YES" : "NO";
                        ignoreLimits = "YES";
                        break;
                }

                sb.AppendLine($"HOME_OFFSET = {axis.HomeOffset}");
                sb.AppendLine($"HOME_SEARCH_VEL = {searchVel}");
                sb.AppendLine($"HOME_LATCH_VEL = {latchVel}");
                sb.AppendLine($"HOME_USE_INDEX = {useIndex}");
                sb.AppendLine($"HOME_IGNORE_LIMITS = {ignoreLimits}");
                sb.AppendLine($"HOME_SEQUENCE = {axis.HomeSequence}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ========================================================================================
        // 2. 生成 HAL (整合 IO Mappings 與標準訊號)
        // ========================================================================================
        private string GenerateHal(MachineConfig config)
        {
            var sb = new StringBuilder();
            int axesCount = config.Axes.Count;

            sb.AppendLine("# Generated by CncController (Smart Feedback + IO Support)");
            sb.AppendLine("loadrt [KINS]KINEMATICS");
            sb.AppendLine("loadrt [EMCMOT]EMCMOT servo_period_nsec=[EMCMOT]SERVO_PERIOD num_joints=[KINS]JOINTS");
            sb.AppendLine($"loadrt cia402 count={axesCount}");

            var sliceNames = string.Join(",", config.Axes.Select(a => $"slice_{a.AxisID}"));
            var personalities = string.Join(",", Enumerable.Repeat("32", axesCount));
            sb.AppendLine($"loadrt bitslice names={sliceNames} personality={personalities}");

            // 計算 NOT 元件總需求
            int axisNotCount = axesCount * 3;
            int ioInvertCount = config.Mappings
                .Where(m => m.Type == MapType.Input || m.Type == MapType.Output)
                .SelectMany(m => m.Pins)
                .Count(p => p.IsInverted);

            int totalNotCount = axisNotCount + ioInvertCount;

            if (totalNotCount > 0) sb.AppendLine($"loadrt not count={totalNotCount}");

            sb.AppendLine("loadusr -W lcec_conf ethercat-conf.xml");
            sb.AppendLine("loadrt lcec");
            sb.AppendLine();

            sb.AppendLine("addf lcec.read-all servo-thread");
            foreach (var axis in config.Axes) sb.AppendLine($"addf slice_{axis.AxisID} servo-thread");
            for (int i = 0; i < axesCount; i++) sb.AppendLine($"addf cia402.{i}.read-all servo-thread");

            if (totalNotCount > 0)
            {
                for (int k = 0; k < totalNotCount; k++) sb.AppendLine($"addf not.{k} servo-thread");
            }

            sb.AppendLine("addf motion-command-handler servo-thread");
            sb.AppendLine("addf motion-controller servo-thread");
            for (int i = 0; i < axesCount; i++) sb.AppendLine($"addf cia402.{i}.write-all servo-thread");
            sb.AppendLine("addf lcec.write-all servo-thread");
            sb.AppendLine();

            int currentNotIndex = 0;

            // =========================================================
            // A. 軸與馬達邏輯 (AXIS MAPPING)
            // =========================================================
            for (int i = 0; i < axesCount; i++)
            {
                var axis = config.Axes[i];
                string axisHalName = $"joint.{i}";

                int sIdx = -1;
                string axName = axis.AxisID.ToUpper();
                if (axName == "X") sIdx = 0; else if (axName == "Y") sIdx = 2; else if (axName == "Z") sIdx = 3;
                if (sIdx == -1) continue;

                string sliceName = $"slice_{axis.AxisID}";

                bool isServo = false;
                var map = config.Mappings.FirstOrDefault(m => m.PhysicalIndex == sIdx);
                if (map != null)
                {
                    isServo = IsServoDrive(map.ExpectedVendorId, map.ExpectedProductCode);
                }

                // 檢查是否為 5621 (脈波產生器)
                string pidClean = map?.ExpectedProductCode?.ToLower().Replace("0x", "") ?? "";
                bool isPulseGen = pidClean.Contains("5621");

                sb.AppendLine($"# --- Axis {axis.AxisID} (Slave {sIdx}) [Type: {(isServo ? "Servo" : "Stepper/OpenLoop")}] ---");

                sb.AppendLine($"net {axis.AxisID}-enable  {axisHalName}.amp-enable-out => cia402.{i}.enable");
                sb.AppendLine($"net {axis.AxisID}-control cia402.{i}.controlword => lcec.0.{sIdx}.control_word_J{sIdx}");
                sb.AppendLine($"net {axis.AxisID}-status  lcec.0.{sIdx}.status_word_J{sIdx} => cia402.{i}.statusword");

                sb.AppendLine($"setp cia402.{i}.csp-mode 1");

                // ★★★ [修正] 只有「不是」5621 才設定 Operation Mode ★★★
                if (isServo && !isPulseGen)
                {
                    sb.AppendLine($"setp lcec.0.{sIdx}.modes_of_operation_J{sIdx} 8");
                }

                sb.AppendLine($"setp cia402.{i}.pos-scale [JOINT_{i}]STEP_SCALE");

                sb.AppendLine($"net {axis.AxisID}-pos-cmd {axisHalName}.motor-pos-cmd => cia402.{i}.pos-cmd");
                sb.AppendLine($"net {axis.AxisID}-drv-target cia402.{i}.drv-target-position => lcec.0.{sIdx}.target_position_J{sIdx}");

                sb.AppendLine($"net {axis.AxisID}-pos-fb lcec.0.{sIdx}.position_actual_value_J{sIdx} => cia402.{i}.drv-actual-position");

                if (isServo)
                {
                    sb.AppendLine("# [Feedback] Real Encoder Feedback (Closed Loop)");
                    sb.AppendLine($"net {axis.AxisID}-pos-fb-final cia402.{i}.pos-fb => {axisHalName}.motor-pos-fb");
                }
                else
                {
                    sb.AppendLine("# [Feedback] Fake Feedback (Open Loop)");
                    sb.AppendLine($"net {axis.AxisID}-pos-cmd => {axisHalName}.motor-pos-fb");
                }

                sb.AppendLine($"net {axis.AxisID}-di-raw lcec.0.{sIdx}.digital_inputs_J{sIdx} => {sliceName}.in");

                // --- Homing & Limits Logic ---
                if (axis.HomingMode == HomingMode.Immediate)
                {
                    sb.AppendLine("# Homing: Immediate");
                }
                else if (axis.HomingMode == HomingMode.HomeSwitch)
                {
                    if (axis.HomeSwitchLogic != LimitLogic.NotUsed)
                    {
                        string rawHomeSig = $"{sliceName}.out-{axis.HomeDiIndex:00}";
                        if (axis.HomeSwitchLogic == LimitLogic.NC)
                        {
                            sb.AppendLine($"net {axis.AxisID}-home-raw {rawHomeSig} => not.{currentNotIndex}.in");
                            sb.AppendLine($"net {axis.AxisID}-home {axisHalName}.home-sw-in <= not.{currentNotIndex}.out");
                            currentNotIndex++;
                        }
                        else
                        {
                            sb.AppendLine($"net {axis.AxisID}-home {axisHalName}.home-sw-in <= {rawHomeSig}");
                        }
                    }
                }
                else if (axis.HomingMode == HomingMode.LimitSwitch)
                {
                    int targetDiIndex = (axis.HomeDirection > 0) ? axis.PosLimitDiIndex : axis.NegLimitDiIndex;
                    if (axis.LimitSwitchLogic != LimitLogic.NotUsed)
                    {
                        string rawSharedSig = $"{sliceName}.out-{targetDiIndex:00}";
                        if (axis.LimitSwitchLogic == LimitLogic.NC)
                        {
                            sb.AppendLine($"net {axis.AxisID}-home-shared-raw {rawSharedSig} => not.{currentNotIndex}.in");
                            sb.AppendLine($"net {axis.AxisID}-home {axisHalName}.home-sw-in <= not.{currentNotIndex}.out");
                            currentNotIndex++;
                        }
                        else
                        {
                            sb.AppendLine($"net {axis.AxisID}-home {axisHalName}.home-sw-in <= {rawSharedSig}");
                        }
                    }
                }

                if (axis.LimitSwitchLogic != LimitLogic.NotUsed)
                {
                    string rawNeg = $"{sliceName}.out-{axis.NegLimitDiIndex:00}";
                    if (axis.LimitSwitchLogic == LimitLogic.NC)
                    {
                        sb.AppendLine($"net {axis.AxisID}-neg-raw {rawNeg} => not.{currentNotIndex}.in");
                        sb.AppendLine($"net {axis.AxisID}-neg-lim {axisHalName}.neg-lim-sw-in <= not.{currentNotIndex}.out");
                        currentNotIndex++;
                    }
                    else
                    {
                        sb.AppendLine($"net {axis.AxisID}-neg-lim {axisHalName}.neg-lim-sw-in <= {rawNeg}");
                    }

                    string rawPos = $"{sliceName}.out-{axis.PosLimitDiIndex:00}";
                    if (axis.LimitSwitchLogic == LimitLogic.NC)
                    {
                        sb.AppendLine($"net {axis.AxisID}-pos-raw {rawPos} => not.{currentNotIndex}.in");
                        sb.AppendLine($"net {axis.AxisID}-pos-lim {axisHalName}.pos-lim-sw-in <= not.{currentNotIndex}.out");
                        currentNotIndex++;
                    }
                    else
                    {
                        sb.AppendLine($"net {axis.AxisID}-pos-lim {axisHalName}.pos-lim-sw-in <= {rawPos}");
                    }
                }
                sb.AppendLine();
            }

            // =========================================================
            // ★★★ B. 標準系統訊號來源 (Standard Signals) ★★★
            // =========================================================
            sb.AppendLine("# === STANDARD SIGNALS (Source) ===");
            sb.AppendLine("net coolant-flood iocontrol.0.coolant-flood");
            sb.AppendLine("net coolant-mist  iocontrol.0.coolant-mist");
            sb.AppendLine("net spindle-on    spindle.0.on");
            sb.AppendLine("net spindle-cw    spindle.0.forward");
            sb.AppendLine("net spindle-ccw   spindle.0.reverse");
            sb.AppendLine("net spindle-brake spindle.0.brake");
            sb.AppendLine();

            // =========================================================
            // C. 通用 IO 邏輯 (IN/OUT MAPPINGS)
            // =========================================================
            sb.AppendLine("# === GENERAL IO MAPPINGS ===");

            // 處理 INPUT
            foreach (var map in config.Mappings.Where(m => m.Type == MapType.Input))
            {
                string devicePrefix = $"lcec.0.{map.PhysicalIndex}";
                sb.AppendLine($"# Input Group {map.ChannelIndex} (Slave {map.PhysicalIndex})");

                foreach (var pin in map.Pins)
                {
                    string hwPin = $"{devicePrefix}.din-{pin.Index:00}";
                    string signalName = string.IsNullOrWhiteSpace(pin.Function)
                        ? $"input-G{map.ChannelIndex}-{pin.Index:00}"
                        : CleanSignalName(pin.Function);

                    if (pin.IsInverted)
                    {
                        sb.AppendLine($"net raw-{signalName} {hwPin} => not.{currentNotIndex}.in");
                        sb.AppendLine($"net {signalName} not.{currentNotIndex}.out");
                        currentNotIndex++;
                    }
                    else
                    {
                        sb.AppendLine($"net {signalName} {hwPin}");
                    }
                }
                sb.AppendLine();
            }

            // 處理 OUTPUT
            foreach (var map in config.Mappings.Where(m => m.Type == MapType.Output))
            {
                string devicePrefix = $"lcec.0.{map.PhysicalIndex}";
                sb.AppendLine($"# Output Group {map.ChannelIndex} (Slave {map.PhysicalIndex})");

                foreach (var pin in map.Pins)
                {
                    string hwPin = $"{devicePrefix}.dout-{pin.Index:00}";
                    string signalName = string.IsNullOrWhiteSpace(pin.Function)
                        ? $"output-G{map.ChannelIndex}-{pin.Index:00}"
                        : CleanSignalName(pin.Function);

                    if (pin.IsInverted)
                    {
                        sb.AppendLine($"net {signalName} => not.{currentNotIndex}.in");
                        sb.AppendLine($"net inv-{signalName} not.{currentNotIndex}.out => {hwPin}");
                        currentNotIndex++;
                    }
                    else
                    {
                        sb.AppendLine($"net {signalName} => {hwPin}");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("# --- SAFETY BYPASS (FORCE CONNECTION) ---");
            sb.AppendLine("unlinkp iocontrol.0.emc-enable-in");
            sb.AppendLine("net logic-enable iocontrol.0.user-enable-out => iocontrol.0.emc-enable-in");

            return sb.ToString();
        }

        // ========================================================================================
        // 3. 生成 XML (EtherCAT Topology - 修正混合模組與脈波模組)
        // ========================================================================================
        private string GenerateXml(MachineConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<masters>");
            sb.AppendLine($"  <master idx=\"0\" appTimePeriod=\"1000000\" refClockSyncCycles=\"1000\">");

            int maxSlaveIndex = 0;
            if (config.Mappings.Any())
                maxSlaveIndex = config.Mappings.Max(m => m.PhysicalIndex);
            if (maxSlaveIndex < 1) maxSlaveIndex = 1;

            for (int i = 0; i <= maxSlaveIndex; i++)
            {
                // 1. 找出這個 Slave Index 所有相關的設定 (可能同時有 IN 和 OUT)
                var slaveMappings = config.Mappings.Where(m => m.PhysicalIndex == i).ToList();

                // 2. 判斷基本資訊 (取第一筆有資料的)
                var mainMap = slaveMappings.FirstOrDefault();

                if (mainMap != null)
                {
                    bool isServo = IsServoDrive(mainMap.ExpectedVendorId, mainMap.ExpectedProductCode);

                    // ★★★ 關鍵判斷：是否為 Delta 902 (混合 IO 模組) ★★★
                    string pCode = mainMap.ExpectedProductCode?.ToLower().Replace("0x", "") ?? "";
                    bool isDeltaHybrid = pCode.Contains("902");

                    // 判斷是否需要生成 Input 或 Output 區塊
                    bool hasInput = isDeltaHybrid || slaveMappings.Any(m => m.Type == MapType.Input);
                    bool hasOutput = isDeltaHybrid || slaveMappings.Any(m => m.Type == MapType.Output);

                    sb.AppendLine($"    <slave idx=\"{i}\" type=\"generic\" vid=\"{mainMap.ExpectedVendorId}\" pid=\"{mainMap.ExpectedProductCode}\" configPdos=\"true\">");

                    // ★★★ 修正 1: 解決 Sync Error 問題 ★★★
                    // 如果是 IO 模組 (902)，強制使用 Free Run (0x0)
                    if (isDeltaHybrid)
                    {
                        sb.AppendLine("      <dcConf assignActivate=\"0x0\" sync0Cycle=\"*1\" sync0Shift=\"0\"/>");
                    }
                    else
                    {
                        // 伺服馬達使用 DC Sync (0x300)
                        sb.AppendLine("      <dcConf assignActivate=\"0x300\" sync0Cycle=\"*1\" sync0Shift=\"0\"/>");
                    }

                    if (isServo)
                    {
                        // 針對 5621 (脈波產生器) 移除部分 PDO
                        bool isPulseGen = pCode.Contains("5621");

                        // Servo Drive PDOs
                        sb.AppendLine("      <syncManager idx=\"2\" dir=\"out\">");
                        sb.AppendLine("        <pdo idx=\"1600\">");
                        sb.AppendLine($"          <pdoEntry idx=\"6040\" subIdx=\"00\" bitLen=\"16\" halPin=\"control_word_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"607a\" subIdx=\"00\" bitLen=\"32\" halPin=\"target_position_J{i}\" halType=\"s32\"/>");

                        // 只有「不是」5621 才生成 6060
                        if (!isPulseGen)
                        {
                            sb.AppendLine($"          <pdoEntry idx=\"6060\" subIdx=\"00\" bitLen=\"8\" halPin=\"modes_of_operation_J{i}\" halType=\"s32\"/>");
                        }

                        sb.AppendLine("        </pdo>");
                        sb.AppendLine("      </syncManager>");

                        sb.AppendLine("      <syncManager idx=\"3\" dir=\"in\">");
                        sb.AppendLine("        <pdo idx=\"1a00\">");
                        sb.AppendLine($"          <pdoEntry idx=\"6041\" subIdx=\"00\" bitLen=\"16\" halPin=\"status_word_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"6064\" subIdx=\"00\" bitLen=\"32\" halPin=\"position_actual_value_J{i}\" halType=\"s32\"/>");

                        // 只有「不是」5621 才生成 6061
                        if (!isPulseGen)
                        {
                            sb.AppendLine($"          <pdoEntry idx=\"6061\" subIdx=\"00\" bitLen=\"8\" halPin=\"modes_of_operation_display_J{i}\" halType=\"s32\"/>");
                        }

                        sb.AppendLine($"          <pdoEntry idx=\"60fd\" subIdx=\"00\" bitLen=\"32\" halPin=\"digital_inputs_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine("        </pdo>");
                        sb.AppendLine("      </syncManager>");
                    }
                    else
                    {
                        // =========================================================
                        //  針對 Delta R2-EC0902 (混合模組) 與 一般 IO 的生成邏輯
                        //  版本：Bit 模式 (32點), 無 SDO, Free Run
                        // =========================================================

                        // ★★★ [修正 1] SDO 設定：全部清空！ ★★★
                        // 因為 Log 顯示開機時寫入會失敗，我們不在這裡生成 <sdoConfig>。
                        // 請務必使用外部腳本 (unlock_io.sh) 來執行 ethercat download 解鎖 Output。

                        // ★★★ [修正 2] Output 設定：SyncManager 2 ★★★
                        if (hasOutput || isDeltaHybrid)
                        {
                            sb.AppendLine("      <syncManager idx=\"2\" dir=\"out\">");
                            sb.AppendLine("        <pdo idx=\"1600\">");

                            // 不論是 Delta 902 還是普通模組，我們都嘗試用 Bit 模式
                            // 生成 32 個 Output Entry (dout-00 ~ dout-31)
                            for (int k = 0; k < 32; k++)
                            {
                                // idx="0x7000" 是 Generic 驅動標準的 Output 位址
                                // subIdx 從 0x01 開始到 0x20 (32個)
                                // halPin 名稱維持 dout-00 格式
                                sb.AppendLine($"          <pdoEntry idx=\"0x7000\" subIdx=\"0x{k + 1:X2}\" bitLen=\"1\" halPin=\"dout-{k:00}\" halType=\"bit\"/>");
                            }

                            sb.AppendLine("        </pdo>");
                            sb.AppendLine("      </syncManager>");
                        }

                        // ★★★ [修正 3] Input 設定：SyncManager 3 ★★★
                        if (hasInput || isDeltaHybrid)
                        {
                            sb.AppendLine("      <syncManager idx=\"3\" dir=\"in\">");
                            sb.AppendLine("        <pdo idx=\"1a00\">");

                            // 生成 32 個 Input Entry (din-00 ~ din-31)
                            for (int k = 0; k < 32; k++)
                            {
                                // idx="0x6000" 是 Generic 驅動標準的 Input 位址
                                sb.AppendLine($"          <pdoEntry idx=\"0x6000\" subIdx=\"0x{k + 1:X2}\" bitLen=\"1\" halPin=\"din-{k:00}\" halType=\"bit\"/>");
                            }

                            sb.AppendLine("        </pdo>");
                            sb.AppendLine("      </syncManager>");
                        }
                    }
                    sb.AppendLine("    </slave>");
                }
                else
                {
                    // Empty Slave
                    sb.AppendLine($"    <slave idx=\"{i}\" type=\"generic\" vid=\"000001dd\" pid=\"00005500\" configPdos=\"false\"/>");
                }
            }

            sb.AppendLine("  </master>");
            sb.AppendLine("</masters>");
            return sb.ToString();
        }

        private string GeneratePostGuiHal(MachineConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Generated by CncController - PostGUI");
            sb.AppendLine("# 這裡只負責將 HAL 訊號連接到 Probe Basic 介面");
            sb.AppendLine("# ★★★ 絕對不要在這裡 loadrt not/bitslice，因為主 HAL 已經載入了 ★★★");
            sb.AppendLine();
            return sb.ToString();
        }

        private bool IsServoDrive(string vidStr, string pidStr)
        {
            if (string.IsNullOrEmpty(pidStr)) return false;

            string p = pidStr.ToLower().Trim().Replace("0x", "");
            string v = vidStr?.ToLower().Trim().Replace("0x", "") ?? "";

            // 1. 匯川
            if (p.Contains("c010d") || p.Contains("916")) return true;

            // 2. 台達
            if (v.Contains("1dd"))
            {
                if (p.Contains("902")) return false; // IO 模組
                return true; // 其他台達設備 (如 6080, 5621) 視為伺服
            }

            return false;
        }

        private string CleanSignalName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            char[] arr = name.ToCharArray();
            arr = Array.FindAll(arr, (c => (char.IsLetterOrDigit(c) || c == '_' || c == '-')));
            return new string(arr).Trim().Replace(" ", "-");
        }
    }
}