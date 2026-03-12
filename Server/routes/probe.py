# [2026-03-12] 從 server.py 拆分：探測循環 + HAL 設定

import math
import subprocess
from flask import Blueprint, request
import shared
from shared import (
    success_response, error_response, app_log,
    ensure_cnc_connections, linuxcnc,
    probe_send_mdi_and_wait, probe_get_result
)

probe_bp = Blueprint('probe', __name__)


# ==============================================================================
# 探測內部函式（9 個）
# ==============================================================================

def _probe_edge(direction, search_speed, max_xy_dist, max_z_dist,
                xy_clearance, z_clearance, extra_depth, probe_radius=0):
    """[2026-03-04] 單軸邊緣探測"""
    axis_map = {
        'N': ('Y', -(max_xy_dist)),
        'S': ('Y', max_xy_dist),
        'E': ('X', -(max_xy_dist)),
        'W': ('X', max_xy_dist)
    }
    if direction not in axis_map:
        return {'tripped': False, 'error': f'Invalid edge direction: {direction}'}

    axis, dist = axis_map[direction]
    retract = xy_clearance if dist > 0 else -xy_clearance
    z_drop = -(abs(extra_depth) + abs(z_clearance))

    err = probe_send_mdi_and_wait(f"G91 G0 Z{z_drop:.4f}")
    if err:
        probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0, 'error': f'Z drop failed: {err}'}

    shared.cnc_stat.poll()
    if bool(getattr(shared.cnc_stat, 'probe_val', 0)):
        probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Probe input is already HIGH before G38.2 ({direction}). Check probe wiring or clear obstruction.'}

    err = probe_send_mdi_and_wait(f"G91 G38.2 {axis}{dist:.4f} F{search_speed:.1f}", timeout=60)
    if err:
        probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0, 'error': err}
    result = probe_get_result()
    if not result['tripped']:
        probe_send_mdi_and_wait(f"G91 G0 {axis}{-dist:.4f}")
        probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        probe_send_mdi_and_wait("G90")
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Probe not tripped ({direction})'}

    if probe_radius > 0:
        sign = 1 if dist > 0 else -1
        if axis == 'X':
            result['x'] += sign * probe_radius
        else:
            result['y'] += sign * probe_radius

    probe_send_mdi_and_wait(f"G91 G0 {axis}{-retract:.4f}")
    probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
    probe_send_mdi_and_wait("G90")

    return result


def _probe_outside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                          xy_clearance, z_clearance, extra_depth, probe_radius=0):
    """[2026-03-04] 雙軸外角探測"""
    corner_map = {
        'NW': ('W', 'N'), 'NE': ('E', 'N'),
        'SW': ('W', 'S'), 'SE': ('E', 'S')
    }
    if direction not in corner_map:
        return {'tripped': False, 'error': f'Invalid corner direction: {direction}'}

    dir1, dir2 = corner_map[direction]

    shared.cnc_stat.poll()
    start_x = shared.cnc_stat.actual_position[0]
    start_y = shared.cnc_stat.actual_position[1]

    r1 = _probe_edge(dir1, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r1['tripped']:
        return r1

    probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    r2 = _probe_edge(dir2, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r2['tripped']:
        return r2

    probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    return {'tripped': True, 'x': r1['x'], 'y': r2['y'], 'z': r1['z']}


def _probe_inside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                         xy_clearance, z_clearance, extra_depth, probe_radius=0):
    """[2026-03-04] 雙軸內角探測"""
    inside_map = {
        'NW': ('E', 'S'), 'NE': ('W', 'S'),
        'SW': ('E', 'N'), 'SE': ('W', 'N')
    }
    if direction not in inside_map:
        return {'tripped': False, 'error': f'Invalid inside corner direction: {direction}'}

    dir1, dir2 = inside_map[direction]

    shared.cnc_stat.poll()
    start_x = shared.cnc_stat.actual_position[0]
    start_y = shared.cnc_stat.actual_position[1]

    r1 = _probe_edge(dir1, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r1['tripped']:
        return r1

    probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    r2 = _probe_edge(dir2, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r2['tripped']:
        return r2

    probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    return {'tripped': True, 'x': r1['x'], 'y': r2['y'], 'z': r1['z']}


def _probe_center(search_speed, max_xy_dist, max_z_dist,
                  xy_clearance, z_clearance, extra_depth, axes='XY', probe_radius=0):
    """[2026-03-04] 中心/口袋探測（從內向外探壁）"""
    shared.cnc_stat.poll()
    start_x = shared.cnc_stat.actual_position[0]
    start_y = shared.cnc_stat.actual_position[1]
    first_z = None
    center_x = start_x
    center_y = start_y
    width_x = 0
    width_y = 0

    if axes in ('X', 'XY'):
        r_xp = _probe_edge('W', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_xp['tripped']: return r_xp
        if first_z is None: first_z = r_xp['z']
        probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
        r_xn = _probe_edge('E', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_xn['tripped']: return r_xn
        probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
        center_x = (r_xp['x'] + r_xn['x']) / 2.0
        width_x = abs(r_xp['x'] - r_xn['x'])

    if axes in ('Y', 'XY'):
        r_yp = _probe_edge('S', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_yp['tripped']: return r_yp
        if first_z is None: first_z = r_yp['z']
        probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
        r_yn = _probe_edge('N', search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, probe_radius)
        if not r_yn['tripped']: return r_yn
        probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
        center_y = (r_yp['y'] + r_yn['y']) / 2.0
        width_y = abs(r_yp['y'] - r_yn['y'])

    return {
        'tripped': True, 'x': center_x, 'y': center_y,
        'z': first_z if first_z is not None else 0,
        'width_x': width_x, 'width_y': width_y
    }


def _probe_boss(search_speed, max_xy_dist, max_z_dist,
                xy_clearance, z_clearance, extra_depth, axes='XY', diameter=0, probe_radius=0):
    """[2026-03-04] Boss 凸台探測"""
    shared.cnc_stat.poll()
    start_x = shared.cnc_stat.actual_position[0]
    start_y = shared.cnc_stat.actual_position[1]
    z_drop = -(abs(extra_depth) + abs(z_clearance))
    move_dist = (diameter / 2.0 + xy_clearance) if diameter > 0 else max_xy_dist
    probe_travel = move_dist + max_xy_dist

    all_sides = {
        'xp': ('X', move_dist, -probe_travel),
        'xn': ('X', -move_dist, probe_travel),
        'yp': ('Y', move_dist, -probe_travel),
        'yn': ('Y', -move_dist, probe_travel),
    }
    if axes == 'X': side_keys = ['xp', 'xn']
    elif axes == 'Y': side_keys = ['yp', 'yn']
    else: side_keys = ['xp', 'xn', 'yp', 'yn']

    touch_points = {}
    for label in side_keys:
        axis, move_out, probe_dist = all_sides[label]
        probe_send_mdi_and_wait(f"G91 G0 {axis}{move_out:.4f}")
        probe_send_mdi_and_wait(f"G91 G0 Z{z_drop:.4f}")
        probe_send_mdi_and_wait(f"G91 G38.2 {axis}{probe_dist:.4f} F{search_speed:.1f}", timeout=30)
        r = probe_get_result()
        if not r['tripped']:
            retract = xy_clearance if probe_dist > 0 else -xy_clearance
            probe_send_mdi_and_wait(f"G91 G0 {axis}{retract:.4f}")
            probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
            probe_send_mdi_and_wait("G90")
            probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
            return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                    'error': f'Boss probe not tripped ({label})'}
        if probe_radius > 0:
            sign = 1 if probe_dist > 0 else -1
            if axis == 'X': r['x'] += sign * probe_radius
            else: r['y'] += sign * probe_radius
        touch_points[label] = r
        retract = xy_clearance if move_out > 0 else -xy_clearance
        probe_send_mdi_and_wait(f"G91 G0 {axis}{retract:.4f}")
        probe_send_mdi_and_wait(f"G91 G0 Z{-z_drop:.4f}")
        probe_send_mdi_and_wait("G90")
        probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    center_x, center_y, width_x, width_y = start_x, start_y, 0, 0
    if 'xp' in touch_points and 'xn' in touch_points:
        center_x = (touch_points['xp']['x'] + touch_points['xn']['x']) / 2.0
        width_x = abs(touch_points['xp']['x'] - touch_points['xn']['x'])
    if 'yp' in touch_points and 'yn' in touch_points:
        center_y = (touch_points['yp']['y'] + touch_points['yn']['y']) / 2.0
        width_y = abs(touch_points['yp']['y'] - touch_points['yn']['y'])

    return {
        'tripped': True, 'x': center_x, 'y': center_y,
        'z': touch_points[side_keys[0]]['z'],
        'width_x': width_x, 'width_y': width_y
    }


def _probe_edge_angle(edge, search_speed, max_xy_dist, max_z_dist,
                      xy_clearance, z_clearance, extra_depth, edge_width=0, probe_radius=0):
    """[2026-03-04] Edge Angle 邊角角度探測"""
    shared.cnc_stat.poll()
    start_x = shared.cnc_stat.actual_position[0]
    start_y = shared.cnc_stat.actual_position[1]
    spacing = edge_width if edge_width > 0 else xy_clearance * 3.0

    edge_config = {
        'NW': {'probe_dir': 'N', 'move_axis': 'X', 'move_dist': spacing},
        'N':  {'probe_dir': 'N', 'move_axis': 'X', 'move_dist': spacing},
        'NE': {'probe_dir': 'N', 'move_axis': 'X', 'move_dist': -spacing},
        'W':  {'probe_dir': 'W', 'move_axis': 'Y', 'move_dist': spacing},
        'E':  {'probe_dir': 'E', 'move_axis': 'Y', 'move_dist': spacing},
        'SW': {'probe_dir': 'S', 'move_axis': 'X', 'move_dist': spacing},
        'S':  {'probe_dir': 'S', 'move_axis': 'X', 'move_dist': spacing},
        'SE': {'probe_dir': 'S', 'move_axis': 'X', 'move_dist': -spacing},
    }

    if edge == 'CENTER':
        result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                               xy_clearance, z_clearance, extra_depth, 'XY', probe_radius)
        result['angle'] = 0
        result['edge_width'] = 0
        return result

    cfg = edge_config.get(edge)
    if not cfg:
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Invalid edge_angle edge: {edge}'}

    r1 = _probe_edge(cfg['probe_dir'], search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r1['tripped']: return r1
    probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    probe_send_mdi_and_wait(f"G91 G0 {cfg['move_axis']}{cfg['move_dist']:.4f}")
    probe_send_mdi_and_wait("G90")

    r2 = _probe_edge(cfg['probe_dir'], search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, probe_radius)
    if not r2['tripped']:
        probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")
        return r2
    probe_send_mdi_and_wait(f"G90 G0 X{start_x:.4f} Y{start_y:.4f}")

    dx = r2['x'] - r1['x']
    dy = r2['y'] - r1['y']
    if cfg['move_axis'] == 'X':
        angle_rad = math.atan2(dy, dx)
    else:
        angle_rad = math.atan2(dx, dy)
    angle_deg = math.degrees(angle_rad)

    if cfg['probe_dir'] in ('N', 'S'):
        ew = abs(r2['y'] - r1['y'])
    else:
        ew = abs(r2['x'] - r1['x'])

    return {
        'tripped': True,
        'x': (r1['x'] + r2['x']) / 2.0, 'y': (r1['y'] + r2['y']) / 2.0,
        'z': r1['z'], 'angle': angle_deg, 'edge_width': ew
    }


def _probe_calibrate(cal_type, search_speed, max_xy_dist, max_z_dist,
                     xy_clearance, z_clearance, extra_depth, cal_width=0, probe_radius=0):
    """[2026-03-04] Probe Calibrate 校正探測"""
    if cal_type in ('ring_inside', 'xy_turret', 'x_bore', 'square_inside'):
        return _probe_center(search_speed, max_xy_dist, max_z_dist,
                             xy_clearance, z_clearance, extra_depth, 'XY', probe_radius)
    elif cal_type in ('ring_outside', 'square_outside'):
        return _probe_boss(search_speed, max_xy_dist, max_z_dist,
                           xy_clearance, z_clearance, extra_depth, 'XY', cal_width, probe_radius)
    elif cal_type in ('avg_xy', 'x_error', 'y_error'):
        axes = 'XY'
        if cal_type == 'x_error': axes = 'X'
        elif cal_type == 'y_error': axes = 'Y'
        return _probe_center(search_speed, max_xy_dist, max_z_dist,
                             xy_clearance, z_clearance, extra_depth, axes, probe_radius)
    elif cal_type == 'x_edge':
        return _probe_center(search_speed, max_xy_dist, max_z_dist,
                             xy_clearance, z_clearance, extra_depth, 'X', probe_radius)
    else:
        return {'tripped': False, 'x': 0, 'y': 0, 'z': 0,
                'error': f'Invalid cal_type: {cal_type}'}


# ==============================================================================
# 路由
# ==============================================================================

@probe_bp.route('/v2/probe/run', methods=['POST'])
def v2_probe_run():
    """探測循環端點：edge/corner/center/boss/pocket/ridge/valley/edge_angle/calibrate"""
    if not ensure_cnc_connections():
        return error_response("No connection")
    try:
        shared.cnc_stat.poll()
        if shared.cnc_stat.task_state == linuxcnc.STATE_ESTOP:
            return error_response("ESTOP Active", 400)
        if shared.cnc_stat.task_state != linuxcnc.STATE_ON:
            return error_response("Machine OFF", 400)

        data = request.json or {}
        probe_type = data.get('probe_type', '')
        direction = data.get('direction', '').upper()
        search_speed = float(data.get('search_speed', 50.0))
        max_xy_dist = float(data.get('max_xy_distance', 20.0))
        max_z_dist = float(data.get('max_z_distance', 20.0))
        xy_clearance = float(data.get('xy_clearance', 5.0))
        z_clearance = float(data.get('z_clearance', 5.0))
        extra_depth = float(data.get('extra_depth', 2.0))
        diameter = float(data.get('diameter', 0))
        offset_x = float(data.get('offset_x', 0))
        offset_y = float(data.get('offset_y', 0))
        probe_tool = int(data.get('probe_tool', 0))

        if shared.cnc_stat.task_mode != linuxcnc.MODE_MDI:
            shared.cnc_cmd.mode(linuxcnc.MODE_MDI)
            shared.cnc_cmd.wait_complete()

        probe_radius = 0.0
        if probe_tool > 0:
            g43_err = probe_send_mdi_and_wait(f"G43 H{probe_tool}", timeout=5)
            if g43_err:
                return error_response(f"G43 H{probe_tool} failed: {g43_err}")
            try:
                shared.cnc_stat.poll()
                if probe_tool < len(shared.cnc_stat.tool_table):
                    probe_diameter = float(shared.cnc_stat.tool_table[probe_tool].diameter)
                    probe_radius = abs(probe_diameter) / 2.0
                    app_log('CMD', f'Probe tool #{probe_tool}: diameter={probe_diameter:.4f} radius={probe_radius:.4f}')
            except Exception as e:
                app_log('WARN', f'Failed to read probe tool diameter: {e}')

        result = None
        if probe_type == 'edge':
            result = _probe_edge(direction, search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, probe_radius)
        elif probe_type == 'outside_corner':
            result = _probe_outside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                                           xy_clearance, z_clearance, extra_depth, probe_radius)
        elif probe_type == 'inside_corner':
            result = _probe_inside_corner(direction, search_speed, max_xy_dist, max_z_dist,
                                          xy_clearance, z_clearance, extra_depth, probe_radius)
        elif probe_type == 'inside_edge':
            inside_edge_map = {'N': 'S', 'S': 'N', 'E': 'W', 'W': 'E'}
            mapped_dir = inside_edge_map.get(direction, direction)
            result = _probe_edge(mapped_dir, search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, probe_radius)
        elif probe_type == 'center':
            result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                                   xy_clearance, z_clearance, extra_depth, 'XY', probe_radius)
        elif probe_type in ('boss_x', 'boss_y', 'boss_xy', 'boss'):
            axes = 'XY'
            if probe_type == 'boss_x': axes = 'X'
            elif probe_type == 'boss_y': axes = 'Y'
            if offset_x != 0 or offset_y != 0:
                probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                probe_send_mdi_and_wait("G90")
            result = _probe_boss(search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, axes, diameter, probe_radius)
        elif probe_type in ('pocket_x', 'pocket_y', 'pocket_xy', 'pocket'):
            axes = 'XY'
            if probe_type == 'pocket_x': axes = 'X'
            elif probe_type == 'pocket_y': axes = 'Y'
            if offset_x != 0 or offset_y != 0:
                probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                probe_send_mdi_and_wait("G90")
            result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                                   xy_clearance, z_clearance, extra_depth, axes, probe_radius)
        elif probe_type in ('ridge_x', 'ridge_y', 'ridge_xy'):
            axes = 'XY'
            if probe_type == 'ridge_x': axes = 'X'
            elif probe_type == 'ridge_y': axes = 'Y'
            if offset_x != 0 or offset_y != 0:
                probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                probe_send_mdi_and_wait("G90")
            result = _probe_boss(search_speed, max_xy_dist, max_z_dist,
                                 xy_clearance, z_clearance, extra_depth, axes, diameter, probe_radius)
        elif probe_type in ('valley_x', 'valley_y', 'valley_xy'):
            axes = 'XY'
            if probe_type == 'valley_x': axes = 'X'
            elif probe_type == 'valley_y': axes = 'Y'
            if offset_x != 0 or offset_y != 0:
                probe_send_mdi_and_wait(f"G91 G0 X{offset_x:.4f} Y{offset_y:.4f}")
                probe_send_mdi_and_wait("G90")
            result = _probe_center(search_speed, max_xy_dist, max_z_dist,
                                   xy_clearance, z_clearance, extra_depth, axes, probe_radius)
        elif probe_type == 'edge_angle':
            edge_w = float(data.get('edge_width', 0))
            result = _probe_edge_angle(direction, search_speed, max_xy_dist, max_z_dist,
                                       xy_clearance, z_clearance, extra_depth, edge_w, probe_radius)
        elif probe_type in ('cal_ring_inside', 'cal_ring_outside',
                            'cal_square_inside', 'cal_square_outside',
                            'cal_avg_xy', 'cal_x_error', 'cal_y_error',
                            'cal_xy_turret', 'cal_x_edge', 'cal_x_bore'):
            cal_type = probe_type.replace('cal_', '')
            cal_width = float(data.get('calibration_width', 0))
            result = _probe_calibrate(cal_type, search_speed, max_xy_dist, max_z_dist,
                                      xy_clearance, z_clearance, extra_depth, cal_width, probe_radius)
        else:
            return error_response(f"Invalid probe_type: {probe_type}", 400)

        error_msg = result.get('error', '')
        if error_msg:
            app_log('WARN', f'Probe {probe_type}/{direction}: {error_msg}')
        else:
            app_log('CMD', f'Probe {probe_type}/{direction}: '
                    f'X={result["x"]:.4f} Y={result["y"]:.4f} Z={result["z"]:.4f}')

        wcs = data.get('wcs', '')
        probe_only = bool(data.get('probe_position_only', False))
        if result.get('tripped') and not error_msg and wcs and not probe_only:
            wcs_map = {'G54':1,'G55':2,'G56':3,'G57':4,'G58':5,'G59':6,
                       'G59.1':7,'G59.2':8,'G59.3':9}
            p_idx = wcs_map.get(wcs, 0)
            if p_idx > 0:
                g10_cmd = f"G10 L20 P{p_idx} X{result['x']:.4f} Y{result['y']:.4f} Z{result['z']:.4f}"
                wcs_err = probe_send_mdi_and_wait(g10_cmd, timeout=5)
                if wcs_err:
                    app_log('WARN', f'Probe WCS write failed: {wcs_err}')
                else:
                    app_log('CMD', f'Probe → {wcs}: {g10_cmd}')

        return success_response({
            'Tripped': result.get('tripped', False),
            'X': result.get('x', 0), 'Y': result.get('y', 0), 'Z': result.get('z', 0),
            'Error': error_msg,
            'Angle': result.get('angle', 0), 'EdgeWidth': result.get('edge_width', 0),
            'WidthX': result.get('width_x', 0), 'WidthY': result.get('width_y', 0)
        })
    except Exception as e:
        app_log('ERROR', f'Probe Fail: {e}')
        return error_response(f"Probe Fail: {e}")


@probe_bp.route('/v2/hal/setp', methods=['POST'])
def v2_hal_setp():
    """[2026-03-05] 通用 halcmd setp：設定 HAL signal 值"""
    try:
        data = request.json or {}
        signal_name = data.get('signal', '').strip()
        value = data.get('value')

        if not signal_name or value is None:
            return error_response("Missing 'signal' or 'value'", 400)

        allowed_prefixes = ('sim-probe-', 'probe-in')
        if not any(signal_name.startswith(p) for p in allowed_prefixes):
            return error_response(f"Signal '{signal_name}' not allowed", 403)

        result = subprocess.run(
            ['halcmd', 'sets', signal_name, str(value)],
            capture_output=True, text=True, timeout=5
        )

        if result.returncode != 0:
            return error_response(f"halcmd failed: {result.stderr.strip()}", 500)

        app_log('CMD', f'HAL setp: {signal_name} = {value}')
        return success_response({'signal': signal_name, 'value': value})
    except Exception as e:
        return error_response(f"HAL setp failed: {e}")
