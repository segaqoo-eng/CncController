using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
            // IP 設定
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

            var payload = new { IniContent = iniContent, HalContent = halContent, XmlContent = xmlContent };
            try
            {
                // 1. 上傳設定檔
                await _http.PostAsJsonAsync("/api/config/update", payload);

                // ★★★ 2. 新增：發送重啟指令 ★★★
                await _http.PostAsync("/api/machine/restart", null);
            }
            catch { }
        }

        // ==========================================
        // 1. 生成 INI (路徑已修正)
        // ==========================================
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
            sb.AppendLine("OPEN_FILE = ./blank.ngc");
            sb.AppendLine("CONFIG_FILE = custom_config.yml");
            sb.AppendLine("CYCLE_TIME = 0.200");
            sb.AppendLine("POSITION_OFFSET = RELATIVE");
            sb.AppendLine("POSITION_FEEDBACK = ACTUAL");
            sb.AppendLine("MAX_FEED_OVERRIDE = 2.000000");
            sb.AppendLine("MAX_SPINDLE_OVERRIDE = 2.000000");
            sb.AppendLine("MIN_SPINDLE_OVERRIDE = 0.500000");
            sb.AppendLine("DEFAULT_SPINDLE_SPEED = 300");
            sb.AppendLine("PROGRAM_PREFIX = ~/linuxcnc/nc_files");
            sb.AppendLine("INTRO_GRAPHIC = pbsplash.png");
            sb.AppendLine("INTRO_TIME = 3");
            sb.AppendLine("EDITOR = gedit");
            sb.AppendLine("INCREMENTS = JOG 0.100 0.010 0.001");
            sb.AppendLine("DEFAULT_LINEAR_VELOCITY = 50.0000");
            sb.AppendLine("MAX_LINEAR_VELOCITY = 125.0000");
            sb.AppendLine("MIN_LINEAR_VELOCITY = 0.5000");
            sb.AppendLine("DEFAULT_ANGULAR_VELOCITY = 12.0000");
            sb.AppendLine("MAX_ANGULAR_VELOCITY = 180.0000");
            sb.AppendLine("MIN_ANGULAR_VELOCITY = 1.6667");
            sb.AppendLine($"GEOMETRY = {geometry}");
            sb.AppendLine($"DRO_DISPLAY = {geometry}");
            sb.AppendLine("OFFSET_COLUMNS = XYZR");
            sb.AppendLine("TOOL_TABLE_COLUMNS = TZDR");
            sb.AppendLine("KEYBOARD_JOG = true");
            sb.AppendLine("KEYBOARD_JOG_SAFETY_OFF = true");
            sb.AppendLine("ATC_TAB_DISPLAY = 2");
            sb.AppendLine("USER_BUTTONS_PATH = user_buttons/");
            sb.AppendLine("USER_DROS_PATH = user_dro_display/");
            sb.AppendLine("USER_ATC_BUTTONS_PATH = user_atc_buttons/");
            sb.AppendLine();

            sb.AppendLine("[FILTER]");
            sb.AppendLine("PROGRAM_EXTENSION = .nc,.txt,.tap Other NC files");
            sb.AppendLine("PROGRAM_EXTENSION = .png,.gif,.jpg Greyscale Depth Image");
            sb.AppendLine("png = image-to-gcode");
            sb.AppendLine("gif = image-to-gcode");
            sb.AppendLine("jpg = image-to-gcode");
            sb.AppendLine();

            sb.AppendLine("[PYTHON]");
            sb.AppendLine("TOPLEVEL = ./python/toplevel.py");
            sb.AppendLine("PATH_APPEND = ./python/");
            sb.AppendLine();

            sb.AppendLine("[ATC]");
            sb.AppendLine("POCKETS = 12");
            sb.AppendLine();

            sb.AppendLine("[RS274NGC]");
            sb.AppendLine("SUBROUTINE_PATH = macros_metric_sim");
            sb.AppendLine("PARAMETER_FILE = vmc_metric.var");
            sb.AppendLine("RS274NGC_STARTUP_CODE = F10 S300 G21 G17 G40 G49 G54 G64 P0.001 G80 G90 G91.1 G92.1 G94 G97 G98");
            sb.AppendLine("OWORD_NARGS = 1");
            sb.AppendLine("NO_DOWNCASE_OWORD = 1");
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
            sb.AppendLine($"SPINDLES = 1");
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
                double scale = axis.PulsePerRev / (axis.Pitch == 0 ? 1 : axis.Pitch);
                double homeVel = Math.Abs(axis.HomeSpeed) * axis.HomeDirection;
                int homeSeq = (axis.AxisID == "Z") ? 1 : 2;

                sb.AppendLine($"# --- Axis {axis.AxisID} ---");
                sb.AppendLine($"[AXIS_{axis.AxisID}]");
                sb.AppendLine("MAX_VELOCITY = 100.0");
                sb.AppendLine("MAX_ACCELERATION = 1000.0");
                sb.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                sb.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                sb.AppendLine();

                sb.AppendLine($"[JOINT_{i}]");
                sb.AppendLine("TYPE = LINEAR");
                sb.AppendLine("HOME = 0.0");
                sb.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                sb.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                sb.AppendLine("MAX_VELOCITY = 100.0");
                sb.AppendLine("MAX_ACCELERATION = 1000.0");
                sb.AppendLine($"STEP_SCALE = {scale}");
                sb.AppendLine("FERROR = 10.0");
                sb.AppendLine("MIN_FERROR = 1.0");
                sb.AppendLine($"HOME_OFFSET = {axis.HomeSpeed * 0.1}");
                sb.AppendLine($"HOME_SEARCH_VEL = {homeVel}");
                sb.AppendLine($"HOME_LATCH_VEL = {homeVel * -0.1}");
                sb.AppendLine("HOME_USE_INDEX = 0");
                sb.AppendLine("HOME_IGNORE_LIMITS = YES");
                sb.AppendLine($"HOME_SEQUENCE = {homeSeq}");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private string GenerateXml(MachineConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<masters>");
            sb.AppendLine($"  <master idx=\"{config.MasterIndex}\" appTimePeriod=\"1000000\" refClockSyncCycles=\"1000\">");

            foreach (var map in config.Mappings)
            {
                sb.AppendLine($"    <slave idx=\"{map.PhysicalIndex}\" type=\"generic\" vid=\"{map.ExpectedVendorId}\" pid=\"{map.ExpectedProductCode}\" configPdos=\"true\">");
                sb.AppendLine("      <dcConf assignActivate=\"300\" sync0Cycle=\"*1\" sync0Shift=\"0\"/>");
                sb.AppendLine("      <syncManager idx=\"2\" dir=\"out\">");
                sb.AppendLine("        <pdo idx=\"1600\">");
                sb.AppendLine($"          <pdoEntry idx=\"6040\" subIdx=\"00\" bitLen=\"16\" halPin=\"control_word_J{map.PhysicalIndex}\" halType=\"u32\"/>");
                sb.AppendLine($"          <pdoEntry idx=\"607a\" subIdx=\"00\" bitLen=\"32\" halPin=\"target_position_J{map.PhysicalIndex}\" halType=\"s32\"/>");
                sb.AppendLine($"          <pdoEntry idx=\"6060\" subIdx=\"00\" bitLen=\"8\" halPin=\"modes_of_operation_J{map.PhysicalIndex}\" halType=\"s32\"/>");
                sb.AppendLine($"          <pdoEntry idx=\"60fd\" subIdx=\"00\" bitLen=\"32\" halPin=\"digital_inputs_J{map.PhysicalIndex}\" halType=\"u32\"/>");
                sb.AppendLine("        </pdo>");
                sb.AppendLine("      </syncManager>");
                sb.AppendLine("      <syncManager idx=\"3\" dir=\"in\">");
                sb.AppendLine("        <pdo idx=\"1a00\">");
                sb.AppendLine($"          <pdoEntry idx=\"6041\" subIdx=\"00\" bitLen=\"16\" halPin=\"status_word_J{map.PhysicalIndex}\" halType=\"u32\"/>");
                sb.AppendLine($"          <pdoEntry idx=\"6064\" subIdx=\"00\" bitLen=\"32\" halPin=\"position_actual_value_J{map.PhysicalIndex}\" halType=\"s32\"/>");
                sb.AppendLine("        </pdo>");
                sb.AppendLine("      </syncManager>");
                sb.AppendLine("    </slave>");
            }
            sb.AppendLine("  </master>");
            sb.AppendLine("</masters>");
            return sb.ToString();
        }

        private string GenerateHal(MachineConfig config)
        {
            var sb = new StringBuilder();
            int count = config.Axes.Count;

            sb.AppendLine("# Generated by CncController");
            sb.AppendLine("loadrt [KINS]KINEMATICS");
            sb.AppendLine("loadrt [EMCMOT]EMCMOT servo_period_nsec=[EMCMOT]SERVO_PERIOD num_joints=[KINS]JOINTS");
            sb.AppendLine($"loadrt cia402 count={count}");

            var sliceNames = string.Join(",", config.Axes.Select(a => $"slice_{a.AxisID}"));
            var personalities = string.Join(",", Enumerable.Repeat("32", count));
            sb.AppendLine($"loadrt bitslice names={sliceNames} personality={personalities}");

            sb.AppendLine("loadusr -W lcec_conf ethercat-conf.xml");
            sb.AppendLine("loadrt lcec");

            // ★★★ [新增 1] 載入安全邏輯元件 ★★★
            sb.AppendLine("loadrt and2 count=1");
            sb.AppendLine("loadrt not count=1");
            sb.AppendLine("loadrt message names=msg_ec_error messages=\"CRITICAL ERROR: EtherCAT Communication Lost!\"");

            sb.AppendLine();
            sb.AppendLine("addf lcec.read-all          servo-thread");
            foreach (var axis in config.Axes) sb.AppendLine($"addf slice_{axis.AxisID}             servo-thread");
            for (int i = 0; i < count; i++) sb.AppendLine($"addf cia402.{i}.read-all       servo-thread");
            sb.AppendLine("addf motion-command-handler servo-thread");
            sb.AppendLine("addf motion-controller      servo-thread");

            // ★★★ [新增 2] 加入邏輯運算到執行緒 (必須在 motion-controller 之後) ★★★
            sb.AppendLine("addf and2.0       servo-thread");
            sb.AppendLine("addf not.0        servo-thread");
            sb.AppendLine("addf msg_ec_error servo-thread");

            for (int i = 0; i < count; i++) sb.AppendLine($"addf cia402.{i}.write-all      servo-thread");
            sb.AppendLine("addf lcec.write-all         servo-thread");
            sb.AppendLine();

            // ★★★ [新增 3] 安全迴路與錯誤發報接線 (替換掉原本的 Loopback) ★★★
            sb.AppendLine("# --- E-Stop Safety Loop & Error Msg ---");

            // 1. 安全開關 (AND閘): 只有當 (使用者按F2) 且 (EtherCAT連線正常) 時，才允許開機
            sb.AppendLine("net user-request    iocontrol.0.user-enable-out => and2.0.in0");
            // 注意：這裡使用 lcec.state-op 分接給 AND (in1) 和 NOT (in)
            sb.AppendLine("net ec-status       lcec.state-op               => and2.0.in1 not.0.in");
            sb.AppendLine("net system-ok       and2.0.out                  => iocontrol.0.emc-enable-in");

            // 2. 錯誤發報 (NOT閘): 當 EtherCAT 斷線(False) -> 反相為True -> 觸發紅色警報
            sb.AppendLine("net ec-error-trigger not.0.out                  => msg_ec_error.trigger");

            sb.AppendLine();
            sb.AppendLine("# --- Tool Change Loopback ---");
            sb.AppendLine("net tool-prep-loop iocontrol.0.tool-prepare => iocontrol.0.tool-prepared");
            sb.AppendLine();

            for (int i = 0; i < count; i++)
            {
                var axis = config.Axes[i];
                var mapping = config.Mappings.FirstOrDefault(m => m.LogicalName == axis.Name);

                if (mapping != null)
                {
                    int jIdx = i;
                    int sIdx = mapping.PhysicalIndex;
                    string sliceName = $"slice_{axis.AxisID}";

                    sb.AppendLine($"# Axis {axis.AxisID} -> Slave {sIdx}");
                    sb.AppendLine($"net {axis.AxisID}-enable      joint.{jIdx}.amp-enable-out  => cia402.{jIdx}.enable");
                    sb.AppendLine($"net {axis.AxisID}-control     cia402.{jIdx}.controlword    => lcec.0.{sIdx}.control_word_J{sIdx}");
                    sb.AppendLine($"net {axis.AxisID}-status      lcec.0.{sIdx}.status_word_J{sIdx} => cia402.{jIdx}.statusword");
                    sb.AppendLine($"setp cia402.{jIdx}.csp-mode 1");
                    sb.AppendLine($"setp lcec.0.{sIdx}.modes_of_operation_J{sIdx} 8");
                    sb.AppendLine($"setp cia402.{jIdx}.pos-scale [JOINT_{jIdx}]STEP_SCALE");
                    sb.AppendLine($"net {axis.AxisID}-pos-cmd     joint.{jIdx}.motor-pos-cmd          => cia402.{jIdx}.pos-cmd");
                    sb.AppendLine($"net {axis.AxisID}-drv-target  cia402.{jIdx}.drv-target-position  => lcec.0.{sIdx}.target_position_J{sIdx}");
                    sb.AppendLine($"net {axis.AxisID}-pos-fb      lcec.0.{sIdx}.position_actual_value_J{sIdx} => cia402.{jIdx}.drv-actual-position");
                    sb.AppendLine($"net {axis.AxisID}-pos-fb-final cia402.{jIdx}.pos-fb              => joint.{jIdx}.motor-pos-fb");

                    sb.AppendLine($"net {axis.AxisID}-di-raw      lcec.0.{sIdx}.digital_inputs_J{sIdx} => {sliceName}.in");
                    sb.AppendLine($"net {axis.AxisID}-home-sw     {sliceName}.out-02 => joint.{jIdx}.home-sw-in");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }
    }
}