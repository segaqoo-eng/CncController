# [2026-03-12] 從 server.py 拆分：程式檔案管理 + 加工循環控制

import os
from flask import Blueprint, request
import shared
from shared import (
    NC_FILES_DIR,
    success_response, error_response, app_log,
    ensure_cnc_connections, linuxcnc
)

program_bp = Blueprint('program', __name__)


# [2026-03-12] /api/files/upload → /v2/program/upload（路由統一）
@program_bp.route('/v2/program/upload', methods=['POST'])
def upload_file():
    """接收前端傳來的 G-Code 字串並存檔"""
    try:
        data = request.json
        if not data:
            return error_response('No JSON data received', 400)

        filename = data.get('name')
        content = data.get('content')

        if not filename or content is None:
            return error_response('Missing filename or content', 400)

        filename = os.path.basename(filename)

        if not os.path.exists(NC_FILES_DIR):
            os.makedirs(NC_FILES_DIR)

        filepath = os.path.join(NC_FILES_DIR, filename)

        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

        print(f"[Upload] File saved: {filepath}")

        return success_response({
            'message': f'File {filename} uploaded successfully',
            'path': filepath
        })
    except Exception as e:
        return error_response(str(e))


@program_bp.route('/v2/program/list', methods=['GET'])
def v2_program_list():
    """列出 NC 檔案（檔名 + 大小 + 修改時間）"""
    try:
        if not os.path.exists(NC_FILES_DIR):
            return success_response([])
        files = []
        for f in os.listdir(NC_FILES_DIR):
            fp = os.path.join(NC_FILES_DIR, f)
            if os.path.isfile(fp):
                stat = os.stat(fp)
                files.append({
                    'name': f,
                    'size': stat.st_size,
                    'modified': stat.st_mtime
                })
        files.sort(key=lambda x: x['modified'], reverse=True)
        return success_response(files)
    except Exception as e:
        return error_response(f"List fail: {e}")


@program_bp.route('/v2/program/delete', methods=['POST'])
def v2_program_delete():
    """刪除指定 NC 檔案"""
    try:
        data = request.json or {}
        filename = data.get('name', '')
        if not filename:
            return error_response("Missing filename", 400)
        filename = os.path.basename(filename)
        filepath = os.path.join(NC_FILES_DIR, filename)
        if not os.path.exists(filepath):
            return error_response(f"File not found: {filename}", 404)
        os.remove(filepath)
        print(f"[FileMan] Deleted: {filepath}")
        return success_response(f"Deleted: {filename}")
    except Exception as e:
        return error_response(f"Delete fail: {e}")


@program_bp.route('/v2/program/rename', methods=['POST'])
def v2_program_rename():
    """重命名 NC 檔案"""
    try:
        data = request.json or {}
        old_name = data.get('old_name', '')
        new_name = data.get('new_name', '')
        if not old_name or not new_name:
            return error_response("Missing old_name or new_name", 400)
        old_name = os.path.basename(old_name)
        new_name = os.path.basename(new_name)
        old_path = os.path.join(NC_FILES_DIR, old_name)
        new_path = os.path.join(NC_FILES_DIR, new_name)
        if not os.path.exists(old_path):
            return error_response(f"File not found: {old_name}", 404)
        if os.path.exists(new_path):
            return error_response(f"Target exists: {new_name}", 409)
        os.rename(old_path, new_path)
        print(f"[FileMan] Renamed: {old_name} -> {new_name}")
        return success_response(f"Renamed: {old_name} -> {new_name}")
    except Exception as e:
        return error_response(f"Rename fail: {e}")


@program_bp.route('/v2/program/load', methods=['POST'])
def v2_program_load():
    """載入指定 NC 檔案到 LinuxCNC（不執行）"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        filename = data.get('name', '')
        if not filename:
            return error_response("Missing filename", 400)
        filename = os.path.basename(filename)
        filepath = os.path.join(NC_FILES_DIR, filename)
        if not os.path.exists(filepath):
            return error_response(f"File not found: {filename}", 404)
        shared.cnc_stat.poll()
        if shared.cnc_stat.task_mode != linuxcnc.MODE_AUTO:
            shared.cnc_cmd.mode(linuxcnc.MODE_AUTO)
            shared.cnc_cmd.wait_complete()
        shared.cnc_cmd.program_open(filepath)
        shared.cnc_cmd.wait_complete()
        print(f"[FileMan] Loaded: {filepath}")
        return success_response(f"Loaded: {filename}")
    except Exception as e:
        return error_response(f"Load fail: {e}")


@program_bp.route('/v2/program/read', methods=['POST'])
def v2_program_read():
    """回讀 NC 檔案內容"""
    try:
        data = request.json or {}
        filename = data.get('name', '')
        if not filename:
            return error_response("Missing filename", 400)
        filename = os.path.basename(filename)
        filepath = os.path.join(NC_FILES_DIR, filename)
        if not os.path.exists(filepath):
            return error_response(f"File not found: {filename}", 404)
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        return success_response({"name": filename, "content": content})
    except Exception as e:
        return error_response(f"Read fail: {e}")


@program_bp.route('/v2/program/run', methods=['POST'])
def v2_program_run():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        line = int(data.get('line', 0))
        file_name = data.get('file_name')

        shared.cnc_stat.poll()
        if shared.cnc_stat.task_state != linuxcnc.STATE_ON: return error_response("Machine OFF")

        if shared.cnc_stat.interp_state == linuxcnc.INTERP_PAUSED:
            shared.cnc_cmd.auto(linuxcnc.AUTO_RESUME)
            return success_response("Resumed (Auto)")

        if shared.cnc_stat.task_mode != linuxcnc.MODE_AUTO:
            shared.cnc_cmd.mode(linuxcnc.MODE_AUTO)
            shared.cnc_cmd.wait_complete()

        if file_name:
            full_path = os.path.join(NC_FILES_DIR, os.path.basename(file_name))
            if os.path.exists(full_path):
                shared.cnc_cmd.program_open(full_path)
                shared.cnc_cmd.wait_complete()
            else:
                return error_response(f"File not found: {file_name}", 404)

        start_line = int(data.get('start_line', line))
        shared.cnc_cmd.auto(linuxcnc.AUTO_RUN, start_line)
        return success_response("Started")
    except Exception as e: return error_response(f"Run Fail: {e}")


@program_bp.route('/v2/program/resume', methods=['POST'])
def v2_program_resume():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        shared.cnc_stat.poll()
        if shared.cnc_stat.interp_state == linuxcnc.INTERP_PAUSED:
            shared.cnc_cmd.auto(linuxcnc.AUTO_RESUME)
            return success_response("Resumed")
        else:
            return error_response("Not Paused", 400)
    except Exception as e: return error_response(f"Resume Fail: {e}")


@program_bp.route('/v2/program/pause', methods=['POST'])
def v2_program_pause():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        shared.cnc_cmd.auto(linuxcnc.AUTO_PAUSE)
        return success_response("Paused")
    except Exception as e: return error_response(f"Pause Fail: {e}")


@program_bp.route('/v2/program/stop', methods=['POST'])
def v2_program_stop():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        shared.cnc_cmd.abort()
        return success_response("Aborted")
    except Exception as e: return error_response(f"Stop Fail: {e}")


@program_bp.route('/v2/program/step', methods=['POST'])
def v2_program_step():
    """單節執行：每次 Cycle Start 僅執行一行 G-Code"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        shared.cnc_stat.poll()
        if shared.cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine OFF")
        if shared.cnc_stat.task_mode != linuxcnc.MODE_AUTO:
            shared.cnc_cmd.mode(linuxcnc.MODE_AUTO)
            shared.cnc_cmd.wait_complete()
        shared.cnc_cmd.auto(linuxcnc.AUTO_STEP)
        app_log('CMD', 'Single Block Step')
        return success_response("Stepped")
    except Exception as e:
        return error_response(f"Step Fail: {e}")


@program_bp.route('/v2/macro/read', methods=['POST'])
def v2_macro_read():
    """讀取指定巨集變數（從 .var 檔）"""
    from shared import _read_var_params
    try:
        data = request.json or {}
        ids = data.get('ids', [])
        if not ids:
            return error_response("Missing 'ids' list", 400)
        param_ids = set(int(i) for i in ids)
        params = _read_var_params(param_ids)
        result = {}
        for pid in sorted(param_ids):
            result[str(pid)] = params.get(pid, 0.0)
        return success_response(result)
    except Exception as e:
        return error_response(f"Macro read fail: {e}")


@program_bp.route('/v2/macro/write', methods=['POST'])
def v2_macro_write():
    """透過 MDI 安全寫入巨集變數（#id = value）"""
    from shared import probe_send_mdi_and_wait
    try:
        data = request.json or {}
        assignments = data.get('assignments', {})
        if not assignments:
            return error_response("Missing 'assignments' dict", 400)
        shared.cnc_stat.poll()
        if shared.cnc_stat.task_mode != linuxcnc.MODE_MDI:
            shared.cnc_cmd.mode(linuxcnc.MODE_MDI)
            shared.cnc_cmd.wait_complete(2)
        errors = []
        for param_id, value in assignments.items():
            pid = int(param_id)
            val = float(value)
            mdi_cmd = f"#{pid} = {val}"
            err = probe_send_mdi_and_wait(mdi_cmd, timeout=5)
            if err:
                errors.append(f"#{pid}: {err}")
        if errors:
            return error_response("; ".join(errors))
        return success_response("Variables updated")
    except Exception as e:
        return error_response(f"Macro write fail: {e}")


@program_bp.route('/v2/macro/readall', methods=['POST'])
def v2_macro_readall():
    """讀取指定範圍的所有巨集變數"""
    from shared import _read_var_params
    try:
        data = request.json or {}
        start = int(data.get('start', 1))
        end = int(data.get('end', 30))
        if end < start or (end - start) > 1000:
            return error_response("Invalid range (max 1000)", 400)
        param_ids = set(range(start, end + 1))
        params = _read_var_params(param_ids)
        result = {}
        for pid in range(start, end + 1):
            if pid in params:
                result[str(pid)] = params[pid]
        return success_response(result)
    except Exception as e:
        return error_response(f"Macro readall fail: {e}")
