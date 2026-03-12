# [2026-03-12] 從 server.py 拆分：ATC 刀庫控制（13 路由）

import os
import logging
import configparser
from flask import Blueprint, request
import shared
from shared import (
    LINUXCNC_INI_PATH,
    success_response, error_response, app_log,
    ensure_cnc_connections, linuxcnc,
    probe_send_mdi_and_wait,
    _ini_value, _read_hal_pin, _read_hal_pins_batch, _read_var_params
)

atc_bp = Blueprint('atc', __name__)


def _atc_send_mdi(command, timeout=15):
    """[2026-03-09] ATC 專用 MDI 送出"""
    shared.cnc_stat.poll()
    if shared.cnc_stat.task_state != linuxcnc.STATE_ON:
        return "Machine is OFF"
    if shared.cnc_stat.task_mode != linuxcnc.MODE_MDI:
        shared.cnc_cmd.mode(linuxcnc.MODE_MDI)
        shared.cnc_cmd.wait_complete()
    return probe_send_mdi_and_wait(command, timeout=timeout)


@atc_bp.route('/v2/atc/status', methods=['GET'])
def v2_atc_status():
    """[2026-03-09] 讀取 ATC 刀庫即時狀態"""
    if not ensure_cnc_connections():
        return error_response("Not connected")
    try:
        shared.cnc_stat.poll()

        pockets = 12
        control_mode = 'SERVO'
        try:
            with open(LINUXCNC_INI_PATH, 'r', encoding='utf-8', errors='ignore') as f:
                in_atc = False
                for line in f:
                    stripped = line.strip()
                    if stripped.startswith('['):
                        in_atc = stripped.upper() == '[ATC]'
                    elif in_atc and stripped.upper().startswith('POCKETS'):
                        pockets = int(stripped.split('=')[1].strip())
                    elif in_atc and stripped.upper().startswith('CONTROL_MODE'):
                        control_mode = stripped.split('=')[1].strip().upper()
        except: pass

        do_pins = [f'motion.digital-out-{i:02d}' for i in range(32)]
        di_pins = [f'motion.digital-in-{i:02d}' for i in range(32)]
        all_pins = do_pins + di_pins
        pin_states = _read_hal_pins_batch(all_pins)

        do_states = {}
        di_states = {}
        for i in range(32):
            do_name = f'motion.digital-out-{i:02d}'
            di_name = f'motion.digital-in-{i:02d}'
            if do_name in pin_states: do_states[i] = pin_states[do_name]
            if di_name in pin_states: di_states[i] = pin_states[di_name]

        slot_param_ids = set(range(4001, 4001 + pockets))
        slot_param_ids.add(3990)
        slot_params = _read_var_params(slot_param_ids)

        current_pocket = int(slot_params.get(3990, 0))
        slot_tools = {}
        for i in range(1, pockets + 1):
            slot_tools[i] = int(slot_params.get(4000 + i, 0))

        carousel_angle = 0.0
        try:
            c_raw = _read_hal_pin('motion.analog-out-00')
            if c_raw is not None and c_raw is not False:
                carousel_angle = round(float(c_raw) % 360.0, 2)
        except Exception as e:
            logging.error(f"[ATC] carousel angle read error: {e}")

        data = {
            'Pockets': pockets,
            'CurrentPocket': current_pocket,
            'CarouselAngle': carousel_angle,
            'ToolInSpindle': getattr(shared.cnc_stat, 'tool_in_spindle', 0),
            'ControlMode': control_mode,
            'DO': do_states,
            'DI': di_states,
            'SlotTools': slot_tools,
        }
        return success_response(data)
    except Exception as e:
        return error_response(f"ATC status failed: {e}")


@atc_bp.route('/v2/atc/rotate', methods=['POST'])
def v2_atc_rotate():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        data = request.json or {}
        pocket = data.get('pocket', 1)
        err = _atc_send_mdi(f"M10 P{pocket}")
        if err: return error_response(f"ATC rotate failed: {err}")
        app_log('CMD', f'ATC rotate to pocket {pocket}')
        return success_response({'pocket': pocket})
    except Exception as e:
        return error_response(f"ATC rotate failed: {e}")


@atc_bp.route('/v2/atc/fwd', methods=['POST'])
def v2_atc_fwd():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("M11")
        if err: return error_response(f"ATC fwd failed: {err}")
        app_log('CMD', 'ATC forward one pocket')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC fwd failed: {e}")


@atc_bp.route('/v2/atc/rev', methods=['POST'])
def v2_atc_rev():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("M12")
        if err: return error_response(f"ATC rev failed: {err}")
        app_log('CMD', 'ATC reverse one pocket')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC rev failed: {e}")


@atc_bp.route('/v2/atc/clamp', methods=['POST'])
def v2_atc_clamp():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("M25")
        if err: return error_response(f"ATC clamp failed: {err}")
        app_log('CMD', 'ATC clamp tool')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC clamp failed: {e}")


@atc_bp.route('/v2/atc/unclamp', methods=['POST'])
def v2_atc_unclamp():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("M24")
        if err: return error_response(f"ATC unclamp failed: {err}")
        app_log('CMD', 'ATC unclamp tool')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC unclamp failed: {e}")


@atc_bp.route('/v2/atc/extend', methods=['POST'])
def v2_atc_extend():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("o<extendatc> call")
        if err: return error_response(f"ATC extend failed: {err}")
        app_log('CMD', 'ATC extend carousel')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC extend failed: {e}")


@atc_bp.route('/v2/atc/retract', methods=['POST'])
def v2_atc_retract():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("o<retractatc> call")
        if err: return error_response(f"ATC retract failed: {err}")
        app_log('CMD', 'ATC retract carousel')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC retract failed: {e}")


@atc_bp.route('/v2/atc/ref', methods=['POST'])
def v2_atc_ref():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("M13", timeout=30)
        if err: return error_response(f"ATC ref failed: {err}")
        app_log('CMD', 'ATC reference carousel')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC ref failed: {e}")


@atc_bp.route('/v2/atc/head_up', methods=['POST'])
def v2_atc_head_up():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("o<move_head_above_carousel> call")
        if err: return error_response(f"ATC head_up failed: {err}")
        app_log('CMD', 'ATC move head above carousel')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC head_up failed: {e}")


@atc_bp.route('/v2/atc/head_down', methods=['POST'])
def v2_atc_head_down():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("o<move_tool_to_carousel_height> call")
        if err: return error_response(f"ATC head_down failed: {err}")
        app_log('CMD', 'ATC move tool to carousel height')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC head_down failed: {e}")


@atc_bp.route('/v2/atc/orient', methods=['POST'])
def v2_atc_orient():
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        err = _atc_send_mdi("M19")
        if err: return error_response(f"ATC orient failed: {err}")
        app_log('CMD', 'ATC orient spindle')
        return success_response({})
    except Exception as e:
        return error_response(f"ATC orient failed: {e}")


@atc_bp.route('/v2/atc/slot', methods=['POST'])
def v2_atc_slot():
    """設定刀位對應表"""
    if not ensure_cnc_connections(): return error_response("Not connected")
    try:
        data = request.json or {}
        slot = int(data.get('slot', 0))
        tool_num = int(data.get('tool_number', 0))
        if slot < 1 or slot > 24:
            return error_response("Invalid slot (1~24)")
        param_id = 4000 + slot
        err = _atc_send_mdi(f"#{param_id}={tool_num}")
        if err: return error_response(f"ATC slot set failed: {err}")
        app_log('CMD', f'ATC slot {slot} = T{tool_num}')
        return success_response({'slot': slot, 'tool_number': tool_num})
    except Exception as e:
        return error_response(f"ATC slot set failed: {e}")
