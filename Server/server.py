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
            'probe_basic_postgui.hal': data_lower.get('postguicontent')
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

        # 讀取目前 Active WCS (1=G54, 2=G55, ... 6=G59)
        active_wcs = "G54"
        try:
            idx = cnc_stat.g5x_index
            if 1 <= idx <= 6:
                active_wcs = f"G{53 + idx}"
        except:
            pass

        return success_response({
            "Connected": True,
            "Task_State": t_state,
            "Interp_State": i_state,
            "Position": pos_dict,
            "DTG": dtg_dict,
            "Feedrate": feed,
            "Spindle_Speed": spindle_speed,
            "File": filename,
            "Servo_IO": servo_io_data,
            "Active_WCS": active_wcs
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


def read_work_offsets():
    """從 LinuxCNC Parameter File (.var) 讀取 G54–G59 六組 WCS 座標值"""
    param_bases = {
        'G54': 5221, 'G55': 5241, 'G56': 5261,
        'G57': 5281, 'G58': 5301, 'G59': 5321
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

    return offsets


@app.route('/v2/offsets', methods=['GET'])
def v2_offsets():
    """回傳 G54–G59 所有工件座標系偏移值 + 目前 Active WCS"""
    active_wcs = "G54"
    try:
        if ensure_cnc_connections():
            cnc_stat.poll()
            idx = cnc_stat.g5x_index
            if 1 <= idx <= 6:
                active_wcs = f"G{53 + idx}"
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
            if hasattr(cnc_stat, 'joint') and len(cnc_stat.joint) > axis:
                is_homed = (cnc_stat.joint[axis].homed == 1)
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
        cnc_cmd.home(axis)
        app_log('CMD', f'Home axis={axis}')
        return success_response()
    except Exception as e:
        return error_response(f"Home Fail: {e}")

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
        app_log('CMD', f"MDI: {command}")
        return success_response({"command": command})
    except Exception as e:
        return error_response(f"MDI Fail: {e}")

if __name__ == '__main__':
    print("[INIT] Server starting...", flush=True)
    start_linuxcnc_process() 
    port = SETTINGS['PORT']
    print(f"[START] Server running on port {port}")
    app.run(host='0.0.0.0', port=port, debug=False, use_reloader=False)
