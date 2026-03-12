# [2026-03-12] 從 server.py 拆分：統計/維護/續切/備份

import os
import time
import json
import shutil
import datetime
import threading
from flask import Blueprint, request
import shared
from shared import (
    USER_HOME, CONFIG_DIR,
    success_response, error_response, app_log,
    linuxcnc
)

stats_bp = Blueprint('stats', __name__)


# ==============================================================================
# 加工時間統計
# ==============================================================================
MACHINING_STATS_PATH = os.path.join(CONFIG_DIR, 'machining_stats.json')
_machining_stats = {"total_seconds": 0, "cycle_count": 0, "last_file": ""}
_machining_stats_lock = threading.Lock()


def _load_machining_stats():
    global _machining_stats
    try:
        if os.path.exists(MACHINING_STATS_PATH):
            with open(MACHINING_STATS_PATH, 'r', encoding='utf-8') as f:
                _machining_stats = json.load(f)
    except: pass


def _save_machining_stats():
    try:
        with open(MACHINING_STATS_PATH, 'w', encoding='utf-8') as f:
            json.dump(_machining_stats, f, indent=2)
    except: pass


def _machining_stats_tracker():
    _load_machining_stats()
    was_running = False
    last_save = time.time()

    while True:
        try:
            time.sleep(1)
            if shared.cnc_stat is None: continue
            try: shared.cnc_stat.poll()
            except: continue

            is_running = False
            try:
                is_running = shared.cnc_stat.interp_state in (linuxcnc.INTERP_READING, linuxcnc.INTERP_WAITING) if linuxcnc else False
            except: pass

            with _machining_stats_lock:
                if is_running:
                    _machining_stats["total_seconds"] += 1
                    try:
                        if shared.cnc_stat.file:
                            _machining_stats["last_file"] = os.path.basename(shared.cnc_stat.file)
                    except: pass

                if was_running and not is_running:
                    _machining_stats["cycle_count"] += 1
                    _save_machining_stats()

                was_running = is_running

                if time.time() - last_save >= 30:
                    _save_machining_stats()
                    last_save = time.time()
        except Exception as e:
            app_log('ERROR', f'Machining stats tracker error: {e}')
            time.sleep(5)


_machining_stats_thread = threading.Thread(target=_machining_stats_tracker, daemon=True)
_machining_stats_thread.start()


@stats_bp.route('/v2/machining/stats', methods=['GET'])
def v2_machining_stats():
    try:
        with _machining_stats_lock:
            return success_response(dict(_machining_stats))
    except Exception as e:
        return error_response(f"Machining stats read failed: {e}")


@stats_bp.route('/v2/machining/stats/reset', methods=['POST'])
def v2_machining_stats_reset():
    try:
        with _machining_stats_lock:
            _machining_stats["total_seconds"] = 0
            _machining_stats["cycle_count"] = 0
            _save_machining_stats()
        return success_response({'reset': True})
    except Exception as e:
        return error_response(f"Machining stats reset failed: {e}")


# ==============================================================================
# 維護保養提醒
# ==============================================================================
MAINTENANCE_PATH = os.path.join(CONFIG_DIR, 'maintenance.json')
_maintenance_data = []
_maintenance_lock = threading.Lock()


def _load_maintenance():
    global _maintenance_data
    try:
        if os.path.exists(MAINTENANCE_PATH):
            with open(MAINTENANCE_PATH, 'r', encoding='utf-8') as f:
                _maintenance_data = json.load(f)
    except:
        _maintenance_data = []


def _save_maintenance():
    try:
        with open(MAINTENANCE_PATH, 'w', encoding='utf-8') as f:
            json.dump(_maintenance_data, f, indent=2, ensure_ascii=False)
    except: pass


def _maintenance_tracker():
    _load_maintenance()
    last_save = time.time()

    while True:
        try:
            time.sleep(1)
            if shared.cnc_stat is None: continue
            try: shared.cnc_stat.poll()
            except: continue

            is_on = False
            try:
                is_on = (shared.cnc_stat.task_state == linuxcnc.STATE_ON) if linuxcnc else False
            except: pass

            if is_on:
                with _maintenance_lock:
                    for item in _maintenance_data:
                        item["accumulated_hours"] = item.get("accumulated_hours", 0) + (1.0 / 3600.0)

            if time.time() - last_save >= 60:
                with _maintenance_lock:
                    _save_maintenance()
                last_save = time.time()
        except:
            time.sleep(5)


_maintenance_thread = threading.Thread(target=_maintenance_tracker, daemon=True)
_maintenance_thread.start()


@stats_bp.route('/v2/maintenance', methods=['GET'])
def v2_maintenance_get():
    try:
        with _maintenance_lock:
            return success_response(list(_maintenance_data))
    except Exception as e:
        return error_response(f"Maintenance read failed: {e}")


@stats_bp.route('/v2/maintenance', methods=['POST'])
def v2_maintenance_post():
    try:
        data = request.json or {}
        items = data.get('items', [])
        with _maintenance_lock:
            global _maintenance_data
            _maintenance_data = items
            _save_maintenance()
        return success_response({'saved': len(items)})
    except Exception as e:
        return error_response(f"Maintenance save failed: {e}")


@stats_bp.route('/v2/maintenance/reset', methods=['POST'])
def v2_maintenance_reset():
    try:
        data = request.json or {}
        name = data.get('name', '')
        with _maintenance_lock:
            for item in _maintenance_data:
                if item.get('name') == name:
                    item['accumulated_hours'] = 0
                    item['last_reset_time'] = time.time()
                    break
            _save_maintenance()
        return success_response({'reset': name})
    except Exception as e:
        return error_response(f"Maintenance reset failed: {e}")


# ==============================================================================
# 斷電續切
# ==============================================================================
RESUME_STATE_PATH = os.path.join(CONFIG_DIR, 'resume_state.json')


def _resume_state_tracker():
    was_running = False

    while True:
        try:
            time.sleep(5)
            if shared.cnc_stat is None: continue
            try: shared.cnc_stat.poll()
            except: continue

            is_running = False
            try:
                is_running = shared.cnc_stat.interp_state in (linuxcnc.INTERP_READING, linuxcnc.INTERP_WAITING) if linuxcnc else False
            except: pass

            if is_running:
                try:
                    state = {
                        "file": os.path.basename(shared.cnc_stat.file) if shared.cnc_stat.file else "",
                        "line": int(shared.cnc_stat.motion_line),
                        "tool": int(shared.cnc_stat.tool_in_spindle),
                        "wcs": shared.cnc_stat.g5x_index,
                        "timestamp": time.time()
                    }
                    with open(RESUME_STATE_PATH, 'w', encoding='utf-8') as f:
                        json.dump(state, f, indent=2)
                except: pass
            elif was_running and not is_running:
                try:
                    if os.path.exists(RESUME_STATE_PATH):
                        os.remove(RESUME_STATE_PATH)
                except: pass

            was_running = is_running
        except:
            time.sleep(5)


_resume_state_thread = threading.Thread(target=_resume_state_tracker, daemon=True)
_resume_state_thread.start()


@stats_bp.route('/v2/resume/state', methods=['GET'])
def v2_resume_state():
    try:
        if os.path.exists(RESUME_STATE_PATH):
            with open(RESUME_STATE_PATH, 'r', encoding='utf-8') as f:
                state = json.load(f)
            return success_response(state)
        return success_response(None)
    except Exception as e:
        return error_response(f"Resume state read failed: {e}")


@stats_bp.route('/v2/resume/clear', methods=['POST'])
def v2_resume_clear():
    try:
        if os.path.exists(RESUME_STATE_PATH):
            os.remove(RESUME_STATE_PATH)
        return success_response({'cleared': True})
    except Exception as e:
        return error_response(f"Resume state clear failed: {e}")


# ==============================================================================
# 備份 / 還原
# ==============================================================================
BACKUP_DIR = os.path.join(USER_HOME, "linuxcnc/backups")
BACKUP_FILES = [
    '3axis.ini', '3axis.hal', 'ethercat-conf.xml',
    'tool_metric.tbl', 'linuxcnc.var',
    'probe_basic_postgui.hal', 'sim_probe.hal', 'sim_atc.hal',
    'custom_config.yml'
]


@stats_bp.route('/v2/backup/create', methods=['POST'])
def v2_backup_create():
    try:
        timestamp = datetime.datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
        backup_path = os.path.join(BACKUP_DIR, timestamp)
        os.makedirs(backup_path, exist_ok=True)

        copied = []
        for fname in BACKUP_FILES:
            src = os.path.join(CONFIG_DIR, fname)
            if os.path.exists(src):
                shutil.copy2(src, os.path.join(backup_path, fname))
                copied.append(fname)

        macros_src = os.path.join(CONFIG_DIR, 'macros_metric_sim')
        if os.path.isdir(macros_src):
            macros_dst = os.path.join(backup_path, 'macros_metric_sim')
            shutil.copytree(macros_src, macros_dst)
            for f_name in os.listdir(macros_dst):
                copied.append(f'macros_metric_sim/{f_name}')

        data = request.json or {}
        for key in ['MachineConfig', 'AppSettings', 'ProbeSettings', 'ToolLife']:
            content = data.get(key)
            if content:
                with open(os.path.join(backup_path, f'{key}.json'), 'w', encoding='utf-8') as f:
                    f.write(content)
                copied.append(f'{key}.json')

        manifest = {
            'timestamp': timestamp,
            'created_at': datetime.datetime.now().isoformat(),
            'files': copied,
            'file_count': len(copied)
        }
        with open(os.path.join(backup_path, 'backup_manifest.json'), 'w', encoding='utf-8') as f:
            json.dump(manifest, f, indent=2)

        total_size = sum(
            os.path.getsize(os.path.join(dp, fn))
            for dp, _, fns in os.walk(backup_path) for fn in fns
        )
        app_log('CMD', f'Backup created: {timestamp} ({len(copied)} files, {total_size} bytes)')
        return success_response({'name': timestamp, 'file_count': len(copied), 'size_bytes': total_size})
    except Exception as e:
        return error_response(f"Backup create failed: {e}")


@stats_bp.route('/v2/backup/list', methods=['GET'])
def v2_backup_list():
    try:
        backups = []
        if not os.path.isdir(BACKUP_DIR):
            return success_response(backups)
        for name in sorted(os.listdir(BACKUP_DIR), reverse=True):
            bpath = os.path.join(BACKUP_DIR, name)
            if not os.path.isdir(bpath): continue
            manifest_path = os.path.join(bpath, 'backup_manifest.json')
            file_count = 0
            if os.path.exists(manifest_path):
                with open(manifest_path, 'r', encoding='utf-8') as f:
                    m = json.load(f)
                    file_count = m.get('file_count', 0)
            total_size = sum(
                os.path.getsize(os.path.join(dp, fn))
                for dp, _, fns in os.walk(bpath) for fn in fns
            )
            backups.append({'name': name, 'file_count': file_count, 'size_bytes': total_size})
        return success_response(backups)
    except Exception as e:
        return error_response(f"Backup list failed: {e}")


@stats_bp.route('/v2/backup/restore', methods=['POST'])
def v2_backup_restore():
    try:
        data = request.json or {}
        name = data.get('name', '')
        if not name: return error_response("Missing backup name", 400)
        bpath = os.path.join(BACKUP_DIR, name)
        if not os.path.isdir(bpath): return error_response(f"Backup not found: {name}", 404)

        restored = []
        for fname in BACKUP_FILES:
            src = os.path.join(bpath, fname)
            if os.path.exists(src):
                shutil.copy2(src, os.path.join(CONFIG_DIR, fname))
                restored.append(fname)

        macros_src = os.path.join(bpath, 'macros_metric_sim')
        if os.path.isdir(macros_src):
            macros_dst = os.path.join(CONFIG_DIR, 'macros_metric_sim')
            if os.path.isdir(macros_dst): shutil.rmtree(macros_dst)
            shutil.copytree(macros_src, macros_dst)
            restored.append('macros_metric_sim/')

        app_log('CMD', f'Backup restored: {name} ({len(restored)} items)')

        frontend_configs = {}
        for key in ['MachineConfig', 'AppSettings', 'ProbeSettings', 'ToolLife']:
            fpath = os.path.join(bpath, f'{key}.json')
            if os.path.exists(fpath):
                with open(fpath, 'r', encoding='utf-8') as f:
                    frontend_configs[key] = f.read()

        def _delayed_restart():
            time.sleep(1)
            from server import perform_hard_restart
            perform_hard_restart()
        threading.Thread(target=_delayed_restart, daemon=True).start()

        return success_response({'restored': restored, 'frontend_configs': frontend_configs})
    except Exception as e:
        return error_response(f"Backup restore failed: {e}")


@stats_bp.route('/v2/backup/delete', methods=['POST'])
def v2_backup_delete():
    try:
        data = request.json or {}
        name = data.get('name', '')
        if not name: return error_response("Missing backup name", 400)
        bpath = os.path.join(BACKUP_DIR, name)
        if not os.path.isdir(bpath): return error_response(f"Backup not found: {name}", 404)
        shutil.rmtree(bpath)
        app_log('CMD', f'Backup deleted: {name}')
        return success_response({'deleted': name})
    except Exception as e:
        return error_response(f"Backup delete failed: {e}")
