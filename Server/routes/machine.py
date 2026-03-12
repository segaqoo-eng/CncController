# [2026-03-12] 從 server.py 拆分：機台控制（machine/motion/mdi/override）

from flask import Blueprint, request
import shared
from shared import (
    success_response, error_response, app_log,
    ensure_cnc_connections, linuxcnc,
    _update_wcs_cache_from_g10
)

machine_bp = Blueprint('machine', __name__)


@machine_bp.route('/v2/motion/jog', methods=['POST'])
def v2_motion_jog():
    if not ensure_cnc_connections(): return error_response("No channel")
    try:
        data = request.json
        axis = int(data.get('axis', 0))
        speed = float(data.get('speed', 0))
        dist = float(data.get('dist', 0))

        shared.cnc_stat.poll()
        if shared.cnc_stat.task_state == linuxcnc.STATE_ESTOP: return error_response("ESTOP Active")
        if shared.cnc_stat.task_state != linuxcnc.STATE_ON: return error_response("Power OFF")

        if speed == 0.0:
            jjogmode = 1 if shared.cnc_stat.motion_mode == 1 else 0
            shared.cnc_cmd.jog(linuxcnc.JOG_STOP, jjogmode, axis)
            return success_response()

        if shared.cnc_stat.task_mode != linuxcnc.MODE_MANUAL:
            shared.cnc_cmd.mode(linuxcnc.MODE_MANUAL)
            shared.cnc_cmd.wait_complete()

        is_homed = False
        try:
            if hasattr(shared.cnc_stat, 'homed') and len(shared.cnc_stat.homed) > axis:
                is_homed = bool(shared.cnc_stat.homed[axis])
        except: pass

        if is_homed:
            if shared.cnc_stat.motion_mode != 3:
                shared.cnc_cmd.teleop_enable(1)
                shared.cnc_cmd.wait_complete()
            jjogmode = 0
        else:
            if shared.cnc_stat.motion_mode != 1:
                shared.cnc_cmd.teleop_enable(0)
                shared.cnc_cmd.wait_complete()
            jjogmode = 1

        final_speed = abs(speed)
        if dist > 0:
            direction = 1.0 if speed > 0 else -1.0
            shared.cnc_cmd.jog(linuxcnc.JOG_INCREMENT, jjogmode, axis, final_speed, dist * direction)
        else:
            shared.cnc_cmd.jog(linuxcnc.JOG_CONTINUOUS, jjogmode, axis, speed)

        return success_response()
    except Exception as e:
        return error_response(str(e))


@machine_bp.route('/v2/machine/reset', methods=['POST'])
def v2_machine_reset():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        shared.cnc_stat.poll()
        if shared.cnc_stat.task_state == linuxcnc.STATE_ESTOP:
            shared.cnc_cmd.state(linuxcnc.STATE_ESTOP_RESET)
        elif shared.cnc_stat.task_state == linuxcnc.STATE_ON:
            shared.cnc_cmd.state(linuxcnc.STATE_OFF)
        else:
            shared.cnc_cmd.state(linuxcnc.STATE_ON)
        return success_response()
    except Exception as e: return error_response(f"Reset Fail: {e}")


@machine_bp.route('/v2/machine/estop', methods=['POST'])
def v2_machine_estop():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        shared.cnc_cmd.state(linuxcnc.STATE_ESTOP)
        return success_response()
    except Exception as e: return error_response(f"Estop Fail: {e}")


@machine_bp.route('/v2/machine/home', methods=['POST'])
def v2_machine_home():
    """回原點：-1 = 全軸，0~5 = 單軸"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        shared.cnc_stat.poll()
        if shared.cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine must be ON to home")
        data = request.json or {}
        axis = int(data.get('axis', -1))
        shared.cnc_cmd.mode(linuxcnc.MODE_MANUAL)
        shared.cnc_cmd.wait_complete()
        shared.cnc_cmd.teleop_enable(0)
        shared.cnc_cmd.wait_complete()
        shared.cnc_cmd.home(axis)
        app_log('CMD', f'Home axis={axis}')
        return success_response()
    except Exception as e:
        return error_response(f"Home Fail: {e}")


@machine_bp.route('/v2/machine/mode', methods=['POST'])
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
        shared.cnc_cmd.mode(mode_map[mode])
        shared.cnc_cmd.wait_complete()
        app_log('CMD', f'Mode changed to {mode}')
        return success_response({"mode": mode})
    except Exception as e:
        return error_response(f"Mode Fail: {e}")


@machine_bp.route('/v2/mdi', methods=['POST'])
def v2_mdi():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        command = data.get('command', '').strip()
        if not command:
            return error_response("No command provided", 400)

        shared.cnc_stat.poll()
        if shared.cnc_stat.task_state == linuxcnc.STATE_ESTOP:
            return error_response("ESTOP Active", 400)
        if shared.cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine OFF", 400)

        if shared.cnc_stat.task_mode != linuxcnc.MODE_MDI:
            shared.cnc_cmd.mode(linuxcnc.MODE_MDI)
            shared.cnc_cmd.wait_complete()

        shared.cnc_cmd.mdi(command)
        shared.cnc_cmd.wait_complete()
        _update_wcs_cache_from_g10(command)
        app_log('CMD', f"MDI: {command}")
        return success_response({"command": command})
    except Exception as e:
        return error_response(f"MDI Fail: {e}")


@machine_bp.route('/v2/override/feed', methods=['POST'])
def v2_override_feed():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        value = float(data.get('value', 100.0))
        scale = max(0.0, min(value / 100.0, 2.0))
        shared.cnc_cmd.feedrate(scale)
        app_log('CMD', f'Feed Override: {value}%')
        return success_response({"feed_override": value})
    except Exception as e:
        return error_response(f"Feed Override Fail: {e}")


@machine_bp.route('/v2/override/spindle', methods=['POST'])
def v2_override_spindle():
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.json or {}
        value = float(data.get('value', 100.0))
        scale = max(0.0, min(value / 100.0, 2.0))
        shared.cnc_cmd.spindleoverride(scale)
        app_log('CMD', f'Spindle Override: {value}%')
        return success_response({"spindle_override": value})
    except Exception as e:
        return error_response(f"Spindle Override Fail: {e}")


@machine_bp.route('/v2/program/block_delete', methods=['POST'])
def v2_block_delete():
    """切換 Block Delete 開關"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.get_json(silent=True) or {}
        value = bool(data.get('value', False))
        shared.cnc_cmd.set_block_delete(value)
        app_log('CMD', f'Block Delete: {value}')
        return success_response({"block_delete": value})
    except Exception as e:
        return error_response(f"Block Delete Fail: {e}")


@machine_bp.route('/v2/program/optional_stop', methods=['POST'])
def v2_optional_stop():
    """切換 Optional Stop (M01) 開關"""
    if not ensure_cnc_connections(): return error_response("No connection")
    try:
        data = request.get_json(silent=True) or {}
        value = bool(data.get('value', False))
        shared.cnc_cmd.set_optional_stop(value)
        app_log('CMD', f'Optional Stop: {value}')
        return success_response({"optional_stop": value})
    except Exception as e:
        return error_response(f"Optional Stop Fail: {e}")
