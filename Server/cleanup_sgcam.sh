#!/bin/bash
# [2026-03-04] SGCAM_PB 清理腳本
# 用途：將不需要的暫存/備份檔移到 BACK/ 目錄
# 使用方式：在 LinuxCNC 機台上執行
#   cd ~/linuxcnc/configs/SGCAM_PB && bash cleanup_sgcam.sh

DIR="$(cd "$(dirname "$0")" && pwd)"
BACK="$DIR/BACK"

echo "=== SGCAM_PB 清理腳本 ==="
echo "工作目錄: $DIR"
echo ""

# 建立 BACK 目錄
mkdir -p "$BACK"

# ---------------------------------------------------------------
# 必要檔案清單（絕對不動）
# ---------------------------------------------------------------
KEEP_FILES=(
    "3axis.ini"                   # 主設定檔
    "3axis.hal"                   # HAL 接線
    "ethercat-conf.xml"           # EtherCAT 拓撲
    "probe_basic_postgui.hal"     # PostGUI HAL
    "tool_metric.tbl"             # 刀具表
    "tool.tbl"                    # 刀具表（備用名稱）
    "vmc_metric.var"              # 系統參數（G54-G59.3）
    "custom_config.yml"           # Probe Basic 顯示設定
    "pbsplash.png"                # Probe Basic Splash（保留 PB UI）
    "cleanup_sgcam.sh"            # 本腳本
)

# 必要目錄清單（絕對不動）
KEEP_DIRS=(
    "macros_metric_sim"           # NGC 副程式（M6 REMAP 依賴）
    "user_buttons"                # Probe Basic 自訂按鈕（保留 PB UI）
    "user_atc_buttons"            # Probe Basic ATC 按鈕（保留 PB UI）
    "user_dro_display"            # Probe Basic DRO 顯示（保留 PB UI）
    "BACK"                        # 備份目錄本身
)

# ---------------------------------------------------------------
# 1. 先列出目前所有檔案
# ---------------------------------------------------------------
echo "--- 目前目錄內容 ---"
ls -la "$DIR"
echo ""

# ---------------------------------------------------------------
# 2. 報告每個檔案的用途
# ---------------------------------------------------------------
echo "=== 檔案用途報告 ==="
echo ""

for item in "$DIR"/*; do
    name=$(basename "$item")

    # 跳過 BACK 目錄
    [[ "$name" == "BACK" ]] && continue

    # 判斷是否在保留清單
    is_keep=false
    for k in "${KEEP_FILES[@]}"; do
        [[ "$name" == "$k" ]] && is_keep=true && break
    done
    for k in "${KEEP_DIRS[@]}"; do
        [[ "$name" == "$k" ]] && is_keep=true && break
    done

    # 輸出說明
    if [ -d "$item" ]; then
        TYPE="[目錄]"
    else
        TYPE="[檔案]"
    fi

    case "$name" in
        3axis.ini)                  echo "  $TYPE $name — 主設定檔（LinuxCNC 啟動入口）  [必要]" ;;
        3axis.hal)                  echo "  $TYPE $name — HAL 接線（EtherCAT↔軸/IO）  [必要]" ;;
        ethercat-conf.xml)          echo "  $TYPE $name — EtherCAT 硬體拓撲  [必要]" ;;
        probe_basic_postgui.hal)    echo "  $TYPE $name — PostGUI HAL 信號  [必要]" ;;
        tool_metric.tbl|tool.tbl)   echo "  $TYPE $name — 刀具表  [必要]" ;;
        vmc_metric.var)             echo "  $TYPE $name — 系統參數（G54-G59.3 偏移）  [必要]" ;;
        custom_config.yml)          echo "  $TYPE $name — Probe Basic 顯示設定  [必要]" ;;
        pbsplash.png)               echo "  $TYPE $name — Probe Basic 開機 Splash  [保留PB]" ;;
        macros_metric_sim)          echo "  $TYPE $name — NGC 副程式（M6 REMAP）  [必要]" ;;
        user_buttons)               echo "  $TYPE $name — Probe Basic 按鈕  [保留PB]" ;;
        user_atc_buttons)           echo "  $TYPE $name — Probe Basic ATC 按鈕  [保留PB]" ;;
        user_dro_display)           echo "  $TYPE $name — Probe Basic DRO 顯示  [保留PB]" ;;
        position.txt)               echo "  $TYPE $name — 暫存檔（啟動時自動刪除）  [可清理]" ;;
        linuxcnc.var)               echo "  $TYPE $name — 舊參數檔（已被 vmc_metric.var 取代）  [可清理]" ;;
        *.var.bak)                  echo "  $TYPE $name — .var 備份檔  [可清理]" ;;
        *.bak)                      echo "  $TYPE $name — 備份檔  [可清理]" ;;
        *.old)                      echo "  $TYPE $name — 舊版備份  [可清理]" ;;
        *.save)                     echo "  $TYPE $name — 存檔備份  [可清理]" ;;
        *~)                         echo "  $TYPE $name — 編輯器暫存  [可清理]" ;;
        cleanup_sgcam.sh)           echo "  $TYPE $name — 本清理腳本  [保留]" ;;
        *)
            if $is_keep; then
                echo "  $TYPE $name — （保留清單）  [保留]"
            else
                echo "  $TYPE $name — 未知用途  [待確認]"
            fi
            ;;
    esac
done

echo ""

# ---------------------------------------------------------------
# 3. 執行搬移
# ---------------------------------------------------------------
MOVED=0

move_to_back() {
    local f="$1"
    local name=$(basename "$f")
    if [ -e "$f" ]; then
        mv "$f" "$BACK/$name"
        echo "  MOVED: $name → BACK/"
        MOVED=$((MOVED + 1))
    fi
}

echo "=== 搬移暫存/備份檔至 BACK/ ==="

# 暫存檔
[ -f "$DIR/position.txt" ] && move_to_back "$DIR/position.txt"

# 舊 .var（若 vmc_metric.var 存在，linuxcnc.var 就是多餘的）
if [ -f "$DIR/vmc_metric.var" ] && [ -f "$DIR/linuxcnc.var" ]; then
    move_to_back "$DIR/linuxcnc.var"
fi

# 所有 .bak / .old / .save / ~ 備份檔
for f in "$DIR"/*.var.bak "$DIR"/*.bak "$DIR"/*.old "$DIR"/*.save "$DIR"/*~; do
    [ -f "$f" ] && move_to_back "$f"
done

if [ $MOVED -eq 0 ]; then
    echo "  （無需搬移的檔案）"
fi

echo ""
echo "=== 完成 ==="
echo "搬移了 $MOVED 個檔案到 BACK/"
echo ""

# ---------------------------------------------------------------
# 4. 最終目錄狀態
# ---------------------------------------------------------------
echo "--- 清理後目錄內容 ---"
ls -la "$DIR"
echo ""
echo "--- BACK/ 目錄內容 ---"
ls -la "$BACK" 2>/dev/null || echo "  （空）"
