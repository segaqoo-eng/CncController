# [2026-03-12] 從 server.py 拆分：刀具表 + 刀具壽命

import os
import re
import time
import json
import threading
import configparser
from flask import Blueprint, request
import shared
from shared import (
    CONFIG_DIR, LINUXCNC_INI_PATH,
    success_response, error_response, app_log,
    ensure_cnc_connections, linuxcnc
)

tool_bp = Blueprint('tool', __name__)


@tool_bp.route('/v2/tool/table', methods=['GET'])
def v2_tool_table():
    """回傳完整刀具表（tool_table + tool.tbl 註解）"""
    tools = []
    try:
        _tool_tbl_name = "tool.tbl"
        try:
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
                    if ';' in line:
                        parts_split = line.split(';', 1)
                        comment = parts_split[1].strip()
                    else:
                        comment = ""
                    t_match = re.search(r'T(\d+)', line)
                    if t_match:
                        tbl_comments[int(t_match.group(1))] = comment

        if ensure_cnc_connections():
            shared.cnc_stat.poll()
            for i, entry in enumerate(shared.cnc_stat.tool_table):
                if i == 0:
                    continue
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
            if os.path.exists(tool_tbl_path):
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
                        t_match = re.search(r'T(\d+)', line)
                        p_match = re.search(r'P(\d+)', line)
                        d_match = re.search(r'D([-+]?[\d.]+)', line)
                        z_match = re.search(r'Z([-+]?[\d.]+)', line)
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


@tool_bp.route('/v2/tool/save', methods=['POST'])
def v2_tool_save():
    """將前端刀具表寫入 tool.tbl 並重載"""
    try:
        data = request.json or {}
        tools = data.get('tools', [])
        if not tools:
            return error_response("No tool data provided", 400)

        _tool_tbl_name = "tool.tbl"
        try:
            _ini = configparser.ConfigParser(strict=False)
            _ini.read(LINUXCNC_INI_PATH)
            _tool_tbl_name = _ini.get('EMCIO', 'TOOL_TABLE', fallback='tool.tbl')
        except Exception:
            pass
        tool_tbl_path = os.path.join(CONFIG_DIR, _tool_tbl_name)
        lines = []
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

        if ensure_cnc_connections():
            shared.cnc_cmd.load_tool_table()
            app_log('CMD', f'Tool table saved & reloaded ({len(tools)} tools)')

        return success_response({"saved": len(tools)})
    except Exception as e:
        return error_response(f"ToolTable Save Fail: {e}")


# ==============================================================================
# 刀具壽命管理（背景 thread 追蹤）
# ==============================================================================
TOOL_LIFE_PATH = os.path.join(CONFIG_DIR, 'tool_life.json')
_tool_life_data = {}
_tool_life_lock = threading.Lock()
_tool_life_last_save = 0
_tool_life_last_tool = 0


def _load_tool_life():
    global _tool_life_data
    try:
        if os.path.exists(TOOL_LIFE_PATH):
            with open(TOOL_LIFE_PATH, 'r', encoding='utf-8') as f:
                _tool_life_data = json.load(f)
            app_log('CMD', f'Tool life loaded: {len(_tool_life_data)} tools')
    except Exception as e:
        app_log('ERROR', f'Tool life load failed: {e}')
        _tool_life_data = {}


def _save_tool_life():
    global _tool_life_last_save
    try:
        with open(TOOL_LIFE_PATH, 'w', encoding='utf-8') as f:
            json.dump(_tool_life_data, f, indent=2)
        _tool_life_last_save = time.time()
    except Exception as e:
        app_log('ERROR', f'Tool life save failed: {e}')


def _tool_life_tracker():
    global _tool_life_last_tool, _tool_life_last_save
    _load_tool_life()

    while True:
        try:
            time.sleep(1)
            if shared.cnc_stat is None:
                continue
            try:
                shared.cnc_stat.poll()
            except:
                continue

            current_tool = 0
            spindle_dir = 0
            try:
                current_tool = int(shared.cnc_stat.tool_in_spindle)
            except: pass
            try:
                if hasattr(shared.cnc_stat, 'spindle') and len(shared.cnc_stat.spindle) > 0:
                    spindle_dir = int(shared.cnc_stat.spindle[0].get('direction', 0))
            except: pass

            with _tool_life_lock:
                if _tool_life_last_tool > 0 and current_tool > 0 and current_tool != _tool_life_last_tool:
                    old_key = str(_tool_life_last_tool)
                    if old_key not in _tool_life_data:
                        _tool_life_data[old_key] = {"cutting_time_sec": 0, "change_count": 0, "max_time_sec": 0, "max_count": 0}
                    _tool_life_data[old_key]["change_count"] += 1
                    app_log('CMD', f'Tool life: T{_tool_life_last_tool} change_count +1 = {_tool_life_data[old_key]["change_count"]}')
                    _save_tool_life()

                _tool_life_last_tool = current_tool

                if current_tool > 0 and spindle_dir != 0:
                    key = str(current_tool)
                    if key not in _tool_life_data:
                        _tool_life_data[key] = {"cutting_time_sec": 0, "change_count": 0, "max_time_sec": 0, "max_count": 0}
                    _tool_life_data[key]["cutting_time_sec"] += 1

                if time.time() - _tool_life_last_save >= 30:
                    _save_tool_life()

        except Exception as e:
            app_log('ERROR', f'Tool life tracker error: {e}')
            time.sleep(5)


# 啟動背景追蹤 thread
_tool_life_thread = threading.Thread(target=_tool_life_tracker, daemon=True)
_tool_life_thread.start()


@tool_bp.route('/v2/tool/life', methods=['GET'])
def v2_tool_life():
    try:
        with _tool_life_lock:
            return success_response(_tool_life_data)
    except Exception as e:
        return error_response(f"Tool life read failed: {e}")


@tool_bp.route('/v2/tool/life/config', methods=['POST'])
def v2_tool_life_config():
    try:
        data = request.json or {}
        tool_num = str(data.get('tool_number', 0))
        max_time = data.get('max_time_sec', 0)
        max_count = data.get('max_count', 0)
        if tool_num == '0':
            return error_response("Missing tool_number", 400)
        with _tool_life_lock:
            if tool_num not in _tool_life_data:
                _tool_life_data[tool_num] = {"cutting_time_sec": 0, "change_count": 0, "max_time_sec": 0, "max_count": 0}
            _tool_life_data[tool_num]["max_time_sec"] = int(max_time)
            _tool_life_data[tool_num]["max_count"] = int(max_count)
            _save_tool_life()
        app_log('CMD', f'Tool life config: T{tool_num} max_time={max_time}s max_count={max_count}')
        return success_response({'tool_number': tool_num, 'max_time_sec': max_time, 'max_count': max_count})
    except Exception as e:
        return error_response(f"Tool life config failed: {e}")


@tool_bp.route('/v2/tool/life/reset', methods=['POST'])
def v2_tool_life_reset():
    try:
        data = request.json or {}
        tool_num = str(data.get('tool_number', 0))
        if tool_num == '0':
            return error_response("Missing tool_number", 400)
        with _tool_life_lock:
            if tool_num in _tool_life_data:
                _tool_life_data[tool_num]["cutting_time_sec"] = 0
                _tool_life_data[tool_num]["change_count"] = 0
                _save_tool_life()
        app_log('CMD', f'Tool life reset: T{tool_num}')
        return success_response({'tool_number': tool_num})
    except Exception as e:
        return error_response(f"Tool life reset failed: {e}")
