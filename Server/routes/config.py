# [2026-03-12] 從 server.py 拆分：設定部署（scan/update/restart）
# [2026-03-12] 路由統一：/api/* → /v2/config/*

import os
import json
import threading
import subprocess
from flask import Blueprint, request, jsonify
from shared import (
    CONFIG_DIR, BASE_DIR, SCAN_SCRIPT, SCAN_OUTPUT_JSON,
    success_response, error_response
)

config_bp = Blueprint('config', __name__)


# [2026-03-12] /api/ethercat/scan → /v2/config/scan
@config_bp.route('/v2/config/scan', methods=['POST', 'GET'])
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


# [2026-03-12] /api/config/update → /v2/config/update
@config_bp.route('/v2/config/update', methods=['POST'])
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
            'sim_probe.hal': data_lower.get('simprobehalcontent'),
            'sim_atc.hal': data_lower.get('simatchalcontent')
        }

        updated = []
        for name, content in files_map.items():
            if content:
                path = os.path.join(CONFIG_DIR, name)
                with open(path, 'w', encoding='utf-8') as f:
                    f.write(content.replace('\r\n', '\n'))
                updated.append(path)

        ngc_files = data_lower.get('ngcfiles')
        if ngc_files and isinstance(ngc_files, dict):
            macros_dir = os.path.join(CONFIG_DIR, 'macros_metric_sim')
            if not os.path.exists(macros_dir):
                os.makedirs(macros_dir)
            for ngc_name, ngc_content in ngc_files.items():
                if ngc_content:
                    ngc_path = os.path.join(macros_dir, ngc_name)
                    with open(ngc_path, 'w', encoding='utf-8') as f:
                        f.write(ngc_content.replace('\r\n', '\n'))
                    updated.append(ngc_path)

        dependencies = {
            "tool_metric.tbl": "T1 P1 D10.0 ; Default Tool\n",
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


# [2026-03-12] /api/machine/restart → /v2/config/restart
@config_bp.route('/v2/config/restart', methods=['POST'])
def api_machine_restart():
    from server import perform_hard_restart
    try:
        t = threading.Thread(target=perform_hard_restart)
        t.start()
        return success_response({"message": "Server & Machine Restarting..."})
    except Exception as e:
        return error_response(f"Restart API failed: {e}")
