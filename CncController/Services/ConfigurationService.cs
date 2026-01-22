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
            // 假設後端 API 地址
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
            catch
            {
                return new MachineConfig();
            }
        }

        public async Task SaveConfigAsync(MachineConfig config)
        {
            // 1. 存本地 JSON
            string json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(ConfigFileName, json);

            // 2. 動態生成三大設定檔
            string iniContent = GenerateIni(config);
            string xmlContent = GenerateXml(config);
            string halContent = GenerateHal(config);

            // 3. 上傳到後端
            var payload = new { IniContent = iniContent, HalContent = halContent, XmlContent = xmlContent };
            try
            {
                await _http.PostAsJsonAsync("/api/config/update", payload);
            }
            catch
            {
                // 忽略連線錯誤
            }
        }

        // ==========================================
        // 1. 生成 INI (包含 KINS, AXIS, JOINT)
        // ==========================================
        private string GenerateIni(MachineConfig config)
        {
            var sb = new StringBuilder();
            int axesCount = config.Axes.Count;

            sb.AppendLine("[EMC]");
            sb.AppendLine("MACHINE = EtherCAT-Machine");
            sb.AppendLine("VERSION = 1.1");
            sb.AppendLine();

            sb.AppendLine("[DISPLAY]");
            sb.AppendLine("DISPLAY = probe_basic");
            sb.AppendLine("CONFIG_FILE = custom_config.yml"); // 這是 probe_basic 需要的
            sb.AppendLine("GEOMETRY = XYZ"); // 假設是 XYZ
            sb.AppendLine("DRO_DISPLAY = XYZ");
            sb.AppendLine();

            sb.AppendLine("[KINS]");
            sb.AppendLine($"JOINTS = {axesCount}");
            // 使用 trivkins (XYZ架構)
            sb.AppendLine("KINEMATICS = trivkins coordinates=" + string.Join("", config.Axes.Select(a => a.AxisID)));
            sb.AppendLine();

            sb.AppendLine("[TRAJ]");
            sb.AppendLine("COORDINATES = " + string.Join(" ", config.Axes.Select(a => a.AxisID)));
            sb.AppendLine("LINEAR_UNITS = mm");
            sb.AppendLine("ANGULAR_UNITS = degree");
            sb.AppendLine("DEFAULT_LINEAR_VELOCITY = 25.00");
            sb.AppendLine("MAX_LINEAR_VELOCITY = 200.00");
            sb.AppendLine();

            sb.AppendLine("[EMCMOT]");
            sb.AppendLine("EMCMOT = motmod");
            sb.AppendLine("COMM_TIMEOUT = 1.0");
            sb.AppendLine("SERVO_PERIOD = 1000000"); // 1ms
            sb.AppendLine();

            // 生成 AXIS 和 JOINT 章節
            for (int i = 0; i < axesCount; i++)
            {
                var axis = config.Axes[i];
                double scale = axis.PulsePerRev / (axis.Pitch == 0 ? 1 : axis.Pitch);
                double homeVel = Math.Abs(axis.HomeSpeed) * axis.HomeDirection;

                // [AXIS_X]
                sb.AppendLine($"[AXIS_{axis.AxisID}]");
                sb.AppendLine("MAX_VELOCITY = 200.0");      // 預設值，未來可加到 Model
                sb.AppendLine("MAX_ACCELERATION = 1000.0"); // 預設值
                sb.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                sb.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                sb.AppendLine();

                // [JOINT_0]
                sb.AppendLine($"[JOINT_{i}]");
                sb.AppendLine("TYPE = LINEAR");
                sb.AppendLine("HOME = 0.0");
                sb.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                sb.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                sb.AppendLine("MAX_VELOCITY = 200.0");
                sb.AppendLine("MAX_ACCELERATION = 1000.0");
                sb.AppendLine($"STEP_SCALE = {scale}");
                sb.AppendLine("FERROR = 10.0");
                sb.AppendLine("MIN_FERROR = 1.0");

                // 原點設定
                sb.AppendLine("HOME_OFFSET = 0.0");
                sb.AppendLine($"HOME_SEARCH_VEL = {homeVel}");
                sb.AppendLine($"HOME_LATCH_VEL = {homeVel * 0.2}"); // 慢速定位設為 20%
                sb.AppendLine("HOME_SEQUENCE = 0"); // 同時回原點 (或可設為 i+1 依序回)
                sb.AppendLine("HOME_IGNORE_LIMITS = YES");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ==========================================
        // 2. 生成 XML (EtherCAT Topology)
        // ==========================================
        private string GenerateXml(MachineConfig config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<masters>");
            sb.AppendLine($"  <master idx=\"{config.MasterIndex}\" appTimePeriod=\"1000000\" refClockSyncCycles=\"1000\">");

            // 根據 Mapping 動態生成 Slave
            // 注意：這裡假設所有 Servo 都是標準 CiA402 設定 (參考您的 ethercat-conf.xml)
            foreach (var map in config.Mappings)
            {
                // idx 必須對應實體串接順序
                sb.AppendLine($"    <slave idx=\"{map.PhysicalIndex}\" type=\"generic\" vid=\"{map.ExpectedVendorId}\" pid=\"{map.ExpectedProductCode}\" configPdos=\"true\">");
                sb.AppendLine("      <dcConf assignActivate=\"300\" sync0Cycle=\"*1\" sync0Shift=\"0\"/>");

                // Sync Manager 2 (Outputs: Control, Target Pos, Mode, Digital Out)
                sb.AppendLine("      <syncManager idx=\"2\" dir=\"out\">");
                sb.AppendLine("        <pdo idx=\"1600\">");
                sb.AppendLine($"          <pdoEntry idx=\"6040\" subIdx=\"00\" bitLen=\"16\" halPin=\"control_word_J{map.PhysicalIndex}\" halType=\"u32\"/>");
                sb.AppendLine($"          <pdoEntry idx=\"607a\" subIdx=\"00\" bitLen=\"32\" halPin=\"target_position_J{map.PhysicalIndex}\" halType=\"s32\"/>");
                sb.AppendLine($"          <pdoEntry idx=\"6060\" subIdx=\"00\" bitLen=\"8\" halPin=\"modes_of_operation_J{map.PhysicalIndex}\" halType=\"s32\"/>");
                sb.AppendLine($"          <pdoEntry idx=\"60fd\" subIdx=\"00\" bitLen=\"32\" halPin=\"digital_inputs_J{map.PhysicalIndex}\" halType=\"u32\"/>");
                sb.AppendLine("        </pdo>");
                sb.AppendLine("      </syncManager>");

                // Sync Manager 3 (Inputs: Status, Actual Pos)
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

        // ==========================================
        // 3. 生成 HAL (訊號連接)
        // ==========================================
        private string GenerateHal(MachineConfig config)
        {
            var sb = new StringBuilder();
            int count = config.Axes.Count;

            // 載入核心模組
            sb.AppendLine("loadrt [KINS]KINEMATICS");
            sb.AppendLine("loadrt [EMCMOT]EMCMOT servo_period_nsec=[EMCMOT]SERVO_PERIOD num_joints=[KINS]JOINTS");
            sb.AppendLine($"loadrt cia402 count={count}"); // 載入 CiA402 驅動模組
            sb.AppendLine("loadrt lcec"); // 載入 EtherCAT 驅動

            sb.AppendLine();
            sb.AppendLine("# 執行緒順序");
            sb.AppendLine("addf lcec.read-all servo-thread");
            for (int i = 0; i < count; i++) sb.AppendLine($"addf cia402.{i}.read-all servo-thread");
            sb.AppendLine("addf motion-command-handler servo-thread");
            sb.AppendLine("addf motion-controller servo-thread");
            for (int i = 0; i < count; i++) sb.AppendLine($"addf cia402.{i}.write-all servo-thread");
            sb.AppendLine("addf lcec.write-all servo-thread");
            sb.AppendLine();

            // 建立每個軸的連線 (Joint -> CiA402 -> EtherCAT)
            for (int i = 0; i < count; i++)
            {
                var axis = config.Axes[i];
                // 尋找該軸對應的 EtherCAT Slave
                var mapping = config.Mappings.FirstOrDefault(m => m.LogicalName == axis.Name);

                if (mapping != null)
                {
                    int jointIdx = i; // 0, 1, 2...
                    int slaveIdx = mapping.PhysicalIndex; // 硬體站號

                    sb.AppendLine($"# --- Axis {axis.AxisID} (Joint {jointIdx}) mapped to Slave {slaveIdx} ---");

                    // 1. Enable 訊號
                    sb.AppendLine($"net {axis.AxisID}-enable joint.{jointIdx}.amp-enable-out => cia402.{jointIdx}.enable");

                    // 2. Control & Status Word (CiA402 <-> EtherCAT)
                    sb.AppendLine($"net {axis.AxisID}-control cia402.{jointIdx}.controlword => lcec.0.{slaveIdx}.control_word_J{slaveIdx}");
                    sb.AppendLine($"net {axis.AxisID}-status lcec.0.{slaveIdx}.status_word_J{slaveIdx} => cia402.{jointIdx}.statusword");

                    // 3. 模式設定 (CSP Mode = 8)
                    sb.AppendLine($"setp cia402.{jointIdx}.csp-mode 1");
                    sb.AppendLine($"setp lcec.0.{slaveIdx}.modes_of_operation_J{slaveIdx} 8"); // CSP

                    // 4. 位置指令與回授 (Position Command & Feedback)
                    sb.AppendLine($"setp cia402.{jointIdx}.pos-scale [JOINT_{jointIdx}]STEP_SCALE");

                    // LinuxCNC -> CiA402
                    sb.AppendLine($"net {axis.AxisID}-pos-cmd joint.{jointIdx}.motor-pos-cmd => cia402.{jointIdx}.pos-cmd");

                    // CiA402 -> EtherCAT (Target Position)
                    sb.AppendLine($"net {axis.AxisID}-drv-target cia402.{jointIdx}.drv-target-position => lcec.0.{slaveIdx}.target_position_J{slaveIdx}");

                    // EtherCAT -> CiA402 (Actual Position)
                    sb.AppendLine($"net {axis.AxisID}-pos-fb lcec.0.{slaveIdx}.position_actual_value_J{slaveIdx} => cia402.{jointIdx}.drv-actual-position");

                    // CiA402 -> LinuxCNC (Feedback)
                    sb.AppendLine($"net {axis.AxisID}-pos-fb-final cia402.{jointIdx}.pos-fb => joint.{jointIdx}.motor-pos-fb");

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}