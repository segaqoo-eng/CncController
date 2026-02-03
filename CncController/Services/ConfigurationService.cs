using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CncController.Models;
using System.Diagnostics;

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
            catch { return new MachineConfig(); }
        }

        public async Task SaveConfigAsync(MachineConfig config)
        {
            string json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(ConfigFileName, json);

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

            try
            {
                await _http.PostAsJsonAsync("/api/config/update", payload);
                await _http.PostAsync("/api/machine/restart", null);
            }
            catch { }
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
        // 2. 生成 HAL (★ 修正：使用 Fake Feedback 防止跟隨誤差)
        // ========================================================================================
        // ========================================================================================
        // 2. 生成 HAL (★ 修正：智慧判斷回授模式)
        //    - 伺服 (Servo): 使用真實 Encoder 回授 (需搭配 PID 調校)
        //    - 步進/開迴路: 使用偽造回授 (Fake Feedback)
        // ========================================================================================
        private string GenerateHal(MachineConfig config)
        {
            var sb = new StringBuilder();
            int axesCount = config.Axes.Count;

            sb.AppendLine("# Generated by CncController (Smart Feedback Mode)");
            sb.AppendLine("loadrt [KINS]KINEMATICS");
            sb.AppendLine("loadrt [EMCMOT]EMCMOT servo_period_nsec=[EMCMOT]SERVO_PERIOD num_joints=[KINS]JOINTS");
            sb.AppendLine($"loadrt cia402 count={axesCount}");

            var sliceNames = string.Join(",", config.Axes.Select(a => $"slice_{a.AxisID}"));
            var personalities = string.Join(",", Enumerable.Repeat("32", axesCount));
            sb.AppendLine($"loadrt bitslice names={sliceNames} personality={personalities}");

            int notCount = axesCount * 3;
            if (notCount > 0) sb.AppendLine($"loadrt not count={notCount}");

            sb.AppendLine("loadusr -W lcec_conf ethercat-conf.xml");
            sb.AppendLine("loadrt lcec");
            sb.AppendLine();

            sb.AppendLine("addf lcec.read-all servo-thread");
            foreach (var axis in config.Axes) sb.AppendLine($"addf slice_{axis.AxisID} servo-thread");
            for (int i = 0; i < axesCount; i++) sb.AppendLine($"addf cia402.{i}.read-all servo-thread");

            if (notCount > 0)
            {
                sb.AppendLine("addf not.0 servo-thread");
                for (int k = 1; k < notCount; k++) sb.AppendLine($"addf not.{k} servo-thread");
            }

            sb.AppendLine("addf motion-command-handler servo-thread");
            sb.AppendLine("addf motion-controller servo-thread");
            for (int i = 0; i < axesCount; i++) sb.AppendLine($"addf cia402.{i}.write-all servo-thread");
            sb.AppendLine("addf lcec.write-all servo-thread");
            sb.AppendLine();

            int currentNotIndex = 0;

            for (int i = 0; i < axesCount; i++)
            {
                var axis = config.Axes[i];
                string axisHalName = $"joint.{i}";

                // 1. 找出對應的 Slave Index
                int sIdx = -1;
                string axName = axis.AxisID.ToUpper();
                if (axName == "X") sIdx = 0; else if (axName == "Y") sIdx = 2; else if (axName == "Z") sIdx = 3;
                if (sIdx == -1) continue;

                string sliceName = $"slice_{axis.AxisID}";

                // 2. 判斷該 Slave 是否為伺服 (查 Mapping 表)
                bool isServo = false;
                var map = config.Mappings.FirstOrDefault(m => m.PhysicalIndex == sIdx);
                if (map != null)
                {
                    isServo = IsServoDrive(map.ExpectedVendorId, map.ExpectedProductCode);
                }

                sb.AppendLine($"# --- Axis {axis.AxisID} (Slave {sIdx}) [Type: {(isServo ? "Servo" : "Stepper/OpenLoop")}] ---");

                sb.AppendLine($"net {axis.AxisID}-enable  {axisHalName}.amp-enable-out => cia402.{i}.enable");
                sb.AppendLine($"net {axis.AxisID}-control cia402.{i}.controlword => lcec.0.{sIdx}.control_word_J{sIdx}");
                sb.AppendLine($"net {axis.AxisID}-status  lcec.0.{sIdx}.status_word_J{sIdx} => cia402.{i}.statusword");

                if (isServo)
                {
                    // 伺服需要 CSP 模式設定
                    sb.AppendLine($"setp cia402.{i}.csp-mode 1");
                    sb.AppendLine($"setp lcec.0.{sIdx}.modes_of_operation_J{sIdx} 8");
                }
                else
                {
                    // 步進模組可能不需要 CSP Mode (視模組而定，但設了通常無害)
                    sb.AppendLine($"setp cia402.{i}.csp-mode 1");
                }

                sb.AppendLine($"setp cia402.{i}.pos-scale [JOINT_{i}]STEP_SCALE");

                // 3. 命令輸出
                sb.AppendLine($"net {axis.AxisID}-pos-cmd {axisHalName}.motor-pos-cmd => cia402.{i}.pos-cmd");
                sb.AppendLine($"net {axis.AxisID}-drv-target cia402.{i}.drv-target-position => lcec.0.{sIdx}.target_position_J{sIdx}");

                // 4. 回授處理 (Feedback Logic)
                // 讀取硬體位置 (給 cia402 內部狀態機用)
                sb.AppendLine($"net {axis.AxisID}-pos-fb lcec.0.{sIdx}.position_actual_value_J{sIdx} => cia402.{i}.drv-actual-position");

                if (isServo)
                {
                    // ★★★ Case A: 伺服 (使用真實回授) ★★★
                    // 接上真實的 Encoder 回授
                    sb.AppendLine("# [Feedback] Real Encoder Feedback (Closed Loop)");
                    sb.AppendLine($"net {axis.AxisID}-pos-fb-final cia402.{i}.pos-fb => {axisHalName}.motor-pos-fb");
                }
                else
                {
                    // ★★★ Case B: 步進/開迴路 (使用偽造回授) ★★★
                    // 將命令直接接回回授，消除跟隨誤差
                    sb.AppendLine("# [Feedback] Fake Feedback (Open Loop)");
                    sb.AppendLine($"net {axis.AxisID}-pos-cmd => {axisHalName}.motor-pos-fb");
                }

                sb.AppendLine($"net {axis.AxisID}-di-raw lcec.0.{sIdx}.digital_inputs_J{sIdx} => {sliceName}.in");

                // --- Homing & Limits Logic (保持不變) ---
                if (axis.HomingMode == HomingMode.Immediate)
                {
                    sb.AppendLine("# Homing: Immediate (No switch)");
                }
                else if (axis.HomingMode == HomingMode.HomeSwitch)
                {
                    if (axis.HomeSwitchLogic != LimitLogic.NotUsed)
                    {
                        string rawHomeSig = $"{sliceName}.out-{axis.HomeDiIndex:00}";
                        if (axis.HomeSwitchLogic == LimitLogic.NC)
                        {
                            sb.AppendLine($"# Logic: NC (Inverted)");
                            sb.AppendLine($"net {axis.AxisID}-home-raw {rawHomeSig} => not.{currentNotIndex}.in");
                            sb.AppendLine($"net {axis.AxisID}-home {axisHalName}.home-sw-in <= not.{currentNotIndex}.out");
                            currentNotIndex++;
                        }
                        else
                        {
                            sb.AppendLine($"# Logic: NO (Direct)");
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
                            sb.AppendLine($"# Logic: NC (Shared Limit)");
                            sb.AppendLine($"net {axis.AxisID}-home-shared-raw {rawSharedSig} => not.{currentNotIndex}.in");
                            sb.AppendLine($"net {axis.AxisID}-home {axisHalName}.home-sw-in <= not.{currentNotIndex}.out");
                            currentNotIndex++;
                        }
                        else
                        {
                            sb.AppendLine($"# Logic: NO (Shared Limit)");
                            sb.AppendLine($"net {axis.AxisID}-home {axisHalName}.home-sw-in <= {rawSharedSig}");
                        }
                    }
                }

                if (axis.LimitSwitchLogic != LimitLogic.NotUsed)
                {
                    string rawNeg = $"{sliceName}.out-{axis.NegLimitDiIndex:00}";
                    if (axis.LimitSwitchLogic == LimitLogic.NC)
                    {
                        sb.AppendLine($"# Neg Limit: NC");
                        sb.AppendLine($"net {axis.AxisID}-neg-raw {rawNeg} => not.{currentNotIndex}.in");
                        sb.AppendLine($"net {axis.AxisID}-neg-lim {axisHalName}.neg-lim-sw-in <= not.{currentNotIndex}.out");
                        currentNotIndex++;
                    }
                    else
                    {
                        sb.AppendLine($"# Neg Limit: NO");
                        sb.AppendLine($"net {axis.AxisID}-neg-lim {axisHalName}.neg-lim-sw-in <= {rawNeg}");
                    }

                    string rawPos = $"{sliceName}.out-{axis.PosLimitDiIndex:00}";
                    if (axis.LimitSwitchLogic == LimitLogic.NC)
                    {
                        sb.AppendLine($"# Pos Limit: NC");
                        sb.AppendLine($"net {axis.AxisID}-pos-raw {rawPos} => not.{currentNotIndex}.in");
                        sb.AppendLine($"net {axis.AxisID}-pos-lim {axisHalName}.pos-lim-sw-in <= not.{currentNotIndex}.out");
                        currentNotIndex++;
                    }
                    else
                    {
                        sb.AppendLine($"# Pos Limit: NO");
                        sb.AppendLine($"net {axis.AxisID}-pos-lim {axisHalName}.pos-lim-sw-in <= {rawPos}");
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
        // 3. 生成 XML (EtherCAT Topology)
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
                var map = config.Mappings.FirstOrDefault(m => m.PhysicalIndex == i);

                if (map != null)
                {
                    bool isServo = IsServoDrive(map.ExpectedVendorId, map.ExpectedProductCode);

                    sb.AppendLine($"    ");
                    sb.AppendLine($"    <slave idx=\"{i}\" type=\"generic\" vid=\"{map.ExpectedVendorId}\" pid=\"{map.ExpectedProductCode}\" configPdos=\"true\">");
                    sb.AppendLine("      <dcConf assignActivate=\"0x300\" sync0Cycle=\"*1\" sync0Shift=\"0\"/>");

                    if (isServo)
                    {
                        sb.AppendLine("      <syncManager idx=\"2\" dir=\"out\">");
                        sb.AppendLine("        <pdo idx=\"1600\">");
                        sb.AppendLine($"          <pdoEntry idx=\"6040\" subIdx=\"00\" bitLen=\"16\" halPin=\"control_word_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"607a\" subIdx=\"00\" bitLen=\"32\" halPin=\"target_position_J{i}\" halType=\"s32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"6060\" subIdx=\"00\" bitLen=\"8\" halPin=\"modes_of_operation_J{i}\" halType=\"s32\"/>");
                        sb.AppendLine("        </pdo>");
                        sb.AppendLine("      </syncManager>");

                        sb.AppendLine("      <syncManager idx=\"3\" dir=\"in\">");
                        sb.AppendLine("        <pdo idx=\"1a00\">");
                        sb.AppendLine($"          <pdoEntry idx=\"6041\" subIdx=\"00\" bitLen=\"16\" halPin=\"status_word_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"6064\" subIdx=\"00\" bitLen=\"32\" halPin=\"position_actual_value_J{i}\" halType=\"s32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"6061\" subIdx=\"00\" bitLen=\"8\" halPin=\"modes_of_operation_display_J{i}\" halType=\"s32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"60fd\" subIdx=\"00\" bitLen=\"32\" halPin=\"digital_inputs_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine("        </pdo>");
                        sb.AppendLine("      </syncManager>");
                    }
                    else
                    {
                        sb.AppendLine("      <syncManager idx=\"2\" dir=\"out\">");
                        sb.AppendLine("        <pdo idx=\"1600\">");
                        sb.AppendLine($"          <pdoEntry idx=\"6040\" subIdx=\"00\" bitLen=\"16\" halPin=\"control_word_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"607a\" subIdx=\"00\" bitLen=\"32\" halPin=\"target_position_J{i}\" halType=\"s32\"/>");
                        sb.AppendLine("        </pdo>");
                        sb.AppendLine("      </syncManager>");

                        sb.AppendLine("      <syncManager idx=\"3\" dir=\"in\">");
                        sb.AppendLine("        <pdo idx=\"1a00\">");
                        sb.AppendLine($"          <pdoEntry idx=\"6041\" subIdx=\"00\" bitLen=\"16\" halPin=\"status_word_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"6064\" subIdx=\"00\" bitLen=\"32\" halPin=\"position_actual_value_J{i}\" halType=\"s32\"/>");
                        sb.AppendLine($"          <pdoEntry idx=\"60fd\" subIdx=\"00\" bitLen=\"32\" halPin=\"digital_inputs_J{i}\" halType=\"u32\"/>");
                        sb.AppendLine("        </pdo>");
                        sb.AppendLine("      </syncManager>");
                    }
                    sb.AppendLine("    </slave>");
                }
                else
                {
                    sb.AppendLine($"    ");
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
            Debug.WriteLine($"[SERVO_CHECK] Inspecting -> VID: '{vidStr}', PID: '{pidStr}'");

            if (string.IsNullOrEmpty(pidStr)) return false;

            string p = pidStr.ToLower().Trim().Replace("0x", "");
            string v = vidStr?.ToLower().Trim().Replace("0x", "") ?? "";

            Debug.WriteLine($"[SERVO_CHECK] Processed -> v: '{v}', p: '{p}'");

            if (p.Contains("c010d") || p.Contains("916"))
            {
                Debug.WriteLine("[SERVO_CHECK] Result: TRUE (Matched Inovance)");
                return true;
            }

            if (v.Contains("1dd") && p.Contains("6080"))
            {
                Debug.WriteLine("[SERVO_CHECK] Result: TRUE (Matched Delta ASDA-B3)");
                return true;
            }

            Debug.WriteLine("[SERVO_CHECK] Result: FALSE (No Rule Matched)");
            return false;
        }
    }
}