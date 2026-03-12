#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import subprocess
import os
import sys
import csv
import re
import json
import xml.etree.ElementTree as ET
from enum import Enum  # <--- 維持這樣最標準

# 接著定義您的 Enum
class DeviceCategory(str, Enum):
    SERVO = "Servo"
    PULSE_GEN = "PulseGen"  # [2026-03-05] 新增：台達 5621 脈波產生器
    DI_DO = "DI+DO"  # 對應 C# 的 DiDo
    DI = "DigIn"
    DO = "DigOut"
    MPG = "MPG"
    DA = "DA"
    AD = "AD"
    COUPLER = "Coupler"
    UNKNOWN = "Unknown"

# ==========================================
# 設定區
# ==========================================

OUTPUT_XML_FILE = "ethercat-conf.xml"
OUTPUT_REPORT_FILE = "scan_report.csv"
OUTPUT_JSON_FILE = "frontend_topology.json"
ESI_FOLDER = "./esi_snippets"

# ==========================================
# 關鍵 PDO 定義
# ==========================================
CRITICAL_PDOS_LIST = [
    # --- Servo ---
    {"idx": "6040", "sub": "00", "bits": 16, "name": "ctrl-word",      "type": "out"},
    {"idx": "607a", "sub": "00", "bits": 32, "name": "target-pos",     "type": "out"},
    {"idx": "6060", "sub": "00", "bits": 8,  "name": "op-mode",        "type": "out"},
    {"idx": "6041", "sub": "00", "bits": 16, "name": "status-word",    "type": "in"},
    {"idx": "6064", "sub": "00", "bits": 32, "name": "actual-pos",     "type": "in"},
    {"idx": "60fd", "sub": "00", "bits": 32, "name": "digital-inputs", "type": "in"},

    # --- Hybrid IO ---
    {"idx": "7000", "sub": "01", "bits": 32, "name": "dout-32",        "type": "out"},
    {"idx": "7000", "sub": "00", "bits": 32, "name": "dout-pack",      "type": "out"},
    {"idx": "6000", "sub": "01", "bits": 32, "name": "din-32",         "type": "in"},
    {"idx": "6000", "sub": "00", "bits": 32, "name": "din-pack",       "type": "in"},

    # --- DA ---
    {"idx": "6411", "sub": "01", "bits": 16, "name": "da-ch1",         "type": "out"},
    {"idx": "6411", "sub": "02", "bits": 16, "name": "da-ch2",         "type": "out"},
    
    # --- AD ---
    {"idx": "6401", "sub": "01", "bits": 16, "name": "ad-ch1",         "type": "in"},
    {"idx": "6401", "sub": "02", "bits": 16, "name": "ad-ch2",         "type": "in"},
]

# ==========================================
# 備援模板
# ==========================================

TEMPLATE_FALLBACK_SERVO = """
    <slave idx="{POS}" type="fallback_servo" vid="{VID}" pid="{PID}" configPdos="true">
      <dcConf assignActivate="300" sync0Cycle="*1" sync0Shift="0"/>
      <syncManager idx="2" dir="out">
        <pdoEntry idx="6040" subIdx="00" bitLen="16" halPin="ctrl-word"/>
        <pdoEntry idx="607a" subIdx="00" bitLen="32" halPin="target-pos"/>
        <pdoEntry idx="6060" subIdx="00" bitLen="8" halPin="op-mode"/>
      </syncManager>
      <syncManager idx="3" dir="in">
        <pdoEntry idx="6041" subIdx="00" bitLen="16" halPin="status-word"/>
        <pdoEntry idx="6064" subIdx="00" bitLen="32" halPin="actual-pos"/>
        <pdoEntry idx="60fd" subIdx="00" bitLen="32" halPin="digital-inputs"/>
      </syncManager>
    </slave>
"""

TEMPLATE_FALLBACK_HYBRID_32 = """
    <slave idx="{POS}" type="fallback_hybrid_32" vid="{VID}" pid="{PID}" configPdos="true">
      <syncManager idx="2" dir="out">
        <pdoEntry idx="7000" subIdx="01" bitLen="32" halPin="dout-32"/>
      </syncManager>
      <syncManager idx="3" dir="in">
        <pdoEntry idx="6000" subIdx="01" bitLen="32" halPin="din-32"/>
      </syncManager>
    </slave>
"""

TEMPLATE_FALLBACK_MPG = """
    <slave idx="{POS}" type="fallback_mpg" vid="{VID}" pid="{PID}" configPdos="true">
      <syncManager idx="3" dir="in">
        <pdoEntry idx="6000" subIdx="01" bitLen="16" halPin="mpg-buttons"/>
        <pdoEntry idx="6064" subIdx="00" bitLen="32" halPin="mpg-counts"/>
      </syncManager>
    </slave>
"""

TEMPLATE_FALLBACK_AO = """
    <slave idx="{POS}" type="fallback_ao" vid="{VID}" pid="{PID}" configPdos="true">
      <syncManager idx="2" dir="out">
        <pdoEntry idx="7000" subIdx="01" bitLen="16" halPin="ao-ch1"/>
        <pdoEntry idx="7000" subIdx="02" bitLen="16" halPin="ao-ch2"/>
        <pdoEntry idx="7000" subIdx="03" bitLen="16" halPin="ao-ch3"/>
        <pdoEntry idx="7000" subIdx="04" bitLen="16" halPin="ao-ch4"/>
      </syncManager>
    </slave>
"""

TEMPLATE_FALLBACK_COUPLER = """
    <slave idx="{POS}" type="fallback_coupler" vid="{VID}" pid="{PID}" configPdos="false">
    </slave>
"""

TEMPLATE_HEADER = """<masters>
  <master idx="0" appTimePeriod="1000000" refClockSyncCycles="1000">
"""
TEMPLATE_FOOTER = """  </master>
</masters>
"""

# ==========================================
# Helper Functions
# ==========================================

def hex_str_to_int(s):
    s = s.lower().replace("#x", "0x").strip()
    try: return int(s, 16)
    except: return 0

def int_to_hex_str(i):
    return f"0x{i:08x}"

# ==========================================
# Classification Logic
# ==========================================
def determine_category(g_type, found_pdos, name_raw, model_raw, pid_str=""):
    name_upper = name_raw.upper()
    model_upper = model_raw.upper()
    pdo_keys = " ".join(found_pdos)

    # 1. 強制例外清單 (針對特定型號)
    if "R2-EC0902" in model_upper or "R2-EC0902" in name_upper:
        return DeviceCategory.DI_DO

    # [2026-03-05] 台達 5621 脈波產生器（有 CiA 402 但無 6060 PDO，需在 Servo 判斷前攔截）
    pid_upper = pid_str.upper().replace("0X", "")
    if "5621" in pid_upper or "5621" in model_upper:
        return DeviceCategory.PULSE_GEN

    # 2. Fallback 優先 (根據原始 g_type)
    if g_type == "SERVO": return DeviceCategory.SERVO
    if g_type == "HYBRID_32": return DeviceCategory.DI_DO
    if g_type == "MPG": return DeviceCategory.MPG
    if g_type == "AO": return DeviceCategory.DA
    if g_type == "COUPLER": return DeviceCategory.COUPLER

    # 3. PDO 分析 (特徵識別)
    if "MPG" in name_upper or "HANDWHEEL" in name_upper:
        return DeviceCategory.MPG

    if "6040" in pdo_keys: # CiA 402 Control Word
        return DeviceCategory.SERVO
    
    if "6411" in pdo_keys: # Analog Output
        return DeviceCategory.DA
    
    if "6401" in pdo_keys: # Analog Input
        return DeviceCategory.AD

    # IO 判斷
    # 6000: Read Input 8 Bit, 60FD: Digital Inputs
    # 7000: DO Output
    has_in = "6000" in pdo_keys or "60FD" in pdo_keys
    has_out = "7000" in pdo_keys
    
    if has_in and has_out:
        return DeviceCategory.DI_DO
    if has_in:
        return DeviceCategory.DI
    if has_out:
        return DeviceCategory.DO

    return DeviceCategory.UNKNOWN

# ==========================================
# XML Processing
# ==========================================

def index_esi_files(folder_path):
    library = {} 
    if not os.path.exists(folder_path):
        try: os.makedirs(folder_path)
        except: pass
        return library

    print(f"[INFO] Indexing ESI files in '{folder_path}'...")
    encodings = ['utf-8', 'utf-8-sig', 'iso-8859-1', 'gb18030']

    for filename in os.listdir(folder_path):
        if not filename.lower().endswith(".xml"): continue
        full_path = os.path.join(folder_path, filename)
        
        content = None
        for enc in encodings:
            try:
                with open(full_path, 'r', encoding=enc) as f:
                    content = f.read()
                break
            except: continue
        
        if not content: continue

        try:
            content = re.sub(r'\sxmlns="[^"]+"', '', content, count=1)
            root = ET.fromstring(content)
            
            vendor_id_node = root.find(".//Vendor/Id")
            if vendor_id_node is None: continue
            file_vid = hex_str_to_int(vendor_id_node.text)

            for device in root.findall(".//Descriptions/Devices/Device"):
                type_node = device.find("Type")
                if type_node is not None and 'ProductCode' in type_node.attrib:
                    file_pid = hex_str_to_int(type_node.attrib['ProductCode'])
                    library[(file_vid, file_pid)] = (filename, device)
        except:
            pass
    return library

def extract_pdos_from_esi(device_node, slave_pos, vid, pid):
    xml_lines = []
    found_pdos = []
    
    name_node = device_node.find("Name")
    dev_name = name_node.text if name_node is not None else "Unknown Device"
    
    device_objects = set()
    for obj in device_node.findall(".//Profile/Dictionary/Objects/Object"):
        idx_node = obj.find("Index")
        if idx_node is not None:
            idx_hex = idx_node.text.lower().replace("#x", "").replace("0x", "")
            device_objects.add(idx_hex)

    sm2_entries = [] # Outputs
    sm3_entries = [] # Inputs
    
    for item in CRITICAL_PDOS_LIST:
        idx_key = item["idx"]
        sub_idx = item["sub"]
        
        if idx_key in device_objects:
            entry_xml = f'        <pdoEntry idx="{idx_key}" subIdx="{sub_idx}" bitLen="{item["bits"]}" halPin="{item["name"]}"/>'
            if item["type"] == "out":
                sm2_entries.append(entry_xml)
            else:
                sm3_entries.append(entry_xml)
            found_pdos.append(f"{idx_key}:{sub_idx}")

    if not sm2_entries and not sm3_entries:
        return None, dev_name, [] 

    xml_lines.append(f'    <slave idx="{slave_pos}" type="xml_gen" vid="{vid}" pid="{pid}" configPdos="true">')
    xml_lines.append('      <dcConf assignActivate="300" sync0Cycle="*1" sync0Shift="0"/>')

    if sm2_entries:
        xml_lines.append('      <syncManager idx="2" dir="out">')
        xml_lines.extend(sm2_entries)
        xml_lines.append('      </syncManager>')
    
    if sm3_entries:
        xml_lines.append('      <syncManager idx="3" dir="in">')
        xml_lines.extend(sm3_entries)
        xml_lines.append('      </syncManager>')

    xml_lines.append('    </slave>')
    return "\n".join(xml_lines), dev_name, found_pdos

# ==========================================
# Parsing Logic
# ==========================================

def get_ethercat_verbose_data():
    try:
        # ★★★ 修正點：移除 text=True，獲取原始 bytes ★★★
        # 並且指定完整路徑 /usr/bin/ethercat
        cmd = ["/usr/bin/ethercat", "slaves", "-v"]
        raw_bytes = subprocess.check_output(cmd)
        
        # ★★★ 修正點：使用 errors='replace' 來過濾掉亂碼 ★★★
        # 這樣就不會因為 0xFF 而報錯
        return raw_bytes.decode('utf-8', errors='replace')
        
    except FileNotFoundError:
        print("[ERROR] Command '/usr/bin/ethercat' not found! Please check installation.")
        return ""
    except Exception as e:
        print(f"[ERROR] Failed to execute ethercat command: {e}")
        return ""
        

def parse_verbose_output(raw_data):
    slaves = []
    blocks = re.split(r'={3,}\s*Master\s+\d+,\s*Slave\s+(\d+)\s*={3,}', raw_data)
    if len(blocks) < 2: return []

    for i in range(1, len(blocks), 2):
        pos_idx = blocks[i].strip()
        content = blocks[i+1]
        
        slave_data = {
            "pos": pos_idx,
            "vid": "0x00000000",
            "pid": "0x00000000",
            "name": "Unknown",
            "group": "Unknown",
            "order_no": ""
        }

        vid_match = re.search(r"Vendor I[dD]:\s+(0x[0-9a-fA-F]+)", content)
        pid_match = re.search(r"Product [cCode]+:\s+(0x[0-9a-fA-F]+)", content)
        name_match = re.search(r"Device name:\s+(.+)", content)
        group_match = re.search(r"Group:\s+(.+)", content)
        order_match = re.search(r"Order number:\s+(.+)", content)

        if vid_match: slave_data["vid"] = int_to_hex_str(hex_str_to_int(vid_match.group(1)))
        if pid_match: slave_data["pid"] = int_to_hex_str(hex_str_to_int(pid_match.group(1)))
        if name_match: slave_data["name"] = name_match.group(1).strip()
        if group_match: slave_data["group"] = group_match.group(1).strip()
        if order_match: slave_data["order_no"] = order_match.group(1).strip()

        slaves.append(slave_data)
    return slaves

def guess_device_type(slave):
    name = slave["name"].lower()
    group = slave["group"]
    order = slave["order_no"].upper()
    
    if "R2-EC0902" in order or "R2-EC0902" in name.upper():
        return "HYBRID_32", "Inovance Hybrid IO"
    if "InoServo" in group:
        return "SERVO", "Inovance Servo"
    if "Axis" in group:
        return "SERVO", "Motion Module"
    if "Measuring" in group or "mpg" in name:
        return "MPG", "MPG Module"
    if "AnaOut" in group:
        return "AO", "Analog Out"
    if "adapter" in name or "coupler" in name:
        return "COUPLER", "Bus Coupler"

    return "UNKNOWN", "Generic Device"

def get_fallback_snippet(g_type, pos, vid, pid):
    snippet = ""
    mapped = ""
    if g_type == "SERVO":
        snippet = TEMPLATE_FALLBACK_SERVO
        mapped = "Default Servo PDOs"
    elif g_type == "HYBRID_32":
        snippet = TEMPLATE_FALLBACK_HYBRID_32
        mapped = "IN:6000(32b), OUT:7000(32b)"
    elif g_type == "MPG":
        snippet = TEMPLATE_FALLBACK_MPG
        mapped = "6000, 6064"
    elif g_type == "AO":
        snippet = TEMPLATE_FALLBACK_AO
        mapped = "7000:01-04"
    elif g_type == "COUPLER":
        snippet = TEMPLATE_FALLBACK_COUPLER
        mapped = "None"
    else:
        snippet = TEMPLATE_FALLBACK_SERVO
        mapped = "Default Servo"
    
    return snippet.replace("{POS}", pos).replace("{VID}", vid).replace("{PID}", pid), mapped

# ==========================================
# Main Generation Logic
# ==========================================

def generate_full_config(slaves):
    xml_content = TEMPLATE_HEADER
    report_data = []
    
    esi_lib = index_esi_files(ESI_FOLDER)
    print(f"[INFO] ESI Library Definitions: {len(esi_lib)}")
    
    for slave in slaves:
        pos = slave['pos']
        vid_str = slave['vid']
        pid_str = slave['pid']
        vid_int = hex_str_to_int(vid_str)
        pid_int = hex_str_to_int(pid_str)
        key = (vid_int, pid_int)
        
        name_raw = slave['name']
        xml_generated = None
        current_pdos = []
        detection_method = "Unknown"
        guess_type_code = "UNKNOWN"

        # 1. XML Search
        if key in esi_lib:
            filename, device_node = esi_lib[key]
            xml_snippet, dev_name, found_pdos = extract_pdos_from_esi(device_node, pos, vid_str, pid_str)
            
            if xml_snippet:
                xml_generated = xml_snippet
                current_pdos = found_pdos
                detection_method = f"XML ({filename})"
                guess_type_code = "XML_DEFINED"
                print(f"[MATCH] Slave {pos} -> ESI XML ({filename})")
            else:
                print(f"[WARN] Slave {pos} found XML but 0 PDOs matched. Switching to Fallback.")
                xml_generated = None 

        # 2. Smart Fallback
        if not xml_generated:
            g_type, g_desc = guess_device_type(slave)
            print(f"[FALLBACK] Slave {pos} -> Smart Fallback ({g_type})")
            
            xml_snippet, mapped = get_fallback_snippet(g_type, pos, vid_str, pid_str)
            xml_generated = xml_snippet
            current_pdos = [mapped] 
            detection_method = "Smart Fallback"
            guess_type_code = g_type

        # 3. Determine Category
        # [2026-03-05] 傳入 pid_str 讓 determine_category 可判斷 5621 脈波產生器
        final_category = determine_category(guess_type_code, current_pdos, name_raw, slave['order_no'], pid_str)

        xml_content += xml_generated + "\n"
        
        # ★★★ 關鍵修正：補上 VendorId 和 ProductCode ★★★
        report_data.append({
            'Slave': pos,
            'VendorId': vid_str,       # 新增
            'ProductCode': pid_str,    # 新增
            'Name': name_raw,
            'Group': slave['group'],
            'Model': slave['order_no'],
            'Category': final_category, 
            'Source': detection_method,
            'Mapped PDOs': ", ".join(current_pdos)
        })

    xml_content += TEMPLATE_FOOTER
    return xml_content, report_data

def save_frontend_json(report_data):
    frontend_data = {
        "system_status": "scanned",
        "topology": report_data
    }
    with open(OUTPUT_JSON_FILE, 'w', encoding='utf-8') as f:
        json.dump(frontend_data, f, indent=2, ensure_ascii=False)

def main():
    print("==================================================")
    print(f" EtherCAT Smart Scan v6.1 (Auto-Clear Mode)")
    print("==================================================")

    # 1. 取得 EtherCAT 原始資料
    raw_output = get_ethercat_verbose_data()
    
    # 2. [關鍵修改]：如果沒有資料，也要產生空的 JSON 讓 Server 知道掃描完了
    if not raw_output or raw_output.strip() == "":
        print("[INFO] No slaves detected. Writing empty topology.")
        save_frontend_json([])  # 寫入空陣列 []
        # 即使沒資料也清空舊的 XML 和 CSV，避免內容與實際不符
        if os.path.exists(OUTPUT_XML_FILE): os.remove(OUTPUT_XML_FILE)
        if os.path.exists(OUTPUT_REPORT_FILE): os.remove(OUTPUT_REPORT_FILE)
        return  # 正常結束，不拋出錯誤

    # 3. 解析資料
    slaves = parse_verbose_output(raw_output)
    print(f"[INFO] Parsed {len(slaves)} slaves.")
    
    # 4. 生成設定與報表資料
    full_xml, report_data = generate_full_config(slaves)
    
    # 5. 寫入 XML 設定檔
    with open(OUTPUT_XML_FILE, "w", encoding="utf-8") as f:
        f.write(full_xml)
    print(f"[SUCCESS] Config saved to {OUTPUT_XML_FILE}")
    
    # 6. 寫入 CSV 報表 (加入 report_data 檢查)
    if report_data:
        keys = report_data[0].keys()
        with open(OUTPUT_REPORT_FILE, 'w', newline='', encoding='utf-8-sig') as f:
            writer = csv.DictWriter(f, fieldnames=keys)
            writer.writeheader()
            writer.writerows(report_data)
        print(f"[REPORT] Report saved to {OUTPUT_REPORT_FILE}")

    # 7. 儲存 JSON 給前端 (Server.py 會讀這一個檔案)
    save_frontend_json(report_data)

if __name__ == "__main__":
    main()
