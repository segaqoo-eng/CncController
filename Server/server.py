# [2026-03-12] server.py — 主入口（精簡版）
# 路由已拆分至 routes/ 目錄，共用邏輯在 shared.py

import os
import sys
import time
import signal
import threading
import subprocess
import shutil
from flask import Flask, request
from flask_cors import CORS

import shared
from shared import (
    USER_HOME, BASE_DIR, CONFIG_DIR, LINUXCNC_INI_PATH,
    SETTINGS, app_log,
    cached_errors, error_lock, linuxcnc
)

app = Flask(__name__)
CORS(app)

import logging
log = logging.getLogger('werkzeug')
log.setLevel(logging.ERROR)

@app.before_request
def before_request():
    if SETTINGS['SHOW_API_DEBUG'] and request.path != '/v2/status':
        app_log('API', f"REQ {request.method} {request.path}")

# ==============================================================================
# LinuxCNC 進程管理
# ==============================================================================

def is_linuxcnc_alive():
    try:
        subprocess.check_call(["pgrep", "linuxcnc"], stdout=subprocess.DEVNULL)
        return True
    except:
        return False


def execute_user_cleanup_sequence():
    """[V15 客製化指令版] 完全依照使用者指定的順序執行 Shell 指令"""
    print("[CLEANUP] Executing User Custom Sequence...", flush=True)
    commands = [
        "pkill -9 -x rtapi_app",
        "pkill -9 -x milltask",
        "pkill -9 -x qtvcp",
        "pkill -9 -x probe_basic",
        "pkill -9 -x linuxcnc",
        "pkill -9 -x linuxcncsvr",
        "pkill -9 -f 'python3 /usr/bin/linuxcnc'",
        "pkill -9 -f 'python3 /usr/bin/probe_basic'",
        "halrun -U",
        "ipcs -m | grep linuxcnc | awk '{print $2}' | xargs -r ipcrm -m",
        "rm -f /tmp/linuxcnc.lock /tmp/linuxcnc.stat"
    ]
    for cmd in commands:
        try:
            subprocess.run(cmd, shell=True, stderr=subprocess.DEVNULL)
        except Exception as e:
            print(f"[WARN] Command failed: {cmd} -> {e}")
    print("[CLEANUP] User Sequence Complete.", flush=True)


def ensure_var_file_health():
    """[終極修正版] 解決 KeyError: -1"""
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
    """啟動 LinuxCNC"""
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
    """執行硬重啟流程"""
    print("[SERVER] Hard Restart Requested via API...", flush=True)
    execute_user_cleanup_sequence()
    print("[SERVER] Triggering: sudo systemctl restart cnc-server.service", flush=True)
    try:
        subprocess.Popen("sudo systemctl restart cnc-server.service", shell=True)
    except Exception as e:
        print(f"[ERR] Systemctl call failed: {e}", flush=True)
    print("[SERVER] Exiting with code 42...", flush=True)
    time.sleep(1)
    os._exit(42)


# ==============================================================================
# Error Sniffer 背景執行緒
# ==============================================================================

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
# Blueprint 註冊
# ==============================================================================

from routes import register_blueprints
register_blueprints(app)


# ==============================================================================
# 啟動
# ==============================================================================

if __name__ == '__main__':
    print(f"[INIT] User Home: {USER_HOME}")
    print(f"[INIT] Config Dir: {CONFIG_DIR}")
    print(f"[INIT] INI Path:   {LINUXCNC_INI_PATH}")
    print("[INIT] Server starting...", flush=True)
    start_linuxcnc_process()
    port = SETTINGS['PORT']
    print(f"[START] Server running on port {port}")
    app.run(host='0.0.0.0', port=port, debug=False, use_reloader=False, threaded=True)
