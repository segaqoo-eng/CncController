# [2026-03-12] 從 server.py 拆分：核心狀態輪詢（/v2/status + /v2/errors + /v2/offsets）
# [2026-03-13] 重構：背景 Thread 快取架構（高頻 20ms + 低頻 1s），API 純讀快取

import os
import time
import threading
import subprocess
from flask import Blueprint
from shared import (
    cnc_stat, cached_errors, error_lock, _wcs_cache,
    _status_cache_lock, _status_cache_fast, _status_cache_slow,
    _status_cache_fast_ts, STATUS_CACHE_STALE_SEC,
    CONFIG_DIR, LINUXCNC_INI_PATH,
    success_response, error_response, app_log,
    ensure_cnc_connections, linuxcnc,
    _ini_value, _read_hal_pin, _read_hal_pins_batch
)

status_bp = Blueprint('status', __name__)


# ==============================================================================
# 背景快取 Thread 1：高頻（20ms）— 純 NML shared memory，<0.1ms
# ==============================================================================

def _status_fast_loop():
    """[2026-03-13] 高頻輪詢：座標/進給/主軸/狀態（cnc_stat.poll() only）"""
    import shared
    app_log('INFO', "Status Fast Cache Thread Started (20ms).")
    while True:
        try:
            if not ensure_cnc_connections():
                time.sleep(3.0)  # NML 未就緒，3s 後重試（避免日誌洗版）
                continue

            cnc_s = shared.cnc_stat
            cnc_s.poll()

            pos_dict = {}
            try:
                raw_pos = cnc_s.actual_position
                for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                    if i < len(raw_pos): pos_dict[axis] = float(f"{raw_pos[i]:.4f}")
            except: pass

            t_state = "UNKNOWN"
            if cnc_s.task_state == linuxcnc.STATE_ON: t_state = "ON"
            elif cnc_s.task_state == linuxcnc.STATE_OFF: t_state = "OFF"
            elif cnc_s.task_state == linuxcnc.STATE_ESTOP: t_state = "ESTOP"

            i_state = "IDLE"
            if cnc_s.interp_state == linuxcnc.INTERP_IDLE: i_state = "IDLE"
            elif cnc_s.interp_state == linuxcnc.INTERP_READING: i_state = "RUNNING"
            elif cnc_s.interp_state == linuxcnc.INTERP_PAUSED: i_state = "PAUSED"
            elif cnc_s.interp_state == linuxcnc.INTERP_WAITING: i_state = "RUNNING"

            feed = 0.0
            try: feed = cnc_s.settings[1]
            except: pass

            spindle_speed = 0.0
            spindle_direction = 0
            try:
                if hasattr(cnc_s, 'spindle') and len(cnc_s.spindle) > 0:
                    spindle_speed = abs(cnc_s.spindle[0]['speed'])
                    spindle_direction = int(cnc_s.spindle[0].get('direction', 0))
            except: pass

            filename = "No File"
            try:
                if cnc_s.file: filename = os.path.basename(cnc_s.file)
            except: pass

            dtg_dict = {}
            try:
                raw_dtg = cnc_s.dtg
                for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                    if i < len(raw_dtg): dtg_dict[axis] = float(f"{raw_dtg[i]:.4f}")
            except: pass

            work_pos = {}
            try:
                g5x = cnc_s.g5x_offset
                g92 = cnc_s.g92_offset
                tool = cnc_s.tool_offset
                for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                    if i < len(raw_pos):
                        work_pos[axis] = float(f"{raw_pos[i] - g5x[i] - g92[i] - tool[i]:.4f}")
            except: pass

            feed_override = 100.0
            try:
                feed_override = round(cnc_s.feedrate * 100.0, 1)
            except: pass

            spindle_override = 100.0
            try:
                if hasattr(cnc_s, 'spindle') and len(cnc_s.spindle) > 0:
                    spindle_override = round(cnc_s.spindle[0]['override'] * 100.0, 1)
            except: pass

            active_wcs = "G54"
            try:
                idx = cnc_s.g5x_index
                _idx_map = {1:'G54', 2:'G55', 3:'G56', 4:'G57', 5:'G58', 6:'G59',
                            7:'G59.1', 8:'G59.2', 9:'G59.3'}
                active_wcs = _idx_map.get(idx, "G54")
            except: pass

            tool_number = 0
            tool_length = 0.0
            tool_diameter = 0.0
            try:
                tool_number = int(cnc_s.tool_in_spindle)
                tool_length = float(cnc_s.tool_offset[2])
                if tool_number > 0 and tool_number < len(cnc_s.tool_table):
                    tool_diameter = float(cnc_s.tool_table[tool_number].diameter)
            except: pass

            homed_dict = {}
            for i, name in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                try:
                    homed_dict[name] = bool(cnc_s.homed[i])
                except:
                    homed_dict[name] = False

            g92_dict = {}
            try:
                for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                    if i < len(g92):
                        g92_dict[axis] = round(float(g92[i]), 4)
            except: pass

            tool_offset_dict = {}
            try:
                for i, axis in enumerate(['X', 'Y', 'Z', 'A', 'B', 'C']):
                    if i < len(tool):
                        tool_offset_dict[axis] = round(float(tool[i]), 4)
            except: pass

            task_mode_str = {1: "MANUAL", 2: "AUTO", 3: "MDI"}.get(cnc_s.task_mode, "UNKNOWN")

            probe_input = False
            try:
                probe_input = bool(cnc_s.probe_val)
            except: pass

            block_delete = False
            optional_stop = False
            current_line = 0
            try: block_delete = bool(cnc_s.block_delete)
            except: pass
            try: optional_stop = bool(cnc_s.optional_stop)
            except: pass
            try: current_line = int(cnc_s.motion_line)
            except: pass

            snapshot = {
                "Connected": True,
                "Task_State": t_state,
                "Interp_State": i_state,
                "Position": pos_dict,
                "Work_Position": work_pos,
                "DTG": dtg_dict,
                "Feedrate": feed,
                "Spindle_Speed": spindle_speed,
                "Spindle_Direction": spindle_direction,
                "Feed_Override": feed_override,
                "Spindle_Override": spindle_override,
                "File": filename,
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
                "Current_Line": current_line,
            }

            with _status_cache_lock:
                shared._status_cache_fast.update(snapshot)
                shared._status_cache_fast_ts = time.time()

        except Exception as e:
            app_log('ERROR', f"Fast cache error: {e}")
            time.sleep(0.5)
            continue

        time.sleep(0.02)


# ==============================================================================
# 背景快取 Thread 2：低頻（1s）— subprocess halcmd（Servo IO / IO Status / Encoder）
# ==============================================================================

def read_servo_raw_data():
    """ 讀取 lcec 的 HAL Pin 狀態 (DI & Status Word) """
    data = {}
    try:
        res = subprocess.run(
            ["halcmd", "-s", "show", "pin", "lcec"],
            capture_output=True, text=True, timeout=0.5
        )
        if res.returncode != 0: return {}

        for line in res.stdout.splitlines():
            if "lcec.0" not in line: continue
            parts = line.split()

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


def _status_slow_loop():
    """[2026-03-13] 低頻輪詢：Servo IO / IO Status / Spindle Encoder / Program Lines"""
    import shared
    app_log('INFO', "Status Slow Cache Thread Started (1s).")

    # [2026-03-13] 快取 INI 設定值（啟動時讀一次，不必每秒讀 INI 檔）
    _sp_slave = _ini_value('SPINDLE', 'SLAVE_INDEX')
    _sp_ppr = int(_ini_value('SPINDLE', 'ENCODER_PPR') or 4096)
    _atc_io_idx = _ini_value('ATC', 'IO_SLAVE_INDEX')

    while True:
        try:
            if not ensure_cnc_connections():
                time.sleep(1.0)
                continue

            cnc_s = shared.cnc_stat
            cnc_s.poll()

            # --- Spindle Encoder（需要 halcmd subprocess）---
            spindle_position = 0.0
            try:
                sp_found = False
                if _sp_slave is not None and int(_sp_slave) >= 0:
                    sp_idx = int(_sp_slave)
                    sp_raw = _read_hal_pin(f'lcec.0.{sp_idx}.position_actual_value_J{sp_idx}')
                    if sp_raw is not None:
                        spindle_position = round(float(sp_raw) / _sp_ppr * 360.0 % 360.0, 2)
                        sp_found = True
                if not sp_found:
                    sp_revs = _read_hal_pin('spindle.0.revs')
                    if sp_revs is not None and float(sp_revs) != 0:
                        spindle_position = round(float(sp_revs) * 360.0 % 360.0, 2)
                        sp_found = True
                if not sp_found:
                    sp_fb = _read_hal_pin('spindle.0.pos-fb')
                    if sp_fb is not None and float(sp_fb) != 0:
                        spindle_position = round(float(sp_fb) % 360.0, 2)
            except: pass

            # --- Servo IO（subprocess halcmd）---
            servo_io_data = read_servo_raw_data()

            # --- IO Status（subprocess halcmd batch）---
            io_status = {}
            try:
                known_slaves = set()
                if servo_io_data:
                    known_slaves = set(servo_io_data.keys())
                if _atc_io_idx is not None:
                    known_slaves.add(str(int(_atc_io_idx)))
                for slave_idx in known_slaves:
                    si = int(slave_idx)
                    prefix = f'lcec.0.{si}'
                    di_pins = [f'{prefix}.din-{p:02d}' for p in range(32)]
                    di_vals = _read_hal_pins_batch(di_pins, prefix)
                    di_map = {}
                    for p in range(32):
                        pname = f'{prefix}.din-{p:02d}'
                        if pname in di_vals:
                            di_map[p] = di_vals[pname]
                    do_pins = [f'{prefix}.dout-{p:02d}' for p in range(32)]
                    do_vals = _read_hal_pins_batch(do_pins, prefix)
                    do_map = {}
                    for p in range(32):
                        pname = f'{prefix}.dout-{p:02d}'
                        if pname in do_vals:
                            do_map[p] = do_vals[pname]
                    if di_map or do_map:
                        io_status[slave_idx] = {}
                        if di_map:
                            io_status[slave_idx]['di'] = di_map
                        if do_map:
                            io_status[slave_idx]['do'] = do_map
            except: pass

            # --- Program Total Lines（讀檔）---
            program_total_lines = 0
            try:
                if cnc_s.file and os.path.exists(cnc_s.file):
                    with open(cnc_s.file, 'r', encoding='utf-8', errors='ignore') as pf:
                        program_total_lines = sum(1 for _ in pf)
            except: pass

            snapshot = {
                "Spindle_Position": spindle_position,
                "Servo_IO": servo_io_data,
                "IO_Status": io_status,
                "Program_Total_Lines": program_total_lines,
            }

            with _status_cache_lock:
                shared._status_cache_slow.update(snapshot)

        except Exception as e:
            app_log('ERROR', f"Slow cache error: {e}")

        time.sleep(1.0)


# ==============================================================================
# 啟動背景快取 Thread（由 server.py 在 LinuxCNC 啟動後呼叫）
# ==============================================================================

def start_status_cache_threads():
    """[2026-03-13] 由 server.py 在 start_linuxcnc_process() 之後呼叫"""
    if not linuxcnc:
        app_log('INFO', "Status cache threads skipped (SIMULATION mode).")
        return
    _fast_thread = threading.Thread(target=_status_fast_loop, daemon=True)
    _fast_thread.start()
    _slow_thread = threading.Thread(target=_status_slow_loop, daemon=True)
    _slow_thread.start()


# ==============================================================================
# API 端點（純讀快取，不做任何 I/O）
# ==============================================================================

@status_bp.route('/v2/status', methods=['GET'])
def v2_status():
    """[2026-03-13] 重構：直接回傳背景快取，回應時間 <1ms"""
    import shared
    from flask import jsonify

    with _status_cache_lock:
        fast = dict(shared._status_cache_fast)
        slow = dict(shared._status_cache_slow)
        cache_ts = shared._status_cache_fast_ts

    if not fast:
        # 快取尚未就緒（剛啟動）
        if not ensure_cnc_connections():
            return jsonify({'status': 'Error', 'message': 'NML Disconnected'}), 503
        return jsonify({'status': 'Error', 'message': 'Cache warming up'}), 503

    # [2026-03-13] 快取過期檢查：背景 Thread 超過 2s 沒更新 → NML 斷線
    if cache_ts > 0 and (time.time() - cache_ts) > STATUS_CACHE_STALE_SEC:
        return jsonify({'status': 'Error', 'message': 'NML Stale (cache timeout)'}), 503

    # 合併高頻 + 低頻快取
    merged = {**fast, **slow}

    # 低頻快取可能尚未就緒，補預設值
    merged.setdefault("Spindle_Position", 0.0)
    merged.setdefault("Servo_IO", {})
    merged.setdefault("IO_Status", {})
    merged.setdefault("Program_Total_Lines", 0)

    return success_response(merged)


@status_bp.route('/v2/errors', methods=['GET'])
def v2_errors():
    messages = []
    with error_lock:
        while cached_errors:
            messages.append(cached_errors.popleft())
    return success_response(messages if messages else None)


def read_work_offsets():
    """從 LinuxCNC Parameter File (.var) 讀取 G54-G59 六組 WCS 座標值"""
    import shared
    param_bases = {
        'G54': 5221, 'G55': 5241, 'G56': 5261,
        'G57': 5281, 'G58': 5301, 'G59': 5321,
        'G59.1': 5341, 'G59.2': 5361, 'G59.3': 5381
    }
    axes = ['X', 'Y', 'Z', 'A', 'B', 'C']
    offsets = {wcs: {a: 0.0 for a in axes} for wcs in param_bases}

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

    for wcs, cached_vals in _wcs_cache.items():
        if wcs in offsets:
            offsets[wcs].update(cached_vals)

    try:
        cnc_s = shared.cnc_stat
        if cnc_s:
            cnc_s.poll()
            idx = cnc_s.g5x_index
            idx_to_wcs = {1:'G54', 2:'G55', 3:'G56', 4:'G57', 5:'G58', 6:'G59',
                          7:'G59.1', 8:'G59.2', 9:'G59.3'}
            active_wcs_name = idx_to_wcs.get(idx)
            if active_wcs_name and active_wcs_name in offsets:
                g5x = cnc_s.g5x_offset
                for i, axis in enumerate(axes):
                    if i < len(g5x):
                        offsets[active_wcs_name][axis] = round(float(g5x[i]), 4)
    except Exception:
        pass

    return offsets


@status_bp.route('/v2/offsets', methods=['GET'])
def v2_offsets():
    """回傳 G54-G59 所有工件座標系偏移值 + 目前 Active WCS"""
    import shared
    active_wcs = "G54"
    try:
        if ensure_cnc_connections():
            shared.cnc_stat.poll()
            idx = shared.cnc_stat.g5x_index
            _idx_map = {1:'G54', 2:'G55', 3:'G56', 4:'G57', 5:'G58', 6:'G59',
                        7:'G59.1', 8:'G59.2', 9:'G59.3'}
            active_wcs = _idx_map.get(idx, "G54")
    except Exception:
        pass

    offsets = read_work_offsets()
    return success_response({"Active": active_wcs, "Offsets": offsets})
