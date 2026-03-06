import sys
import os
import time
import json
import logging
import threading
import collections
import configparser
import subprocess
import shutil
import signal
from flask import Flask, request, jsonify, g
from flask_cors import CORS

# 嘗試載入 linuxcnc 模組
try:
    import linuxcnc
except ImportError:
    print("[WARN] 'linuxcnc' module not found. Running in SIMULATION mode.")
    linuxcnc = None

app = Flask(__name__)
CORS(app)

log = logging.getLogger('werkzeug')
log.setLevel(logging.ERROR)

# ==============================================================================
# 1. 全域設定與路徑配置
# ==============================================================================

if 'SUDO_USER' in os.environ:
    USER_HOME = f"/home/{os.environ['SUDO_USER']}"
else:
    USER_HOME = os.path.expanduser("~")

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SCAN_SCRIPT = os.path.join(BASE_DIR, "smart_scan.py") 
SCAN_OUTPUT_JSON = os.path.join(BASE_DIR, "frontend_topology.json")

CONFIG_DIR = os.path.join(USER_HOME, "linuxcnc/configs/SGCAM_PB") 
LINUXCNC_INI_PATH = os.path.join(CONFIG_DIR, "3axis.ini") 
CONFIG_FILE = os.path.join(BASE_DIR, 'server_config.ini')

# [2026/02/11 新增] G-Code 存放目錄 (LinuxCNC 預設路徑)
NC_FILES_DIR = os.path.join(USER_HOME, "linuxcnc/nc_files")

os.environ['LINUXCNC_INI'] = LINUXCNC_INI_PATH
os.environ['LINUXCNC_CONFIG_DIR'] = CONFIG_DIR

print(f"[INIT] User Home: {USER_HOME}")
print(f"[INIT] Config Dir: {CONFIG_DIR}")
print(f"[INIT] INI Path:   {LINUXCNC_INI_PATH}")
print(f"[INIT] NC Files:   {NC_FILES_DIR}")

# 確保 NC Files 目錄存在
if not os.path.exists(NC_FILES_DIR):
    try:
        os.makedirs(NC_FILES_DIR)
        print(f"[INIT] Created NC Files Dir: {NC_FILES_DIR}")
    except Exception as e:
        print(f"[ERR] Failed to create NC dir: {e}")

config = configparser.ConfigParser()

SETTINGS = {
    'PORT': 5000,
    'LOG_LEVEL': 'ALL',
    'SHOW_API_DEBUG': True,
    'SHOW_SNIFFER_LOG': True,
    'ENABLE_SNIFFER': True,
    'ERROR_CACHE_SIZE': 20
}

def load_settings():
    global SETTINGS
    if os.path.exists(CONFIG_FILE):
        try:
            config.read(CONFIG_FILE)
            defaults = config['DEFAULT']
            cnc_conf = config['LINUXCNC']
            SETTINGS['PORT'] = defaults.getint('PORT', 5000)
            SETTINGS['LOG_LEVEL'] = defaults.get('LOG_LEVEL', 'ALL').upper()
            SETTINGS['SHOW_API_DEBUG'] = defaults.getboolean('SHOW_API_DEBUG', True)
            SETTINGS['SHOW_SNIFFER_LOG'] = defaults.getboolean('SHOW_SNIFFER_LOG', True)
            SETTINGS['ENABLE_SNIFFER'] = cnc_conf.getboolean('ENABLE_SNIFFER', True)
            SETTINGS['ERROR_CACHE_SIZE'] = cnc_conf.getint('ERROR_CACHE_SIZE', 20)
        except Exception as e:
            print(f"[ERR] Config load failed: {e}")

load_settings()

def app_log(level, msg):
    current_level = SETTINGS['LOG_LEVEL']
    if level == 'ERROR': print(f"[ERR] {msg}")
    elif level == 'SNIFFER' and SETTINGS['SHOW_SNIFFER_LOG']: print(f"[NML] {msg}")
    elif level == 'CMD' and current_level in ['ALL', 'INFO']: print(f"[CMD] {msg}")
    elif level == 'API' and SETTINGS['SHOW_API_DEBUG'] and current_level == 'ALL': print(f"[API] {msg}")

def success_response(data=None):
    return jsonify({'status': 'Success', 'version': '2026.02.05_V15_CUSTOM_CMD', 'data': data}), 200

def error_response(msg, code=500):
    app_log('ERROR', f"API Fail: {msg}")
    return jsonify({'status': 'Error', 'message': str(msg)}), code

@app.before_request
def before_request():
    if SETTINGS['SHOW_API_DEBUG'] and request.path != '/v2/status':
        app_log('API', f"REQ {request.method} {request.path}")

# ==============================================================================
# 2. LinuxCNC 進程管理 (使用 V15 客製化清理邏輯)
# ==============================================================================

def is_linuxcnc_alive():
    try:
        subprocess.check_call(["pgrep", "linuxcnc"], stdout=subprocess.DEVNULL)
        return True
    except:
        return False

def execute_user_cleanup_sequence():
    """ 
    [V15 客製化指令版]
    完全依照使用者指定的順序執行 Shell 指令 
    """
    print("[CLEANUP] Executing User Custom Sequence...", flush=True)

    # 依照您要求的順序定義指令
    commands = [
        "pkill -9 -x rtapi_app",
        "pkill -9 -x milltask",
        "pkill -9 -x qtvcp",
        "pkill -9 -x probe_basic",
        "pkill -9 -x linuxcnc",       # 注意：這行可能導致登出
        "pkill -9 -x linuxcncsvr",
        "pkill -9 -f 'python3 /usr/bin/linuxcnc'",
        "pkill -9 -f 'python3 /usr/bin/probe_basic'",
        "halrun -U",
        "ipcs -m | grep linuxcnc | awk '{print $2}' | xargs -r ipcrm -m",
        "rm -f /tmp/linuxcnc.lock /tmp/linuxcnc.stat"
    ]

    for cmd in commands:
        try:
            # 使用 shell=True 以支援 pipe (|) 和萬用字元
            subprocess.run(cmd, shell=True, stderr=subprocess.DEVNULL)
        except Exception as e:
            print(f"[WARN] Command failed: {cmd} -> {e}")

    print("[CLEANUP] User Sequence Complete.", flush=True)


def ensure_var_file_health():
    """ [終極修正版] 解決 KeyError: -1 """
    print("[SYS] Checking Parameter File health...", flush=True)
    
    param_file = "linuxcnc.var"
    try:
        with open(LINUXCNC_INI_PATH, 'r', encoding='utf-8', errors='ignore') as f:
            for line in f:
                if line.strip().upper().startswith("PARAMETER_FILE"):
                    parts = line.split('=')
                    if len(parts) > 1:
                        raw_val = parts[1].strip()
                        if "#" in raw_val: raw_val = raw_val.split('#')[0].strip()
                        if raw_val: param_file = raw_val
                        break
    except Exception as ex:
        print(f"[SYS] Error scanning INI: {ex}", flush=True)

    var_path = os.path.join(CONFIG_DIR, param_file)
    
    needs_rewrite = False
    
    if os.path.exists(var_path):
        try:
            with open(var_path, "r", encoding='utf-8', errors='ignore') as f:
                content = f.read()
            if "5220" not in content:
                needs_rewrite = True
        except:
            needs_rewrite = True
    else:
        needs_rewrite = True

    if needs_rewrite:
        print(f"[FIX] Recreating healthy {param_file}...", flush=True)
        if os.path.exists(var_path):
            try: shutil.copy(var_path, var_path + ".bak")
            except: pass
        
        try:
            with open(var_path, "w") as f:
                f.write("5220 1.000000\n5221 0.000000\n5222 0.000000\n5223 0.000000\n")
                f.write("5240 0.000000\n5161 0.000000\n5162 0.000000\n5163 0.000000\n")
        except Exception as e:
            print(f"[ERR] Failed to write var file: {e}", flush=True)
    
    try: os.remove(os.path.join(CONFIG_DIR, "position.txt"))
    except: pass


def start_linuxcnc_process():
    """ 啟動 LinuxCNC """
    # 啟動前先跑一次您的客製化清理
    execute_user_cleanup_sequence()

    if is_linuxcnc_alive():
        print("[SYS] LinuxCNC is already running.", flush=True)
        return

    print(f"[SYS] Starting LinuxCNC... INI: {LINUXCNC_INI_PATH}", flush=True)
    
    if not os.path.exists(LINUXCNC_INI_PATH):
        print(f"[ERR] INI file missing: {LINUXCNC_INI_PATH}")
        return

    ensure_var_file_health()

    try:
        env = os.environ.copy()
        if "DISPLAY" not in env: env["DISPLAY"] = ":0"
        
        subprocess.Popen(
            ["linuxcnc", LINUXCNC_INI_PATH],
            env=env,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            cwd=CONFIG_DIR,
            start_new_session=True
        )
        print("[SYS] Launch command sent.", flush=True)
        
        time.sleep(3) 
        
    except Exception as e:
        print(f"[ERR] Start failed: {e}")
        
        
def perform_hard_restart():
    """ 
    執行硬重啟流程: 
    1. 跑使用者指令 
    2. 呼叫 systemctl restart 
    3. 自殺
    """
    print("[SERVER] Hard Restart Requested via API...", flush=True)
    
    # 1. 執行清理指令
    execute_user_cleanup_sequence()

    # 2. 嘗試呼叫 systemctl restart (需要 sudo 權限)
    print("[SERVER] Triggering: sudo systemctl restart cnc-server.service", flush=True)
    try:
        subprocess.Popen("sudo systemctl restart cnc-server.service", shell=True)
    except Exception as e:
        print(f"[ERR] Systemctl call failed: {e}", flush=True)
    
    # 3. 自殺 (Exit Code 42)
    # 如果 Systemctl 成功，這個 Process 本來就會被殺掉
    # 如果 Systemctl 失敗，Guardian 會看到 42 並幫忙重啟
    print("[SERVER] Exiting with code 42...", flush=True)
    time.sleep(1)
    os._exit(42) 

# ==============================================================================
# 4. NML 連線
# ==============================================================================

cnc_cmd = None
cnc_stat = None
cached_errors = collections.deque(maxlen=SETTINGS['ERROR_CACHE_SIZE'])
error_lock = threading.Lock()

# [2026-02-25] in-memory WCS offset cache
# 解決 .var 檔僅關機時寫入，RELOAD 後非 Active WCS 仍顯示舊值的問題
# 更新時機：每次透過 MDI 送出 G10 L2 指令後即時更新
_wcs_cache = {}

def _update_wcs_cache_from_g10(command):
    """解析 G10 L2 P<n> ... 指令，並更新 _wcs_cache"""
    import re
    m = re.match(r'G10\s+L2\s+P(\d+)\s+(.*)', command.strip(), re.IGNORECASE)
    if not m:
        return
    p_num = int(m.group(1))
    p_to_wcs = {1:'G54', 2:'G55', 3:'G56', 4:'G57', 5:'G58', 6:'G59',
                7:'G59.1', 8:'G59.2', 9:'G59.3'}
    wcs = p_to_wcs.get(p_num)
    if not wcs:
        return
    axes_vals = re.findall(r'([XYZABC])([-+]?[\d.]+)', m.group(2), re.IGNORECASE)
    if wcs not in _wcs_cache:
        _wcs_cache[wcs] = {}
    for ax, val in axes_vals:
        _wcs_cache[wcs][ax.upper()] = round(float(val), 4)

def ensure_cnc_connections():
    global cnc_cmd, cnc_stat
    if linuxcnc is None: return False

    if cnc_stat is None or cnc_cmd is None:
        try:
            if cnc_cmd is None: cnc_cmd = linuxcnc.command()
            if cnc_stat is None: cnc_stat = linuxcnc.stat()
            print("[NML] Connection Established.", flush=True)
        except Exception:
            return False

    try:
        cnc_stat.poll()
        return True
    except Exception as e:
        print(f"[NML] Connection Lost. Reconnecting...", flush=True)
        cnc_stat = None
        cnc_cmd = None
        return False

def error_sniffer_loop():
    if not linuxcnc: return
    app_log('INFO', "Error Sniffer Thread Started.")
    sniffer = None
    while True:
        if SETTINGS['ENABLE_SNIFFER']:
            try:
                if sniffer is None: sniffer = linuxcnc.error_channel()
                error = sniffer.poll()
                if error:
                    kind, text = error
                    app_log('SNIFFER', f"Captured: {text}")
                    with error_lock:
                        cached_errors.append({"Kind": str(kind), "Text": text})
            except:
                sniffer = None
                time.sleep(1)
        time.sleep(0.1)

if linuxcnc and SETTINGS['ENABLE_SNIFFER']:
    t = threading.Thread(target=error_sniffer_loop, daemon=True)
    t.start()

# ==============================================================================
# 5. IO 讀取
# ==============================================================================
def read_servo_raw_data():
    """ 讀取 lcec 的 HAL Pin 狀態 (DI & Status Word) """
    data = {}
    try:
        res = subprocess.run(
            ["halcmd", "-s", "show", "pin", "lcec"], 
            capture_output=True, text=True, timeout=0.2
        )
        if res.returncode != 0: return {}

        for line in res.stdout.splitlines():
            if "lcec.0" not in line: continue
            parts = line.split()
            
            # 尋找包含 "lcec.0" 的欄位
            name_idx = -1
            for i, p in enumerate(parts):
                if "lcec.0" in p:
                    name_idx = i
                    break
            
            if name_idx <= 0: continue

            name = parts[name_idx]
            val = parts[name_idx - 1]

            try:
                tokens = name.split('.')
                if len(tokens) < 4: continue
                slave_idx = int(tokens[2])
                
                if slave_idx not in data: data[slave_idx] = {}

                if "digital_inputs" in name:
                    data[slave_idx]['DI'] = val 
                elif "status_word" in name:
                    data[slave_idx]['Status'] = val
            except:
                continue
    except:
        pass 
    return data

# ==============================================================================
# 6. API 路由
# ==============================================================================

@app.route('/api/ethercat/scan', methods=['POST', 'GET'])
def scan_ethercat():
    if os.path.exists(SCAN_OUTPUT_JSON):
        try: os.remove(SCAN_OUTPUT_JSON)
        except: pass

    try:
        if not os.path.exists(SCAN_SCRIPT):
            raise FileNotFoundError("Scan script missing")

        result = subprocess.run(
            ["python3", SCAN_SCRIPT], 
            capture_output=True, text=True, cwd=BASE_DIR, timeout=15
        )
        if os.path.exists(SCAN_OUTPUT_JSON):
            with open(SCAN_OUTPUT_JSON, 'r', encoding='utf-8') as f:
                data = json.load(f)
            output = data.get("topology", data)
            return jsonify(output if output else []), 200
        else:
            return jsonify([]), 200
    except Exception as e:
        return jsonify([]), 200

@app.route('/api/config/update', methods=['POST'])
def update_config():
    try:
        data = request.json
        if not data: return jsonify({"error": "No data"}), 400

        if not os.path.exists(CONFIG_DIR): os.makedirs(CONFIG_DIR)
        data_lower = {k.lower(): v for k, v in data.items()}

        files_map = {
            'ethercat-conf.xml': data_lower.get('xmlcontent'),
            '3axis.hal': data_lower.get('halcontent'),
            '3axis.ini': data_lower.get('inicontent'),
            'probe_basic_postgui.hal': data_lower.get('postguicontent'),
            # [2026-03-05] 探針模擬 HAL（前端 IsProbeSimulation=true 時才傳送）
            'sim_probe.hal': data_lower.get('simprobehalcontent')
        }
        
        updated = []
        for name, content in files_map.items():
            if content:
                path = os.path.join(CONFIG_DIR, name)
                with open(path, 'w', encoding='utf-8') as f:
                    f.write(content.replace('\r\n', '\n'))
                updated.append(path)

        dependencies = {
            "tool.tbl": "T1 P1 D10.0 ; Default Tool\n",
            "linuxcnc.var": "",
            "custom_config.yml": "# Auto-generated Probe Basic Config\n",
            "probe_basic_postgui.hal": "# Auto-generated PostGUI HAL\n"
        }
        for fname, default_content in dependencies.items():
            fpath = os.path.join(CONFIG_DIR, fname)
            if not os.path.exists(fpath):
                with open(fpath, "w", encoding="utf-8") as f: f.write(default_content)

        return jsonify({"status": "success", "updated": updated}), 200
    except Exception as e:
        return jsonify({"error": str(e)}), 500
        
@app.route('/api/machine/restart', methods=['POST'])
def api_machine_restart():
    try:
        t = threading.Thread(target=perform_hard_restart)
        t.start()
        return success_response({"message": "Server & Machine Restarting..."})
    except Exception as e:
        return error_response(f"Restart API failed: {e}")

# [2026/02/11 新增] 檔案上傳 API (記憶體不落地/本機測試用)
import os
from flask import request, jsonify # 確保有匯入 request 和 jsonify

# 設定 NC 檔案存放路徑 (通常是 ~/linuxcnc/nc_files)
# 請確認這個路徑存在，且 LinuxCNC 也是讀取這裡
NC_FILES_DIR = os.path.expanduser("~/linuxcnc/nc_files")

@app.route('/api/files/upload', methods=['POST'])
def upload_file():
    """
    接收前端傳來的 G-Code 字串並存檔
    Payload: { "name": "test.ngc", "content": "G0 X0 Y0..." }
    """
    try:
        data = request.json
        if not data:
            return jsonify({'status': 'Error', 'message': 'No JSON data received'}), 400

        filename = data.get('name')
        content = data.get('content')

        if not filename or content is None:
            return jsonify({'status': 'Error', 'message': 'Missing filename or content'}), 400

        # [資安] 簡單過濾檔名，避免目錄遍歷攻擊 (Directory Traversal)
        filename = os.path.basename(filename)
        
        # 確保目錄存在
        if not os.path.exists(NC_FILES_DIR):
            os.makedirs(NC_FILES_DIR)

        filepath = os.path.join(NC_FILES_DIR, filename)
        
        # 寫入檔案 (使用 UTF-8)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
            
        print(f"[Upload] File saved: {filepath}") # Server Log
        
        return jsonify({
            'status': 'Success', 
            'message': f'File {filename} uploaded successfully',
            'path': filepath
        })

    except Exception as e:
        print(f"[Upload Error] {str(e)}")
        return jsonify({'status': 'Error', 'message': str(e)}), 500

        
@app.route('/v2/status', methods=['GET'])
def v2_status():
    global cnc_stat
    
    # 這裡不檢查 Process 死活，因為重啟中可能剛死掉
    if not ensure_cnc_connections(): 
        return jsonify({'status': 'Error', 'message': 'NML Disconnected'}), 503

    try:
        cnc_stat.poll()
        pos_dict = {}
        try:
            raw_pos = cnc_stat.actual_position
            for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                if i < len(raw_pos): pos_dict[axis] = float(f"{raw_pos[i]:.4f}")
        except: pass

        t_state = "UNKNOWN"
        if cnc_stat.task_state == linuxcnc.STATE_ON: t_state = "ON"
        elif cnc_stat.task_state == linuxcnc.STATE_OFF: t_state = "OFF"
        elif cnc_stat.task_state == linuxcnc.STATE_ESTOP: t_state = "ESTOP"

        i_state = "IDLE"
        if cnc_stat.interp_state == linuxcnc.INTERP_IDLE: i_state = "IDLE"
        elif cnc_stat.interp_state == linuxcnc.INTERP_READING: i_state = "RUNNING"
        elif cnc_stat.interp_state == linuxcnc.INTERP_PAUSED: i_state = "PAUSED"
        elif cnc_stat.interp_state == linuxcnc.INTERP_WAITING: i_state = "RUNNING"

        feed = 0.0
        try: feed = cnc_stat.settings[1]
        except: pass
        
        spindle_speed = 0.0
        try: 
            if hasattr(cnc_stat, 'spindle') and len(cnc_stat.spindle) > 0:
                 spindle_speed = abs(cnc_stat.spindle[0]['speed'])
        except: pass

        filename = "No File"
        try: 
            if cnc_stat.file: filename = os.path.basename(cnc_stat.file)
        except: pass
        
        dtg_dict = {}
        try:
            raw_dtg = cnc_stat.dtg
            for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                if i < len(raw_dtg): dtg_dict[axis] = float(f"{raw_dtg[i]:.4f}")
        except: pass

        servo_io_data = read_servo_raw_data()

        # [2026-02-24] 擴充 Active_WCS 支援 G59.1-G59.3（g5x_index: 7=G59.1, 8=G59.2, 9=G59.3）
        active_wcs = "G54"
        try:
            idx = cnc_stat.g5x_index
            _idx_map = {1:'G54', 2:'G55', 3:'G56', 4:'G57', 5:'G58', 6:'G59',
                        7:'G59.1', 8:'G59.2', 9:'G59.3'}
            active_wcs = _idx_map.get(idx, "G54")
        except:
            pass

        # [2026-02-24] 新增 Work_Position：計算工件座標 = actual_position - g5x_offset - g92_offset - tool_offset
        work_pos = {}
        try:
            g5x = cnc_stat.g5x_offset
            g92 = cnc_stat.g92_offset
            tool = cnc_stat.tool_offset
            for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                if i < len(raw_pos):
                    work_pos[axis] = float(f"{raw_pos[i] - g5x[i] - g92[i] - tool[i]:.4f}")
        except:
            pass

        # [2026-02-24] 新增 Feed_Override / Spindle_Override：讀取目前的進給率與主軸轉速覆蓋百分比
        feed_override = 100.0
        try:
            feed_override = round(cnc_stat.feedrate * 100.0, 1)
        except:
            pass

        spindle_override = 100.0
        try:
            if hasattr(cnc_stat, 'spindle') and len(cnc_stat.spindle) > 0:
                spindle_override = round(cnc_stat.spindle[0]['override'] * 100.0, 1)
        except:
            pass

        # [2026-02-24] 新增刀具資訊：刀具號、刀長（Z 軸補正）、刀徑
        tool_number = 0
        tool_length = 0.0
        tool_diameter = 0.0
        try:
            tool_number = int(cnc_stat.tool_in_spindle)
            tool_length = float(cnc_stat.tool_offset[2])  # Z 軸補正 = 刀長
            if tool_number > 0 and tool_number < len(cnc_stat.tool_table):
                tool_diameter = float(cnc_stat.tool_table[tool_number].diameter)
        except:
            pass

        # [2026-02-24] 新增 Homed 狀態：各軸原點復歸是否完成
        homed_dict = {}
        for i, name in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
            try:
                # [2026-02-24] 修正：改用 cnc_stat.homed[i] 取代 joint[i].homed（joint 回傳 dict 無 homed 屬性）
                homed_dict[name] = bool(cnc_stat.homed[i])
            except:
                homed_dict[name] = False

        # [2026-02-24] 新增 G92 偏移量（供 Offsets 右欄 G52/G92 OFFSET 欄位顯示）
        g92_dict = {}
        try:
            for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                if i < len(g92):
                    g92_dict[axis] = round(float(g92[i]), 4)
        except:
            pass

        # [2026-02-24] 新增完整刀具偏移（供 Offsets 右欄 TOOL OFFSET 欄位顯示）
        tool_offset_dict = {}
        try:
            for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                if i < len(tool):
                    tool_offset_dict[axis] = round(float(tool[i]), 4)
        except:
            pass

        # [2026-02-24] 新增任務模式（供 Offsets 右下角 MAN/AUTO/MDI 按鈕高亮）
        task_mode_str = {1: "MANUAL", 2: "AUTO", 3: "MDI"}.get(cnc_stat.task_mode, "UNKNOWN")

        # [2026-03-06] 新增 Probe_Input：探針輸入訊號即時狀態（motion.probe-input）
        probe_input = False
        try:
            probe_input = bool(cnc_stat.probe_val)
        except: pass

        # [2026-03-04] 新增 Block Delete / Optional Stop / Current Line
        block_delete = False
        optional_stop = False
        current_line = 0
        try:
            block_delete = bool(cnc_stat.block_delete)
        except: pass
        try:
            optional_stop = bool(cnc_stat.optional_stop)
        except: pass
        try:
            current_line = int(cnc_stat.motion_line)
        except: pass

        return success_response({
            "Connected": True,
            "Task_State": t_state,
            "Interp_State": i_state,
            "Position": pos_dict,
            "Work_Position": work_pos,
            "DTG": dtg_dict,
            "Feedrate": feed,
            "Spindle_Speed": spindle_speed,
            "Feed_Override": feed_override,
            "Spindle_Override": spindle_override,
            "File": filename,
            "Servo_IO": servo_io_data,
            "Active_WCS": active_wcs,
            "Tool_Number": tool_number,
            "Tool_Length": round(tool_length, 4),
            "Tool_Diameter": round(tool_diameter, 4),
            "Homed": homed_dict,
            "G92_Offset": g92_dict,
            "Tool_Offset_XYZ": tool_offset_dict,
            "Task_Mode": task_mode_str,
            "Probe_Input": probe_input,
            "Block_Delete": block_delete,
            "Optional_Stop": optional_stop,
            "Current_Line": current_line
        })

    except Exception as e:
        if "without exception set" in str(e):
            return jsonify({'status': 'Error', 'message': 'NML Glitch'}), 503
        return jsonify({'status': 'Error', 'message': str(e)}), 503

@app.route('/v2/errors', methods=['GET'])
def v2_errors():
    messages = []
    with error_lock:
        while cached_errors:
            messages.append(cached_errors.popleft())
    return success_response(messages if messages else None)


# [2026-02-23] 新增 read_work_offsets：從 LinuxCNC .var 參數檔讀取 G54–G59 六組 offset 值
def read_work_offsets():
    """從 LinuxCNC Parameter File (.var) 讀取 G54–G59 六組 WCS 座標值"""
    # [2026-02-24] 擴充 G59.1-G59.3（對齊 PB 版 Offsets 頁面）
    param_bases = {
        'G54': 5221, 'G55': 5241, 'G56': 5261,
        'G57': 5281, 'G58': 5301, 'G59': 5321,
        'G59.1': 5341, 'G59.2': 5361, 'G59.3': 5381
    }
    axes = ['X', 'Y', 'Z', 'A', 'B', 'C']
    offsets = {wcs: {a: 0.0 for a in axes} for wcs in param_bases}

    # 從 INI 找 .var 路徑
    var_path = os.path.join(CONFIG_DIR, "linuxcnc.var")
    try:
        with open(LINUXCNC_INI_PATH, 'r', errors='ignore') as f:
            for line in f:
                if 'PARAMETER_FILE' in line.upper():
                    val = line.split('=', 1)[1].strip().split('#')[0].strip()
                    if val:
                        var_path = os.path.join(CONFIG_DIR, val)
                    break
    except Exception:
        pass

    if os.path.exists(var_path):
        try:
            params = {}
            with open(var_path, 'r') as f:
                for line in f:
                    parts = line.strip().split()
                    if len(parts) == 2:
                        try:
                            params[int(parts[0])] = float(parts[1])
                        except Exception:
                            pass
            for wcs, base in param_bases.items():
                for i, axis in enumerate(axes):
                    if (base + i) in params:
                        offsets[wcs][axis] = round(params[base + i], 4)
        except Exception:
            pass

    # [2026-02-25] 用 in-memory cache 覆蓋 .var 的值（G10 L2 執行後立即更新，比 .var 更新）
    for wcs, cached_vals in _wcs_cache.items():
        if wcs in offsets:
            offsets[wcs].update(cached_vals)

    # [2026-02-24] 用 cnc_stat 記憶體值覆蓋 Active WCS（.var 檔僅關機時寫入，G10 後必定過時）
    # [2026-02-24] 擴充支援 G59.1-G59.3（g5x_index: 7=G59.1, 8=G59.2, 9=G59.3）
    try:
        if cnc_stat:
            cnc_stat.poll()
            idx = cnc_stat.g5x_index  # 1=G54, 2=G55, ..., 6=G59, 7=G59.1, 8=G59.2, 9=G59.3
            idx_to_wcs = {1:'G54', 2:'G55', 3:'G56', 4:'G57', 5:'G58', 6:'G59',
                          7:'G59.1', 8:'G59.2', 9:'G59.3'}
            active_wcs = idx_to_wcs.get(idx)
            if active_wcs and active_wcs in offsets:
                g5x = cnc_stat.g5x_offset
                for i, axis in enumerate(axes):
                    if i < len(g5x):
                        offsets[active_wcs][axis] = round(float(g5x[i]), 4)
    except Exception:
        pass

    return offsets


# [2026-02-23] 新增 /v2/offsets 端點：回傳 G54–G59 所有工件座標系偏移值 + 目前 Active WCS
@app.route('/v2/offsets', methods=['GET'])
def v2_offsets():
    """回傳 G54–G59 所有工件座標系偏移值 + 目前 Active WCS"""
    # [2026-02-24] 擴充支援 G59.1-G59.3
    active_wcs = "G54"
    try:
        if ensure_cnc_connections():
            cnc_stat.poll()
            idx = cnc_stat.g5x_index
            _idx_map = {1:'G54', 2:'G55', 3:'G56', 4:'G57', 5:'G58', 6:'G59',
                        7:'G59.1', 8:'G59.2', 9:'G59.3'}
            active_wcs = _idx_map.get(idx, "G54")
    except Exception:
        pass

    offsets = read_work_offsets()
    return success_response({"Active": active_wcs, "Offsets": offsets})

@app.route('/v2/motion/jog', methods=['POST'])
def v2_motion_jog():
    if not ensure_cnc_connections(): return error_response("No channel")
    try:
        data = request.json
        axis = int(data.get('axis', 0))
        speed = float(data.get('speed', 0))
        dist = float(data.get('dist', 0))

        cnc_stat.poll()
        if cnc_stat.task_state == linuxcnc.STATE_ESTOP: return error_response("ESTOP Active")
        if cnc_stat.task_state != linuxcnc.STATE_ON: return error_response("Power OFF")

        if speed == 0.0:
            jjogmode = 1 if cnc_stat.motion_mode == 1 else 0
            cnc_cmd.jog(linuxcnc.JOG_STOP, jjogmode, axis)
            return success_response()

        if cnc_stat.task_mode != linuxcnc.MODE_MANUAL:
            cnc_cmd.mode(linuxcnc.MODE_MANUAL)
            cnc_cmd.wait_complete()

        is_homed = False
        try:
            # [2026-02-24] 修正：改用 cnc_stat.homed[axis] 取代 joint[axis].homed
            if hasattr(cnc_stat, 'homed') and len(cnc_stat.homed) > axis:
                is_homed = bool(cnc_stat.homed[axis])
        except: pass

        if is_homed:
            if cnc_stat.motion_mode != 3: 
                cnc_cmd.teleop_enable(1)
                cnc_cmd.wait_complete()
            jjogmode = 0
        else:
            if cnc_stat.motion_mode != 1: 
                cnc_cmd.teleop_enable(0)
                cnc_cmd.wait_complete()
            jjogmode = 1

        final_speed = abs(speed)
        if dist > 0:
            direction = 1.0 if speed > 0 else -1.0
            cnc_cmd.jog(linuxcnc.JOG_INCREMENT, jjogmode, axis, final_speed, dist * direction)
        else:
            cnc_cmd.jog(linuxcnc.JOG_CONTINUOUS, jjogmode, axis, speed)

        return success_response()
    except Exception as e:
        return error_response(str(e))

@app.route('/v2/program/run', methods=['POST'])
def v2_program_run():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        line = int(data.get('line', 0))
        # [2026/02/11 新增] 支援指定檔名
        file_name = data.get('file_name') 
        
        cnc_stat.poll()
        if cnc_stat.task_state != linuxcnc.STATE_ON: return error_response("Machine OFF")
        
        # [2026/02/11 修改] 如果是暫停中，轉發 Resume (相容舊版邏輯)
        if cnc_stat.interp_state == linuxcnc.INTERP_PAUSED:
            cnc_cmd.auto(linuxcnc.AUTO_RESUME)
            return success_response("Resumed (Auto)")
            
        # 切換 Auto 模式
        if cnc_stat.task_mode != linuxcnc.MODE_AUTO:
            cnc_cmd.mode(linuxcnc.MODE_AUTO)
            cnc_cmd.wait_complete()
            
        # [2026/02/11 新增] 若有指定檔案，先載入
        if file_name:
            # 確保檔名完整路徑
            full_path = os.path.join(NC_FILES_DIR, os.path.basename(file_name))
            if os.path.exists(full_path):
                cnc_cmd.program_open(full_path)
                cnc_cmd.wait_complete()
            else:
                return error_response(f"File not found: {file_name}", 404)
        
        cnc_cmd.auto(linuxcnc.AUTO_RUN, line)
        return success_response("Started")
    except Exception as e: return error_response(f"Run Fail: {e}")

# [2026/02/11 新增] 獨立 Resume 接口
@app.route('/v2/program/resume', methods=['POST'])
def v2_program_resume():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        cnc_stat.poll()
        if cnc_stat.interp_state == linuxcnc.INTERP_PAUSED:
            cnc_cmd.auto(linuxcnc.AUTO_RESUME)
            return success_response("Resumed")
        else:
            return error_response("Not Paused", 400)
    except Exception as e: return error_response(f"Resume Fail: {e}")

@app.route('/v2/program/pause', methods=['POST'])
def v2_program_pause():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        cnc_cmd.auto(linuxcnc.AUTO_PAUSE)
        return success_response("Paused")
    except Exception as e: return error_response(f"Pause Fail: {e}")

@app.route('/v2/program/stop', methods=['POST'])
def v2_program_stop():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        cnc_cmd.abort()
        return success_response("Aborted")
    except Exception as e: return error_response(f"Stop Fail: {e}")

@app.route('/v2/machine/reset', methods=['POST'])
def v2_machine_reset():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        cnc_stat.poll()
        if cnc_stat.task_state == linuxcnc.STATE_ESTOP:
            cnc_cmd.state(linuxcnc.STATE_ESTOP_RESET)
        elif cnc_stat.task_state == linuxcnc.STATE_ON:
            cnc_cmd.state(linuxcnc.STATE_OFF)
        else:
            cnc_cmd.state(linuxcnc.STATE_ON)
        return success_response()
    except Exception as e: return error_response(f"Reset Fail: {e}")

@app.route('/v2/machine/estop', methods=['POST'])
def v2_machine_estop():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        cnc_cmd.state(linuxcnc.STATE_ESTOP)
        return success_response()
    except Exception as e: return error_response(f"Estop Fail: {e}")

# [2026-02-23] 新增 /v2/machine/home 端點：使用 cnc_cmd.home() 執行回原點（-1=全軸）
#              G28 MDI 為移至預設參考點，不等於 LinuxCNC 的 home 指令
@app.route('/v2/machine/home', methods=['POST'])
def v2_machine_home():
    """回原點：-1 = 全軸，0~5 = 單軸"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        cnc_stat.poll()
        if cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine must be ON to home")
        data = request.json or {}
        axis = int(data.get('axis', -1))  # -1 = 全軸
        cnc_cmd.mode(linuxcnc.MODE_MANUAL)
        cnc_cmd.wait_complete()
        # [2026-02-24] 修正：回原點前必須切換至 Joint Mode（teleop_enable(0)）
        # LinuxCNC 在 Teleop（世界座標）模式下 home() 指令會被忽略
        cnc_cmd.teleop_enable(0)
        cnc_cmd.wait_complete()
        cnc_cmd.home(axis)
        app_log('CMD', f'Home axis={axis}')
        return success_response()
    except Exception as e:
        return error_response(f"Home Fail: {e}")

# [2026-02-24] 新增 /v2/machine/mode 端點：切換任務模式（MAN/AUTO/MDI）
@app.route('/v2/machine/mode', methods=['POST'])
def v2_machine_mode():
    """切換任務模式：MANUAL / AUTO / MDI"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        mode = data.get('mode', '').upper()
        mode_map = {
            'MANUAL': linuxcnc.MODE_MANUAL,
            'AUTO': linuxcnc.MODE_AUTO,
            'MDI': linuxcnc.MODE_MDI
        }
        if mode not in mode_map:
            return error_response(f"Invalid mode: {mode}. Use MANUAL/AUTO/MDI", 400)
        cnc_cmd.mode(mode_map[mode])
        cnc_cmd.wait_complete()
        app_log('CMD', f'Mode changed to {mode}')
        return success_response({"mode": mode})
    except Exception as e:
        return error_response(f"Mode Fail: {e}")

@app.route('/v2/mdi', methods=['POST'])
def v2_mdi():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        command = data.get('command', '').strip()
        if not command:
            return error_response("No command provided", 400)

        cnc_stat.poll()
        if cnc_stat.task_state == linuxcnc.STATE_ESTOP:
            return error_response("ESTOP Active", 400)
        if cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine OFF", 400)

        if cnc_stat.task_mode != linuxcnc.MODE_MDI:
            cnc_cmd.mode(linuxcnc.MODE_MDI)
            cnc_cmd.wait_complete()

        cnc_cmd.mdi(command)
        cnc_cmd.wait_complete()  # [2026-02-24] 等待 MDI 執行完畢，避免後續讀取到舊值
        _update_wcs_cache_from_g10(command)  # [2026-02-25] 若為 G10 L2，即時更新 WCS cache
        app_log('CMD', f"MDI: {command}")
        return success_response({"command": command})
    except Exception as e:
        return error_response(f"MDI Fail: {e}")

# [2026-02-24] 新增 /v2/override/feed 端點：設定進給率覆蓋百分比
@app.route('/v2/override/feed', methods=['POST'])
def v2_override_feed():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        value = float(data.get('value', 100.0))
        scale = max(0.0, min(value / 100.0, 2.0))  # 限制 0%~200%
        cnc_cmd.feedrate(scale)
        app_log('CMD', f'Feed Override: {value}%')
        return success_response({"feed_override": value})
    except Exception as e:
        return error_response(f"Feed Override Fail: {e}")

# [2026-02-24] 新增 /v2/override/spindle 端點：設定主軸轉速覆蓋百分比
@app.route('/v2/override/spindle', methods=['POST'])
def v2_override_spindle():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        value = float(data.get('value', 100.0))
        scale = max(0.0, min(value / 100.0, 2.0))  # 限制 0%~200%
        cnc_cmd.spindleoverride(scale)
        app_log('CMD', f'Spindle Override: {value}%')
        return success_response({"spindle_override": value})
    except Exception as e:
        return error_response(f"Spindle Override Fail: {e}")

# [2026-02-24] 新增 /v2/program/step 端點：單節執行（Single Block）
@app.route('/v2/program/step', methods=['POST'])
def v2_program_step():
    """單節執行：每次 Cycle Start 僅執行一行 G-Code"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        cnc_stat.poll()
        if cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine OFF")
        if cnc_stat.task_mode != linuxcnc.MODE_AUTO:
            cnc_cmd.mode(linuxcnc.MODE_AUTO)
            cnc_cmd.wait_complete()
        cnc_cmd.auto(linuxcnc.AUTO_STEP)
        app_log('CMD', 'Single Block Step')
        return success_response("Stepped")
    except Exception as e:
        return error_response(f"Step Fail: {e}")

# [2026-03-04] 新增 Block Delete / Optional Stop 切換端點
@app.route('/v2/program/block_delete', methods=['POST'])
def v2_block_delete():
    """切換 Block Delete 開關"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.get_json(silent=True) or {}
        value = bool(data.get('value', False))
        cnc_cmd.set_block_delete(value)
        app_log('CMD', f'Block Delete: {value}')
        return success_response({"block_delete": value})
    except Exception as e:
        return error_response(f"Block Delete Fail: {e}")

@app.route('/v2/program/optional_stop', methods=['POST'])
def v2_optional_stop():
    """切換 Optional Stop (M01) 開關"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.get_json(silent=True) or {}
        value = bool(data.get('value', False))
        cnc_cmd.set_optional_stop(value)
        app_log('CMD', f'Optional Stop: {value}')
        return success_response({"optional_stop": value})
    except Exception as e:
        return error_response(f"Optional Stop Fail: {e}")

# [2026-03-03] 新增 /v2/tool/table 端點：讀取 LinuxCNC 刀具表
@app.route('/v2/tool/table', methods=['GET'])
def v2_tool_table():
    """回傳完整刀具表（tool_table + tool.tbl 註解）"""
    tools = []
    try:
        # [2026-03-03] 從 INI 讀取刀具表檔名（預設 tool.tbl，可能是 tool_metric.tbl）
        _tool_tbl_name = "tool.tbl"
        try:
            import configparser
            _ini = configparser.ConfigParser(strict=False)
            _ini.read(LINUXCNC_INI_PATH)
            _tool_tbl_name = _ini.get('EMCIO', 'TOOL_TABLE', fallback='tool.tbl')
        except Exception:
            pass
        tool_tbl_path = os.path.join(CONFIG_DIR, _tool_tbl_name)
        tbl_comments = {}
        if os.path.exists(tool_tbl_path):
            with open(tool_tbl_path, 'r', encoding='utf-8', errors='ignore') as f:
                for line in f:
                    line = line.strip()
                    if not line or line.startswith(';'):
                        continue
                    # LinuxCNC tool.tbl 格式: T1 P1 D0.0 Z0.0 ; comment
                    if ';' in line:
                        parts_split = line.split(';', 1)
                        comment = parts_split[1].strip()
                    else:
                        comment = ""
                    # 解析 T 號
                    import re as _re
                    t_match = _re.search(r'T(\d+)', line)
                    if t_match:
                        tbl_comments[int(t_match.group(1))] = comment

        # 從 cnc_stat 讀取刀具表（記憶體值）
        if ensure_cnc_connections():
            cnc_stat.poll()
            for i, entry in enumerate(cnc_stat.tool_table):
                if i == 0:
                    continue  # tool_table[0] 通常是空的
                t_id = int(entry.id) if hasattr(entry, 'id') else i
                if t_id <= 0:
                    continue
                pocket = int(entry.pocket) if hasattr(entry, 'pocket') else i
                tools.append({
                    "ToolNumber": t_id,
                    "Pocket": pocket,
                    "XOffset": round(float(entry.xoffset), 4) if hasattr(entry, 'xoffset') else 0.0,
                    "YOffset": round(float(entry.yoffset), 4) if hasattr(entry, 'yoffset') else 0.0,
                    "ZOffset": round(float(entry.zoffset), 4) if hasattr(entry, 'zoffset') else 0.0,
                    "AOffset": round(float(entry.aoffset), 4) if hasattr(entry, 'aoffset') else 0.0,
                    "BOffset": round(float(entry.boffset), 4) if hasattr(entry, 'boffset') else 0.0,
                    "COffset": round(float(entry.coffset), 4) if hasattr(entry, 'coffset') else 0.0,
                    "UOffset": round(float(entry.uoffset), 4) if hasattr(entry, 'uoffset') else 0.0,
                    "VOffset": round(float(entry.voffset), 4) if hasattr(entry, 'voffset') else 0.0,
                    "WOffset": round(float(entry.woffset), 4) if hasattr(entry, 'woffset') else 0.0,
                    "Diameter": round(float(entry.diameter), 4) if hasattr(entry, 'diameter') else 0.0,
                    "FrontAngle": round(float(entry.frontangle), 4) if hasattr(entry, 'frontangle') else 0.0,
                    "BackAngle": round(float(entry.backangle), 4) if hasattr(entry, 'backangle') else 0.0,
                    "Orientation": int(entry.orientation) if hasattr(entry, 'orientation') else 0,
                    "Remark": tbl_comments.get(t_id, "")
                })
        else:
            # 離線時從 tool.tbl 解析
            if os.path.exists(tool_tbl_path):
                import re as _re
                with open(tool_tbl_path, 'r', encoding='utf-8', errors='ignore') as f:
                    for line in f:
                        line = line.strip()
                        if not line or line.startswith(';'):
                            continue
                        comment = ""
                        if ';' in line:
                            parts_split = line.split(';', 1)
                            comment = parts_split[1].strip()
                            line = parts_split[0]
                        t_match = _re.search(r'T(\d+)', line)
                        p_match = _re.search(r'P(\d+)', line)
                        d_match = _re.search(r'D([-+]?[\d.]+)', line)
                        z_match = _re.search(r'Z([-+]?[\d.]+)', line)
                        if t_match:
                            tools.append({
                                "ToolNumber": int(t_match.group(1)),
                                "Pocket": int(p_match.group(1)) if p_match else 0,
                                "XOffset": 0.0, "YOffset": 0.0,
                                "ZOffset": float(z_match.group(1)) if z_match else 0.0,
                                "AOffset": 0.0, "BOffset": 0.0, "COffset": 0.0,
                                "UOffset": 0.0, "VOffset": 0.0, "WOffset": 0.0,
                                "Diameter": float(d_match.group(1)) if d_match else 0.0,
                                "FrontAngle": 0.0, "BackAngle": 0.0, "Orientation": 0,
                                "Remark": comment
                            })

        return success_response(tools)
    except Exception as e:
        return error_response(f"ToolTable Read Fail: {e}")


# [2026-03-03] 新增 /v2/tool/save 端點：寫入刀具表
@app.route('/v2/tool/save', methods=['POST'])
def v2_tool_save():
    """將前端刀具表寫入 tool.tbl 並重載"""
    try:
        data = request.json or {}
        tools = data.get('tools', [])
        if not tools:
            return error_response("No tool data provided", 400)

        # [2026-03-03] 從 INI 讀取刀具表檔名
        _tool_tbl_name = "tool.tbl"
        try:
            import configparser
            _ini = configparser.ConfigParser(strict=False)
            _ini.read(LINUXCNC_INI_PATH)
            _tool_tbl_name = _ini.get('EMCIO', 'TOOL_TABLE', fallback='tool.tbl')
        except Exception:
            pass
        tool_tbl_path = os.path.join(CONFIG_DIR, _tool_tbl_name)
        lines = []
        # [2026-03-03] 寫入完整 tool.tbl（含全軸 offset + FNT ANG/BAK ANG/ORIENT/Remark）
        for t in tools:
            t_num = int(t.get('ToolNumber', 0))
            pocket = int(t.get('Pocket', 0))
            diameter = float(t.get('Diameter', 0))
            x_offset = float(t.get('XOffset', 0))
            y_offset = float(t.get('YOffset', 0))
            z_offset = float(t.get('ZOffset', 0))
            a_offset = float(t.get('AOffset', 0))
            b_offset = float(t.get('BOffset', 0))
            c_offset = float(t.get('COffset', 0))
            u_offset = float(t.get('UOffset', 0))
            v_offset = float(t.get('VOffset', 0))
            w_offset = float(t.get('WOffset', 0))
            front_angle = float(t.get('FrontAngle', 0))
            back_angle = float(t.get('BackAngle', 0))
            orientation = int(t.get('Orientation', 0))
            remark = t.get('Remark', '')
            # LinuxCNC tool.tbl 格式
            line = (f"T{t_num} P{pocket}"
                    f" X{x_offset:.4f} Y{y_offset:.4f} Z{z_offset:.4f}"
                    f" A{a_offset:.4f} B{b_offset:.4f} C{c_offset:.4f}"
                    f" U{u_offset:.4f} V{v_offset:.4f} W{w_offset:.4f}"
                    f" D{diameter:.4f}"
                    f" I{front_angle:.4f} J{back_angle:.4f} Q{orientation}")
            if remark:
                line += f" ; {remark}"
            lines.append(line)

        with open(tool_tbl_path, 'w', encoding='utf-8') as f:
            f.write('\n'.join(lines) + '\n')

        # 重載刀具表至 LinuxCNC
        if ensure_cnc_connections():
            cnc_cmd.load_tool_table()
            app_log('CMD', f'Tool table saved & reloaded ({len(tools)} tools)')

        return success_response({"saved": len(tools)})
    except Exception as e:
        return error_response(f"ToolTable Save Fail: {e}")


# ==============================================================================
# [2026-03-04] 探測循環端點（Probing Cycle）
# 目前方案：Python 直接送 G38.2 MDI 指令（單次探測）
# TODO: 未來可改為 NGC 副程式方案（9 個 .ngc 檔）
#   - 4 基礎：xplus/xminus/yplus/yminus.ngc（含 fast→retract→slow 兩段式）
#   - 4 外角：corner_nw/ne/sw/se.ngc（內部呼叫基礎副程式，M68 傳回多軸結果）
#   - 1 中心：center.ngc（呼叫 4 基礎副程式，計算中心點）
#   - 後端改用 o<probe_xxx> call [params]，INI 加 SUBROUTINE_PATH
# ==============================================================================

def _probe_send_mdi_and_wait(command, timeout=10):
    """[2026-03-04] 共用 MDI 執行輔助：送出指令並等待完成
    [2026-03-05] 新增：回傳 error 字串（None=成功），檢查 LinuxCNC 錯誤與執行狀態
    [2026-03-06] 改為非阻塞輪詢：避免 wait_complete() 長時間阻塞導致 status 輪詢斷線"""
    cnc_cmd.mdi(command)
    # 非阻塞輪詢：每 0.1s 檢查一次 interp 狀態，直到 IDLE 或超時
    import time
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(0.1)
        cnc_stat.poll()
        # 先檢查 error
        with error_lock:
            if cached_errors:
                last_err = cached_errors[-1].get('Text', '')
                if 'probe' in last_err.lower() or 'tripped' in last_err.lower() or 'aborting' in last_err.lower():
                    return last_err
        if cnc_stat.state == linuxcnc.RCS_ERROR if linuxcnc else False:
            return f"LinuxCNC RCS_ERROR after: {command}"
        # IDLE = 指令執行完畢
        if cnc_stat.interp_state == linuxcnc.INTERP_IDLE:
            return None
    return f"Timeout ({timeout}s) waiting for: {command}"

def _probe_get_result():
    """[2026-03-04] 讀取探測結果：probed_position + probe_tripped"""
    cnc_stat.poll()
    pos = cnc_stat.probed_position
    tripped = bool(cnc_stat.probe_tripped)
    return {
        'tripped': tripped,
        'x': pos[0],
        'y': pos[1],
        'z': pos[2]
    }

def _probe_edge(direction, search_speed, max_xy_dist, max_z_dist,
                xy_clearance, z_clearance, extra_depth, probe_radius=0):
    """[2026-03-04] 單軸邊緣探測
    方向對應：N→Y-  S→Y+  E→X-  W→X+
    流程：Z 下降 → G38.2 探測 → 讀取位置 → 退回 → Z 恢復
    [2026-03-06] 新增 probe_radius：探針球半徑補正（probed_position ± R）
    """
    # 方向映射
    axis_map = {
        'N': ('Y', -(max_xy_dist)),
        'S': ('Y', max_xy_dist),
        'E': ('X', -(max_xy_dist)),
        'W': ('X', max_xy_dist)
    }
    if direction not in axis_map:
        return {'tripped': False, 'error': f'Invalid edge direction: {direction}'}

    axis, dist = axis_map[direction]
    retract = xy_clearance if dist > 0 else -xy_clearance
    z_drop = -(abs(extra_depth) + abs(z_clearance))

    # 1. Z 下降
    err = _probe_send_mdi_and_wait(f"G91 G0 Z{z_drop:.4f}")
    if err:
        _probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0, 'error': f'Z drop failed: {err}'}
    # [2026-03-06] 檢查 probe 訊號：若已觸發則拒絕 G38.2（避免 "Probe is already tripped" 錯誤）
    cnc_stat.poll()
    if bool(getattr(cnc_stat, 'probe_val', 0)):
        _probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        _probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Probe input is already HIGH before G38.2 ({direction}). Check probe wiring or clear obstruction.'}
    # 2. G38.2 探測
    # [2026-03-06] timeout 30→60s：手動模擬需要更多時間等待使用者按 SIM TRIGGER
    err = _probe_send_mdi_and_wait(f"G91 G38.2 {axis}{dist:.4f} F{search_speed:.1f}", timeout=60)
    if err:
        _probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        _probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0, 'error': err}
    result = _probe_get_result()
    if not result['tripped']:
        # 未觸發 → 安全退回
        _probe_send_mdi_and_wait(f"G91 G0 {axis}{-dist:.4f}")
        _probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        _probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Probe not tripped ({direction})'}

    # [2026-03-06] 探針半徑補正：球心座標 → 工件表面座標
    # 探測方向 dist>0 時探針碰到前方表面，表面 = ball_center + R
    # 探測方向 dist<0 時探針碰到後方表面，表面 = ball_center - R
    if probe_radius > 0:
        sign = 1 if dist > 0 else -1
        if axis == 'X':
            result['x'] += sign * probe_radius
        else:
            result['y'] += sign * probe_radius

    # 3. 退回 xy_clearance
    _probe_send_mdi_and_wait(f"G91 G0 {axis}{-retract:.4f}")
    # 4. Z 恢復
    _probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
    # 5. 恢復絕對模式
    _probe_send_mdi_and_wait("G90")

    return result

def _probe_outside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                          xy_clearance, z_clearance, extra_depth, probe_radius=0):
    """[2026-03-04] 雙軸外角探測
    方向對應：NW→X+,Y-  NE→X-,Y-  SW→X+,Y+  SE→X-,Y+
    流程：先探第一軸 → 回起點 → 探第二軸 → 組合結果
    [2026-03-06] 新增 probe_radius：傳遞至 _probe_edge 做半徑補正
    """
    corner_map = {
        'NW': ('W', 'N'),  # X+, Y-
        'NE': ('E', 'N'),  # X-, Y-
        'SW': ('W', 'S'),  # X+, Y+
        'SE': ('E', 'S')   # X-, Y+
    }
    if direction not in corner_map:
        return {'tripped': False, 'error': f'Invalid corner direction: {direction}'}

    dir1, dir2 = corner_map[direction]

    # 記錄起始位置
    cnc_stat.poll()
    start_x = cnc_stat.actual_position[0]
    start_y = cnc_stat.actual_position[1]

    # 第一軸探測
    r1 = _probe_edge(dir1, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r1['tripped']:
        return r1

    # 回到起點
    _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    # 第二軸探測
    r2 = _probe_edge(dir2, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r2['tripped']:
        return r2

    # 回到起點
    _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    # 組合結果：第一軸提供 X（W/E 方向），第二軸提供 Y（N/S 方向）
    return {
        'tripped': True,
        'x': r1['x'],
        'y': r2['y'],
        'z': r1['z']
    }

# [2026-03-04] 雙軸內角探測（方向與 outside_corner 相反）
# Inside NW: 探針在口袋左上角內 → 向左牆(X-)探 + 向後牆(Y+)探
# Inside NE: → 向右牆(X+)探 + 向後牆(Y+)探
# Inside SW: → 向左牆(X-)探 + 向前牆(Y-)探
# Inside SE: → 向右牆(X+)探 + 向前牆(Y-)探
def _probe_inside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                         xy_clearance, z_clearance, extra_depth, probe_radius=0):
    """[2026-03-04] 雙軸內角探測
    方向對應（與 outside_corner 相反）：NW→X-,Y+  NE→X+,Y+  SW→X-,Y-  SE→X+,Y-
    流程：先探第一軸 → 回起點 → 探第二軸 → 組合結果
    [2026-03-06] 新增 probe_radius：傳遞至 _probe_edge 做半徑補正
    """
    inside_map = {
        'NW': ('E', 'S'),  # X-(E), Y+(S)
        'NE': ('W', 'S'),  # X+(W), Y+(S)
        'SW': ('E', 'N'),  # X-(E), Y-(N)
        'SE': ('W', 'N')   # X+(W), Y-(N)
    }
    if direction not in inside_map:
        return {'tripped': False, 'error': f'Invalid inside corner direction: {direction}'}

    dir1, dir2 = inside_map[direction]

    # 記錄起始位置
    cnc_stat.poll()
    start_x = cnc_stat.actual_position[0]
    start_y = cnc_stat.actual_position[1]

    # 第一軸探測（X 方向）
    r1 = _probe_edge(dir1, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r1['tripped']:
        return r1

    # 回到起點
    _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    # 第二軸探測（Y 方向）
    r2 = _probe_edge(dir2, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r2['tripped']:
        return r2

    # 回到起點
    _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    # 組合結果：第一軸提供 X，第二軸提供 Y
    return {
        'tripped': True,
        'x': r1['x'],
        'y': r2['y'],
        'z': r1['z']
    }

def _probe_center(search_speed, max_xy_dist, max_z_dist,
                  xy_clearance, z_clearance, extra_depth, axes='XY', probe_radius=0):
    """[2026-03-04] 中心/口袋探測（從內向外探壁）
    axes: 'X'=僅 X 兩壁, 'Y'=僅 Y 兩壁, 'XY'=全部 4 壁
    [2026-03-06] 新增 probe_radius：傳遞至 _probe_edge；中心點 R 自動抵消，寬度 ±2R 為真實壁距
    """
    cnc_stat.poll()
    start_x = cnc_stat.actual_position[0]
    start_y = cnc_stat.actual_position[1]
    first_z = None

    center_x = start_x
    center_y = start_y
    # [2026-03-04] 初始化寬度變數（僅探測的軸才會覆寫）
    width_x = 0
    width_y = 0

    if axes in ('X', 'XY'):
        # X+ 方向（W→探右）
        r_xp = _probe_edge('W', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_xp['tripped']:
            return r_xp
        if first_z is None:
            first_z = r_xp['z']
        _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

        # X- 方向（E→探左）
        r_xn = _probe_edge('E', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_xn['tripped']:
            return r_xn
        _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

        center_x = (r_xp['x'] + r_xn['x']) / 2.0
        width_x = abs(r_xp['x'] - r_xn['x'])

    if axes in ('Y', 'XY'):
        # Y+ 方向（S→探上）
        r_yp = _probe_edge('S', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_yp['tripped']:
            return r_yp
        if first_z is None:
            first_z = r_yp['z']
        _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

        # Y- 方向（N→探下）
        r_yn = _probe_edge('N', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_yn['tripped']:
            return r_yn
        _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

        center_y = (r_yp['y'] + r_yn['y']) / 2.0
        width_y = abs(r_yp['y'] - r_yn['y'])

    return {
        'tripped': True,
        'x': center_x,
        'y': center_y,
        'z': first_z if first_z is not None else 0,
        'width_x': width_x,
        'width_y': width_y
    }

# [2026-03-04] Boss 凸台探測：從外部向內探測，支援 X/Y/XY 軸選擇
def _probe_boss(search_speed, max_xy_dist, max_z_dist,
                xy_clearance, z_clearance, extra_depth, axes='XY', diameter=0, probe_radius=0):
    """Boss 凸台探測
    探針起始於凸台上方中心附近，依序向指定面外移 → Z 下降 → 向內探測 → 退回
    axes: 'X'=僅 X 兩側, 'Y'=僅 Y 兩側, 'XY'=全部 4 面
    diameter: 近似直徑（>0 時用 diameter/2+xy_clearance 作外移距離）
    [2026-03-06] 新增 probe_radius：探針球半徑補正（中心點 R 抵消，寬度 ±2R）
    """
    cnc_stat.poll()
    start_x = cnc_stat.actual_position[0]
    start_y = cnc_stat.actual_position[1]
    z_drop = -(abs(extra_depth) + abs(z_clearance))

    # [2026-03-04] 外移距離：若有直徑用 diam/2+clearance，否則用 max_xy_dist
    move_dist = (diameter / 2.0 + xy_clearance) if diameter > 0 else max_xy_dist
    probe_travel = move_dist + max_xy_dist  # 探測行程要夠長

    # [2026-03-04] 依 axes 篩選探測面
    all_sides = {
        'xp': ('X',  move_dist, -probe_travel),
        'xn': ('X', -move_dist,  probe_travel),
        'yp': ('Y',  move_dist, -probe_travel),
        'yn': ('Y', -move_dist,  probe_travel),
    }
    if axes == 'X':
        side_keys = ['xp', 'xn']
    elif axes == 'Y':
        side_keys = ['yp', 'yn']
    else:
        side_keys = ['xp', 'xn', 'yp', 'yn']

    touch_points = {}
    for label in side_keys:
        axis, move_out, probe_dist = all_sides[label]
        # 1. 從中心外移（離開凸台範圍）
        _probe_send_mdi_and_wait(f"G91 G0 {axis}{move_out:.4f}")
        # 2. Z 下降至探測深度
        _probe_send_mdi_and_wait(f"G91 G0 Z{z_drop:.4f}")
        # 3. G38.2 向內探測
        _probe_send_mdi_and_wait(
            f"G91 G38.2 {axis}{probe_dist:.4f} F{search_speed:.1f}", timeout=30)
        r = _probe_get_result()
        if not r['tripped']:
            retract = xy_clearance if probe_dist > 0 else -xy_clearance
            _probe_send_mdi_and_wait(f"G91 G0 {axis}{retract:.4f}")
            _probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
            _probe_send_mdi_and_wait("G90")
            _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
            return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                    'error': f'Boss probe not tripped ({label})'}
        # [2026-03-06] 探針半徑補正：probe_dist<0 → 向負方向探測，表面在 ball-R；反之 ball+R
        if probe_radius > 0:
            sign = 1 if probe_dist > 0 else -1
            if axis == 'X':
                r['x'] += sign * probe_radius
            else:
                r['y'] += sign * probe_radius
        touch_points[label] = r
        # 4. 退回 xy_clearance
        retract = xy_clearance if move_out > 0 else -xy_clearance
        _probe_send_mdi_and_wait(f"G91 G0 {axis}{retract:.4f}")
        # 5. Z 恢復
        _probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        # 6. 回到中心起點
        _probe_send_mdi_and_wait("G90")
        _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    # 計算中心 + 實測寬度（僅計算已探測的軸）
    center_x = start_x
    center_y = start_y
    width_x = 0
    width_y = 0
    if 'xp' in touch_points and 'xn' in touch_points:
        center_x = (touch_points['xp']['x'] + touch_points['xn']['x']) / 2.0
        width_x = abs(touch_points['xp']['x'] - touch_points['xn']['x'])
    if 'yp' in touch_points and 'yn' in touch_points:
        center_y = (touch_points['yp']['y'] + touch_points['yn']['y']) / 2.0
        width_y = abs(touch_points['yp']['y'] - touch_points['yn']['y'])

    return {
        'tripped': True,
        'x': center_x,
        'y': center_y,
        'z': touch_points[side_keys[0]]['z'],
        'width_x': width_x,
        'width_y': width_y
    }

# [2026-03-04] Edge Angle：沿邊緣探 2 點計算角度
def _probe_edge_angle(edge, search_speed, max_xy_dist, max_z_dist,
                      xy_clearance, z_clearance, extra_depth, edge_width=0, probe_radius=0):
    """[2026-03-04] Edge Angle 邊角角度探測（3×3 九宮格對齊 PB 版）
    edge: 'NW'/'N'/'NE'/'W'/'CENTER'/'E'/'SW'/'S'/'SE'
    edge_width: 使用者輸入的邊緣寬度（>0 時用作探測間距）
    在邊緣上探 2 點，計算邊緣相對機台軸的角度（度）
    [2026-03-06] 新增 probe_radius：傳遞至 _probe_edge/_probe_center
    """
    import math
    cnc_stat.poll()
    start_x = cnc_stat.actual_position[0]
    start_y = cnc_stat.actual_position[1]

    # [2026-03-04] 沿邊探 2 點的間距：優先使用使用者輸入的 edge_width
    spacing = edge_width if edge_width > 0 else xy_clearance * 3.0

    # [2026-03-04] 九宮格方向配置（對齊 PB 版）
    # probe_dir: 探針接觸方向, move_axis: 沿邊移動軸, move_dist: 第 2 點偏移
    edge_config = {
        # 上排：探測北面（頂邊），向下探
        'NW': {'probe_dir': 'N', 'move_axis': 'X', 'move_dist':  spacing},
        'N':  {'probe_dir': 'N', 'move_axis': 'X', 'move_dist':  spacing},
        'NE': {'probe_dir': 'N', 'move_axis': 'X', 'move_dist': -spacing},
        # 中排：探測左右面
        'W':  {'probe_dir': 'W', 'move_axis': 'Y', 'move_dist':  spacing},
        'E':  {'probe_dir': 'E', 'move_axis': 'Y', 'move_dist':  spacing},
        # 下排：探測南面（底邊），向上探
        'SW': {'probe_dir': 'S', 'move_axis': 'X', 'move_dist':  spacing},
        'S':  {'probe_dir': 'S', 'move_axis': 'X', 'move_dist':  spacing},
        'SE': {'probe_dir': 'S', 'move_axis': 'X', 'move_dist': -spacing},
    }
    # [2026-03-04] CENTER：4 邊各探 1 點，計算中心（不測角度）
    if edge == 'CENTER':
        result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                               xy_clearance, z_clearance, extra_depth, 'XY', probe_radius)
        result['angle'] = 0
        result['edge_width'] = 0
        return result

    cfg = edge_config.get(edge)
    if not cfg:
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Invalid edge_angle edge: {edge}'}

    # 第 1 點探測
    r1 = _probe_edge(cfg['probe_dir'], search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r1['tripped']:
        return r1
    _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    # 沿邊移動到第 2 點位置
    _probe_send_mdi_and_wait(f"G91 G0 {cfg['move_axis']}{cfg['move_dist']:.4f}")
    _probe_send_mdi_and_wait("G90")

    # 第 2 點探測
    r2 = _probe_edge(cfg['probe_dir'], search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r2['tripped']:
        # 回到起點
        _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
        return r2
    _probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    # 計算角度
    dx = r2['x'] - r1['x']
    dy = r2['y'] - r1['y']
    if cfg['move_axis'] == 'X':
        # 沿 X 移動，探 Y 方向 → angle = atan2(dy, dx)
        angle_rad = math.atan2(dy, dx)
    else:
        # 沿 Y 移動，探 X 方向 → angle = atan2(dx, dy)
        angle_rad = math.atan2(dx, dy)
    angle_deg = math.degrees(angle_rad)

    # Edge width = 兩觸發點在探測方向的距離
    if cfg['probe_dir'] in ('N', 'S'):
        edge_width = abs(r2['y'] - r1['y'])
    else:
        edge_width = abs(r2['x'] - r1['x'])

    return {
        'tripped': True,
        'x': (r1['x'] + r2['x']) / 2.0,
        'y': (r1['y'] + r2['y']) / 2.0,
        'z': r1['z'],
        'angle': angle_deg,
        'edge_width': edge_width
    }

# [2026-03-04] Calibrate 校正：在已知直徑環/邊上探測，計算探針偏移
def _probe_calibrate(cal_type, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, cal_width=0, probe_radius=0):
    """Probe Calibrate 校正探測
    cal_type: 'xy_turret' / 'x_edge' / 'x_bore'
    cal_width: 校正環已知直徑/寬度
    """
    # [2026-03-04] 對齊 PB 版校正類型
    if cal_type in ('ring_inside', 'xy_turret', 'x_bore'):
        # 環孔內探 / XY Turret / X Bore：在已知環內探 4 面（center）
        result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                               xy_clearance, z_clearance, extra_depth, 'XY', probe_radius)
        return result
    elif cal_type in ('ring_outside',):
        # 環外探：在已知環外面探（boss XY）
        result = _probe_boss(search_speed, max_xy_dist, max_z_dist,
                             xy_clearance, z_clearance, extra_depth, 'XY', cal_width, probe_radius)
        return result
    elif cal_type in ('square_inside',):
        # 方孔內探：在已知方孔內探 4 壁（center）
        result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                               xy_clearance, z_clearance, extra_depth, 'XY', probe_radius)
        return result
    elif cal_type in ('square_outside',):
        # 方外探：在已知方塊外面探（boss XY）
        result = _probe_boss(search_speed, max_xy_dist, max_z_dist,
                             xy_clearance, z_clearance, extra_depth, 'XY', cal_width, probe_radius)
        return result
    elif cal_type in ('avg_xy', 'x_error', 'y_error'):
        # 誤差計算：探 4 面，依 cal_type 選取 X/Y/AVG 誤差
        axes = 'XY'
        if cal_type == 'x_error': axes = 'X'
        elif cal_type == 'y_error': axes = 'Y'
        result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                               xy_clearance, z_clearance, extra_depth, axes, probe_radius)
        return result
    elif cal_type == 'x_edge':
        result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                               xy_clearance, z_clearance, extra_depth, 'X', probe_radius)
        return result
    else:
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Invalid cal_type: {cal_type}'}

@app.route('/v2/probe/run', methods=['POST'])
def v2_probe_run():
    """[2026-03-04] 探測循環端點：edge/corner/center/boss/pocket/ridge/valley/edge_angle/calibrate"""
    if not ensure_cnc_connections():
        return error_response("No connection")
    try:
        cnc_stat.poll()
        if cnc_stat.task_state == linuxcnc.STATE_ESTOP:
            return error_response("ESTOP Active", 400)
        if cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine OFF", 400)

        data = request.json or {}
        probe_type = data.get('probe_type', '')
        direction = data.get('direction', '').upper()
        search_speed = float(data.get('search_speed', 50.0))
        max_xy_dist = float(data.get('max_xy_distance', 20.0))
        max_z_dist = float(data.get('max_z_distance', 20.0))
        xy_clearance = float(data.get('xy_clearance', 5.0))
        z_clearance = float(data.get('z_clearance', 5.0))
        extra_depth = float(data.get('extra_depth', 2.0))
        # [2026-03-04] Boss/Pocket 近似直徑（用於外移距離計算）
        diameter = float(data.get('diameter', 0))
        # [2026-03-04] Boss/Pocket 特徵中心相對當前位置的近似偏移
        offset_x = float(data.get('offset_x', 0))
        offset_y = float(data.get('offset_y', 0))
        # [2026-03-06] 探針刀號（用於自動 G43 + 讀取探針直徑做半徑補正）
        probe_tool = int(data.get('probe_tool', 0))

        # 切換 MDI 模式
        if cnc_stat.task_mode != linuxcnc.MODE_MDI:
            cnc_cmd.mode(linuxcnc.MODE_MDI)
            cnc_cmd.wait_complete()

        # [2026-03-06] 自動啟用探針刀長補正（G43），確保 G10 L20 寫入 WCS 時自動扣除刀長
        probe_radius = 0.0
        if probe_tool > 0:
            g43_err = _probe_send_mdi_and_wait(f"G43 H{probe_tool}", timeout=5)
            if g43_err:
                return error_response(f"G43 H{probe_tool} failed: {g43_err}")
            # [2026-03-06] 從刀具表讀取探針直徑 → 半徑（用於 XY 探測補正）
            try:
                cnc_stat.poll()
                if probe_tool < len(cnc_stat.tool_table):
                    probe_diameter = float(cnc_stat.tool_table[probe_tool].diameter)
                    probe_radius = abs(probe_diameter) / 2.0
                    app_log('CMD', f'Probe tool #{probe_tool}: diameter={probe_diameter:.4f} radius={probe_radius:.4f}')
            except Exception as e:
                app_log('WARN', f'Failed to read probe tool diameter: {e}')

        result = None
        if probe_type == 'edge':
            result = _probe_edge(direction, search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, probe_radius)
        elif probe_type == 'outside_corner':
            result = _probe_outside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                                           xy_clearance, z_clearance, extra_depth, probe_radius)
        # [2026-03-04] Inside Corners 支援
        elif probe_type == 'inside_corner':
            result = _probe_inside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                                          xy_clearance, z_clearance, extra_depth, probe_radius)
        elif probe_type == 'inside_edge':
            # 內角邊緣：方向反轉（探針在內側向牆壁探測）
            inside_edge_map = {'N': 'S', 'S': 'N', 'E': 'W', 'W': 'E'}
            mapped_dir = inside_edge_map.get(direction, direction)
            result = _probe_edge(mapped_dir, search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, probe_radius)
        elif probe_type == 'center':
            result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                                   xy_clearance, z_clearance, extra_depth, 'XY', probe_radius)
        # [2026-03-04] Boss 凸台：從外部向內探測（X/Y/XY）
        elif probe_type in ('boss_x', 'boss_y', 'boss_xy', 'boss'):
            axes = 'XY'
            if probe_type == 'boss_x': axes = 'X'
            elif probe_type == 'boss_y': axes = 'Y'
            # [2026-03-04] 先移至特徵近似中心（使用者輸入偏移）
            if offset_x != 0 or offset_y != 0:
                _probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                _probe_send_mdi_and_wait("G90")
            result = _probe_boss(search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, axes, diameter, probe_radius)
        # [2026-03-04] Pocket 口袋：從內部向外探壁（X/Y/XY）
        elif probe_type in ('pocket_x', 'pocket_y', 'pocket_xy', 'pocket'):
            axes = 'XY'
            if probe_type == 'pocket_x': axes = 'X'
            elif probe_type == 'pocket_y': axes = 'Y'
            # [2026-03-04] 先移至特徵近似中心
            if offset_x != 0 or offset_y != 0:
                _probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                _probe_send_mdi_and_wait("G90")
            result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                                   xy_clearance, z_clearance, extra_depth, axes, probe_radius)
        # [2026-03-04] Ridge 脊：從外部向內探（同 Boss）
        elif probe_type in ('ridge_x', 'ridge_y', 'ridge_xy'):
            axes = 'XY'
            if probe_type == 'ridge_x': axes = 'X'
            elif probe_type == 'ridge_y': axes = 'Y'
            # [2026-03-04] 先移至特徵近似中心
            if offset_x != 0 or offset_y != 0:
                _probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                _probe_send_mdi_and_wait("G90")
            result = _probe_boss(search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, axes, diameter, probe_radius)
        # [2026-03-04] Valley 谷：從內部向外探（同 Pocket/Center）
        elif probe_type in ('valley_x', 'valley_y', 'valley_xy'):
            axes = 'XY'
            if probe_type == 'valley_x': axes = 'X'
            elif probe_type == 'valley_y': axes = 'Y'
            # [2026-03-04] 先移至特徵近似中心
            if offset_x != 0 or offset_y != 0:
                _probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                _probe_send_mdi_and_wait("G90")
            result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                                   xy_clearance, z_clearance, extra_depth, axes, probe_radius)
        # [2026-03-04] Edge Angle 邊角角度
        elif probe_type == 'edge_angle':
            edge_w = float(data.get('edge_width', 0))
            result = _probe_edge_angle(direction, search_speed, max_xy_dist, max_z_dist,
                                       xy_clearance, z_clearance, extra_depth, edge_w, probe_radius)
        # [2026-03-04] Calibrate 校正（對齊 PB 版：ring/square inside/outside + avg/x/y error）
        elif probe_type in ('cal_ring_inside', 'cal_ring_outside',
                            'cal_square_inside', 'cal_square_outside',
                            'cal_avg_xy', 'cal_x_error', 'cal_y_error',
                            'cal_xy_turret', 'cal_x_edge', 'cal_x_bore'):
            cal_type = probe_type.replace('cal_', '')
            cal_width = float(data.get('calibration_width', 0))
            result = _probe_calibrate(cal_type, search_speed, max_xy_dist, max_z_dist,
                                      xy_clearance, z_clearance, extra_depth, cal_width, probe_radius)
        else:
            return error_response(f"Invalid probe_type: {probe_type}", 400)

        error_msg = result.get('error', '')
        if error_msg:
            app_log('WARN', f'Probe {probe_type}/{direction}: {error_msg}')
        else:
            app_log('CMD', f'Probe {probe_type}/{direction}: '
                    f'X={result["x"]:.4f} Y={result["y"]:.4f} Z={result["z"]:.4f}')

        # [2026-03-06] 探測成功且非僅顯示模式 → 後端直接寫入 WCS（避免前端另送 MDI 時序衝突）
        wcs = data.get('wcs', '')
        probe_only = bool(data.get('probe_position_only', False))
        if result.get('tripped') and not error_msg and wcs and not probe_only:
            wcs_map = {'G54':1,'G55':2,'G56':3,'G57':4,'G58':5,'G59':6,
                       'G59.1':7,'G59.2':8,'G59.3':9}
            p_idx = wcs_map.get(wcs, 0)
            if p_idx > 0:
                g10_cmd = f"G10 L20 P{p_idx} X{result['x']:.4f} Y{result['y']:.4f} Z{result['z']:.4f}"
                wcs_err = _probe_send_mdi_and_wait(g10_cmd, timeout=5)
                if wcs_err:
                    app_log('WARN', f'Probe WCS write failed: {wcs_err}')
                else:
                    app_log('CMD', f'Probe → {wcs}: {g10_cmd}')

        return success_response({
            'Tripped': result.get('tripped', False),
            'X': result.get('x', 0),
            'Y': result.get('y', 0),
            'Z': result.get('z', 0),
            'Error': error_msg,
            'Angle': result.get('angle', 0),
            'EdgeWidth': result.get('edge_width', 0),
            'WidthX': result.get('width_x', 0),
            'WidthY': result.get('width_y', 0)
        })
    except Exception as e:
        app_log('ERROR', f'Probe Fail: {e}')
        return error_response(f"Probe Fail: {e}")


# ==============================================================================
# [2026-03-05] 通用 HAL Pin 設定端點
# 前端用於動態設定 HAL 參數（如探針模擬觸發位置）
# ==============================================================================
@app.route('/v2/hal/setp', methods=['POST'])
def v2_hal_setp():
    """[2026-03-05] 通用 halcmd setp：設定 HAL signal 值"""
    try:
        data = request.json or {}
        signal_name = data.get('signal', '').strip()
        value = data.get('value')

        if not signal_name or value is None:
            return error_response("Missing 'signal' or 'value'", 400)

        # [2026-03-06] 安全檢查：允許 sim-probe- 和 probe-in（手動模擬觸發）
        allowed_prefixes = ('sim-probe-', 'probe-in')
        if not any(signal_name.startswith(p) for p in allowed_prefixes):
            return error_response(f"Signal '{signal_name}' not allowed", 403)

        result = subprocess.run(
            ['halcmd', 'sets', signal_name, str(value)],
            capture_output=True, text=True, timeout=5
        )

        if result.returncode != 0:
            return error_response(f"halcmd failed: {result.stderr.strip()}", 500)

        app_log('CMD', f'HAL setp: {signal_name} = {value}')
        return success_response({'signal': signal_name, 'value': value})
    except Exception as e:
        return error_response(f"HAL setp failed: {e}")


if __name__ == '__main__':
    print("[INIT] Server starting...", flush=True)
    start_linuxcnc_process()
    port = SETTINGS['PORT']
    print(f"[START] Server running on port {port}")
    # [2026-03-05] threaded=True：允許多請求並行，避免探測阻塞 status 輪詢
    app.run(host='0.0.0.0', port=port, debug=False, use_reloader=False, threaded=True)
