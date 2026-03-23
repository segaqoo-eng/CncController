# [2026-03-12] 共用模組：全域變數 + helpers + 路徑常數 + SETTINGS
# 從 server.py 拆分，供所有 Blueprint 模組 import

import os
import re
import time
import json
import logging
import threading
import collections
import configparser
import subprocess
from flask import jsonify, request

# ==============================================================================
# 1. 全域路徑配置
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

NC_FILES_DIR = os.path.join(USER_HOME, "linuxcnc/nc_files")

os.environ['LINUXCNC_INI'] = LINUXCNC_INI_PATH
os.environ['LINUXCNC_CONFIG_DIR'] = CONFIG_DIR

# 確保 NC Files 目錄存在
if not os.path.exists(NC_FILES_DIR):
    try:
        os.makedirs(NC_FILES_DIR)
    except Exception as e:
        print(f"[ERR] Failed to create NC dir: {e}")

# ==============================================================================
# 2. SETTINGS 設定
# ==============================================================================

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

# ==============================================================================
# 3. LinuxCNC 模組載入
# ==============================================================================

try:
    import linuxcnc
except ImportError:
    print("[WARN] 'linuxcnc' module not found. Running in SIMULATION mode.")
    linuxcnc = None

# ==============================================================================
# 4. 全域 NML 連線
# ==============================================================================

cnc_cmd = None
cnc_stat = None
cached_errors = collections.deque(maxlen=SETTINGS['ERROR_CACHE_SIZE'])
error_lock = threading.Lock()

# [2026-02-25] in-memory WCS offset cache
_wcs_cache = {}

# [2026-03-13] 狀態快取：背景 Thread 持續更新，API 直接讀取
_status_cache_lock = threading.Lock()
_status_cache_fast = {}   # 高頻快取（20ms）：座標/進給/主軸/狀態
_status_cache_slow = {}   # 低頻快取（1s）：Servo IO / IO Status / 主軸 Encoder
_status_cache_fast_ts = 0.0  # 高頻快取最後更新時間（time.time()）
STATUS_CACHE_STALE_SEC = 2.0  # 快取超過此秒數視為過期（NML 斷線）

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
    except Exception:
        print(f"[NML] Connection Lost. Reconnecting...", flush=True)
        cnc_stat = None
        cnc_cmd = None
        return False

# ==============================================================================
# 5. 共用 Helper 函式
# ==============================================================================

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

def _update_wcs_cache_from_g10(command):
    """解析 G10 L2 P<n> ... 指令，並更新 _wcs_cache"""
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

# ==============================================================================
# 6. HAL / INI / .var 工具函式
# ==============================================================================

def _ini_value(section, key):
    """[2026-03-09] 從 INI 讀取指定 section/key 的值"""
    try:
        ini = configparser.ConfigParser(strict=False)
        ini.read(LINUXCNC_INI_PATH, encoding='utf-8')
        return ini.get(section, key, fallback=None)
    except Exception:
        return None

def _read_hal_pin(pin_name):
    """[2026-03-09] 讀取單一 HAL pin 值（透過 halcmd getp）"""
    try:
        res = subprocess.run(
            ['halcmd', 'getp', pin_name],
            capture_output=True, text=True, timeout=0.3
        )
        if res.returncode == 0:
            val = res.stdout.strip()
            if val.upper() in ('TRUE', '1'):
                return True
            elif val.upper() in ('FALSE', '0'):
                return False
            try:
                return float(val)
            except ValueError:
                return val
    except Exception:
        pass
    return None

def _read_hal_pins_batch(pin_names, prefix='motion.digital'):
    """[2026-03-09] 批次讀取多個 HAL pin（單次 halcmd show pin 減少 subprocess 開銷）"""
    result = {}
    try:
        res = subprocess.run(
            ['halcmd', '-s', 'show', 'pin', prefix],
            capture_output=True, text=True, timeout=0.5
        )
        if res.returncode == 0:
            for line in res.stdout.splitlines():
                for pin_name in pin_names:
                    if pin_name in line:
                        parts = line.split()
                        for i, p in enumerate(parts):
                            if pin_name in p:
                                val_str = parts[i - 1] if i > 0 else '0'
                                result[pin_name] = val_str.upper() in ('TRUE', '1')
                                break
    except Exception:
        pass
    return result

def _read_var_params(param_ids):
    """[2026-03-09] 從 .var 檔讀取指定參數（#id → value）"""
    params = {}
    try:
        param_file = "linuxcnc.var"
        try:
            with open(LINUXCNC_INI_PATH, 'r', encoding='utf-8', errors='ignore') as f:
                for line in f:
                    if line.strip().upper().startswith("PARAMETER_FILE"):
                        raw = line.split('=')[1].strip().split('#')[0].strip()
                        if raw:
                            param_file = raw
                        break
        except:
            pass
        var_path = os.path.join(CONFIG_DIR, param_file)
        if os.path.exists(var_path):
            with open(var_path, 'r', encoding='utf-8', errors='ignore') as f:
                for line in f:
                    parts = line.strip().split()
                    if len(parts) >= 2:
                        try:
                            pid = int(parts[0])
                            if pid in param_ids:
                                params[pid] = float(parts[1])
                        except ValueError:
                            pass
    except Exception:
        pass
    return params

# ==============================================================================
# 7. 探測 / ATC 共用 MDI 輔助
# ==============================================================================

def probe_send_mdi_and_wait(command, timeout=10):
    """[2026-03-04] 共用 MDI 執行輔助：送出指令並等待完成
    [2026-03-06] 改為非阻塞輪詢：避免 wait_complete() 長時間阻塞導致 status 輪詢斷線"""
    cnc_cmd.mdi(command)
    deadline = time.time() + timeout
    while time.time() < deadline:
        time.sleep(0.1)
        cnc_stat.poll()
        with error_lock:
            if cached_errors:
                last_err = cached_errors[-1].get('Text', '')
                if 'probe' in last_err.lower() or 'tripped' in last_err.lower() or 'aborting' in last_err.lower():
                    return last_err
        if cnc_stat.state == linuxcnc.RCS_ERROR if linuxcnc else False:
            return f"LinuxCNC RCS_ERROR after: {command}"
        if cnc_stat.interp_state == linuxcnc.INTERP_IDLE:
            return None
    return f"Timeout ({timeout}s) waiting for: {command}"

def probe_get_result():
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
