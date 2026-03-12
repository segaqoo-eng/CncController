# SGCAM_PB 檔案清單與功能說明

> 產生日期：2026-03-04
> 僅列出清理後保留的檔案（BACK/ 備份目錄已排除）

---

## 目錄結構

```
SGCAM_PB/
├── 3axis.ini                    # LinuxCNC 主設定檔
├── 3axis.hal                    # HAL 接線（EtherCAT）
├── ethercat-conf.xml            # EtherCAT 硬體拓撲
├── probe_basic_postgui.hal      # PostGUI HAL
├── vmc_metric.var               # 系統參數（G54-G59.3）
├── tool_metric.tbl              # 刀具表（metric）
├── custom_config.yml            # Probe Basic 顯示設定
├── pbsplash.png                 # Probe Basic 開機 Splash 圖片
├── macros_metric_sim/           # NGC 副程式（88 個）
│   ├── [M6 換刀 REMAP]
│   ├── [ATC 刀庫操作]
│   ├── [探測副程式]
│   ├── [通用輔助]
│   └── ...
├── python/                      # Python REMAP 腳本
│   ├── toplevel.py
│   ├── remap.py
│   ├── stdglue.py
│   └── __pycache__/
├── nc_files/                    # G-Code 程式檔
│   └── Drill.cnc
├── user_buttons/                # PB 自訂按鈕面板
├── user_atc_buttons/            # PB ATC 按鈕面板
├── user_dro_display/            # PB DRO 顯示（6 種軸配置）
└── BACK/                        # 備份（36 項）
```

---

## 1. 根目錄設定檔

### 3axis.ini — LinuxCNC 主設定檔

| 區段 | 關鍵設定 | 說明 |
|------|---------|------|
| `[EMC]` | `MACHINE = SGCAM_PB_ATC` | 機台名稱 |
| `[DISPLAY]` | `DISPLAY = probe_basic` | 使用 Probe Basic UI |
| | `CONFIG_FILE = custom_config.yml` | PB 顯示設定 |
| | `INTRO_GRAPHIC = pbsplash.png` | 開機 Splash 圖 |
| | `ATC_TAB_DISPLAY = 2` | ATC 頁籤模式 |
| | `USER_BUTTONS_PATH = user_buttons/` | 自訂按鈕路徑 |
| | `USER_ATC_BUTTONS_PATH = user_atc_buttons/` | ATC 按鈕路徑 |
| | `USER_DROS_PATH = user_dro_display/` | DRO 顯示路徑 |
| | `OFFSET_COLUMNS = X Y Z` | Offset 表格欄位 |
| `[ATC]` | `POCKETS = 12` | 刀庫 12 刀位 |
| `[RS274NGC]` | `PARAMETER_FILE = vmc_metric.var` | 系統參數檔 |
| | `SUBROUTINE_PATH = macros_metric_sim` | NGC 副程式路徑 |
| | `REMAP=M6 ... ngc=toolchange` | M6 換刀重映射 |
| | `REMAP=M10~M26` | ATC 刀庫動作重映射 |
| `[PYTHON]` | `TOPLEVEL = ./python/toplevel.py` | Python REMAP 入口 |
| `[HAL]` | `HALFILE = 3axis.hal` | HAL 接線檔 |
| | `POSTGUI_HALFILE = probe_basic_postgui.hal` | PostGUI HAL |
| `[EMCIO]` | `TOOL_TABLE = tool_metric.tbl` | 刀具表 |
| `[TRAJ]` | `COORDINATES = X Y Z` / `LINEAR_UNITS = mm` | 3 軸公制 |
| `[KINS]` | `JOINTS = 3` / `trivkins coordinates=XYZ` | 3 Joint 直角運動學 |
| `[JOINT_0]` | X 軸：STEP_SCALE=1000, HOME_SEQUENCE=2 | X 軸參數 |
| `[JOINT_1]` | Y 軸：STEP_SCALE=1000, HOME_SEQUENCE=2 | Y 軸參數 |
| `[JOINT_2]` | Z 軸：STEP_SCALE=2000, HOME_SEQUENCE=1 | Z 軸參數（Z 先歸零） |

### 3axis.hal — HAL 接線（EtherCAT + CiA 402）

| 區塊 | 說明 |
|------|------|
| `loadrt cia402 count=3` | 載入 3 組 CiA 402 伺服驅動 |
| `loadusr -W lcec_conf ethercat-conf.xml` | 載入 EtherCAT 拓撲設定 |
| `loadrt lcec` | 載入 LinuxCNC EtherCAT 元件 |
| Axis X (Slave 0) | CSP 模式、pos-cmd/pos-fb、digital_inputs → bitslice、極限開關 |
| Axis Y (Slave 2) | 同上（Slave index 2） |
| Axis Z (Slave 3) | 同上（Slave index 3） |
| Standard Signals | coolant-flood/mist、spindle-on/cw/ccw/brake |
| Input Group (Slave 13) | 32 DI：`probe-in`(din-00)、Pin1~Pin31 |
| Output Group (Slave 13) | 32 DO：coolant-flood(dout-02)、coolant-mist(dout-03)、Pin4~Pin31 |
| Safety Bypass | `iocontrol.0.emc-enable-in` 強制連接（開發階段） |

### ethercat-conf.xml — EtherCAT 硬體拓撲

| Slave | VID:PID | 說明 |
|-------|---------|------|
| 0 | 0x1dd:0x6080 | **X 軸伺服**（CiA 402 CSP，含 digital_inputs） |
| 1 | 0x1dd:0x5500 | 耦合器/中繼（無 PDO） |
| 2 | 0x1dd:0x5621 | **Y 軸伺服**（CiA 402 CSP，含 digital_inputs） |
| 3 | 0x1dd:0x5621 | **Z 軸伺服**（CiA 402 CSP，含 digital_inputs） |
| 4~12 | 0x1dd:0x5500 | 耦合器/中繼（無 PDO），共 9 個 |
| 13 | 0x1dd:0x0902 | **IO 模組**（32 DI + 32 DO） |

### probe_basic_postgui.hal — PostGUI HAL

| 內容 | 說明 |
|------|------|
| `net spindle-at-speed` | 主軸到速信號（固定 true，模擬用） |
| `net probe-in => motion.probe-input` | 探針信號接線（如果 HAL 有定義） |

### vmc_metric.var — 系統參數

- LinuxCNC 持久化變數檔（#1~#5603）
- 包含 G54~G59.3 工件座標偏移、G28/G30 參考點、探測參數等
- 格式：`變數編號 值`（每行一組）

### tool_metric.tbl — 刀具表

- LinuxCNC 刀具表格式
- 目前定義 T1~T12（含刀長 Z offset、直徑 D、備註）
- 格式：`T{n} P{pocket} Z{offset} D{diameter} ;{remark}`

### custom_config.yml — Probe Basic 顯示設定

- Probe Basic UI 的 YAML 設定檔
- 控制面板顯示/隱藏、DRO 格式、按鈕排列等

### pbsplash.png — 開機 Splash 圖片

- Probe Basic 啟動時顯示的 Splash Screen（136 KB PNG）
- INI `[DISPLAY]INTRO_GRAPHIC` 引用

---

## 2. macros_metric_sim/ — NGC 副程式（88 個）

### 2.1 M6 換刀 REMAP（3 個）

INI 設定：`REMAP=M6 modalgroup=6 prolog=change_prolog ngc=toolchange epilog=change_epilog`

| 檔案 | 功能 |
|------|------|
| `change_prolog.ngc` | M6 REMAP 前置 Stub（輸出 debug 訊息，佔位用） |
| `change_epilog.ngc` | M6 REMAP 收尾 Stub（輸出 debug 訊息，佔位用） |
| `toolchange.ngc` | **M6 換刀主程式**：搜尋刀庫刀位 → M21 存刀 → M22 取刀 → 更新持久化追蹤參數 → G43 刀長補正 |

### 2.2 ATC 刀庫操作（25 個）

#### REMAP M-Code（INI 定義 M10~M26）

| 檔案 | REMAP | 功能 |
|------|-------|------|
| `m10.ngc` | M10 P{n} | 旋轉刀庫到目標刀位（計算 CW/CCW 最短路徑），呼叫 M11/M12；未歸零時自動呼叫 M13 |
| `m11.ngc` | M11 P{n} | 刀庫**順時針**旋轉 P 步（預設 1），計數旋轉 index sensor 脈衝，更新目前刀位追蹤參數 |
| `m12.ngc` | M12 P{n} | 刀庫**逆時針**旋轉 P 步（預設 1），同上 |
| `m13.ngc` | M13 | **刀庫歸零/初始化**：同步所有刀位↔刀號映射（#4001~#4024 → UI widget），恢復主軸刀具狀態（M61 + G43） |
| `m21.ngc` | M21 | **Rack ATC 存刀**：計算目標刀位 XY → Z 安全高度 → 移動到清空位 → 下降到裝刀高度 → 主軸定位 → 滑入刀位 → 鬆刀（M24）→ Z 退回 |
| `m22.ngc` | M22 | **Rack ATC 取刀**：計算目標刀位 XY → 移動到刀位 → 主軸定位 → 鬆刀（M24）→ 下降到裝刀高度 → 夾刀（M25）→ 滑出刀位 → Z 退回 |
| `m23.ngc` | M23 | 空檔（預留 M-Code，無實作） |
| `m24.ngc` | M24 | **鬆刀桿**（Drawbar Release）：開啟 DO P2（M64 P2） |
| `m25.ngc` | M25 | **夾刀桿**（Drawbar Clamp）：關閉 DO P2（M65 P2） |
| `m26.ngc` | M26 | 空檔（預留 M-Code，無實作） |

#### ATC 輔助副程式

| 檔案 | 功能 |
|------|------|
| `clamptool.ngc` | 啟動夾刀電磁閥（M65 P2），等待夾刀感測器確認（2s timeout） |
| `unclamptool.ngc` | 啟動鬆刀電磁閥（M64 P2），等待鬆刀感測器確認（2s timeout） |
| `orientspindle.ngc` | 停止主軸/冷卻（M5 M9），主軸定位（M19 R0）用於換刀對齊 |
| `extendatc.ngc` | Z 移到安全高度，伸出 ATC 刀庫（M64 P0），等待到位感測器（5s timeout） |
| `retractatc.ngc` | 收回 ATC 刀庫到原位（M64 P1），等待到位感測器（5s timeout） |
| `load_spindle_safety.ngc` | **軟體裝刀**（ATC 頁面呼叫）：檢查刀具未存於刀庫 → M61 設定主軸刀號 + G43 刀長補正 → 更新追蹤參數 |
| `load_spindle_safety_2.ngc` | **軟體裝刀**（Tool 頁面呼叫）：同上，從不同 UI 頁面呼叫 |
| `unload_spindle.ngc` | **軟體卸刀**：M61 Q0（清除主軸刀號）+ G49（取消刀長補正）+ 清除追蹤參數 #3991 |
| `store_tool_in_carousel.ngc` | **存刀入刀庫**：G49 取消刀長補正 → T0 M6 觸發換刀流程將刀存回刀庫 |
| `move_head_above_carousel.ngc` | Z 快速移動到 ATC 安全高度（刀庫上方） |
| `move_tool_to_carousel_height.ngc` | Z 快速下降到 ATC 換刀高度（刀庫夾取位） |
| `rack_id_calc.ngc` | **Rack 刀庫幾何計算**：從刀位 1/2 的 XY 座標 + 清空點，計算刀庫排列方向（X/Y 軸）、排列順序、垂直清空方向；寫入持久化參數 + UI widget |

#### M6 呼叫入口（從不同 UI 頁面）

| 檔案 | 功能 |
|------|------|
| `m6_tool_call_atc_page.ngc` | 從 ATC 頁面執行 T{n} M6 G43 換刀，探針刀號停主軸，更新冷卻液 |
| `m6_tool_call_main_panel.ngc` | 從主面板執行 T{n} M6 G43 換刀，同上 |
| `m6_tool_call_tool_page.ngc` | 從 Tool 頁面執行 T{n} M6 G43 換刀，同上 |

### 2.3 探測副程式（52 個）

#### 2.3.1 單軸邊緣探測（基礎 Sub，回傳邊緣位置）

| 檔案 | 探測方向 | 功能 |
|------|---------|------|
| `probe_x_plus.ngc` | X+ | 探測 X+ 方向邊緣，回傳補正後邊緣位置（供其他副程式呼叫） |
| `probe_x_minus.ngc` | X- | 探測 X- 方向邊緣，回傳補正後邊緣位置 |
| `probe_y_plus.ngc` | Y+ | 探測 Y+ 方向邊緣，回傳補正後邊緣位置 |
| `probe_y_minus.ngc` | Y- | 探測 Y- 方向邊緣，回傳補正後邊緣位置 |
| `probe_z_minus_sub.ngc` | Z- | 探測 Z- 找工件頂面，可選寫入 Z 零點到 WCO |

#### 2.3.2 單軸邊緣探測 + WCO 寫入（獨立副程式，讀取 #30xx 參數）

| 檔案 | 探測方向 | 功能 |
|------|---------|------|
| `probe_x_plus_wco.ngc` | X+ | 探測 X+ 邊緣 → 寫入 X 零點到 Active WCS（G10 L2） |
| `probe_x_minus_wco.ngc` | X- | 探測 X- 邊緣 → 寫入 X 零點到 Active WCS |
| `probe_y_plus_wco.ngc` | Y+ | 探測 Y+ 邊緣 → 寫入 Y 零點到 Active WCS |
| `probe_y_minus_wco.ngc` | Y- | 探測 Y- 邊緣 → 寫入 Y 零點到 Active WCS |
| `probe_z_minus_wco.ngc` | Z- | 探測 Z- 頂面 → 寫入 Z 零點到 Active WCS |

#### 2.3.3 Outside Corners（外角探測：頂面 + 雙邊，設定 XYZ 零點）

| 檔案 | 位置 | 探測動作 |
|------|------|---------|
| `probe_front_left_top_corner.ngc` | 前左 | Z 頂面 → X+ 邊 → Y+ 邊 → 寫入 XYZ 零點到 WCO |
| `probe_front_right_top_corner.ngc` | 前右 | Z 頂面 → X- 邊 → Y+ 邊 → 寫入 XYZ 零點到 WCO |
| `probe_back_left_top_corner.ngc` | 後左 | Z 頂面 → X+ 邊 → Y- 邊 → 寫入 XYZ 零點到 WCO |
| `probe_back_right_top_corner.ngc` | 後右 | Z 頂面 → X- 邊 → Y- 邊 → 寫入 XYZ 零點到 WCO |

#### 2.3.4 Inside Corners（內角探測：頂面 + 雙內壁，設定 XYZ 零點）

| 檔案 | 位置 | 探測動作 |
|------|------|---------|
| `probe_front_left_inside_corner.ngc` | 前左 | Z 頂面 → X- 內壁 → Y- 內壁 → 寫入 XYZ 零點到 WCO |
| `probe_front_right_inside_corner.ngc` | 前右 | Z 頂面 → X+ 內壁 → Y- 內壁 → 寫入 XYZ 零點到 WCO |
| `probe_back_left_inside_corner.ngc` | 後左 | Z 頂面 → X- 內壁 → Y+ 內壁 → 寫入 XYZ 零點到 WCO |
| `probe_back_right_inside_corner.ngc` | 後右 | Z 頂面 → X+ 內壁 → Y+ 內壁 → 寫入 XYZ 零點到 WCO |

#### 2.3.5 Top Side（頂面 + 單邊探測，設定單軸 + Z 零點）

| 檔案 | 邊 | 探測動作 |
|------|---|---------|
| `probe_front_top_side.ngc` | 前（Y+） | Z 頂面 → Y+ 邊 → 寫入 Y + Z 零點到 WCO |
| `probe_back_top_side.ngc` | 後（Y-） | Z 頂面 → Y- 邊 → 寫入 Y + Z 零點到 WCO |
| `probe_left_top_side.ngc` | 左（X+） | Z 頂面 → X+ 邊 → 寫入 X + Z 零點到 WCO |
| `probe_right_top_side.ngc` | 右（X-） | Z 頂面 → X- 邊 → 寫入 X + Z 零點到 WCO |

#### 2.3.6 Boss（凸台中心探測）

| 檔案 | 形狀 | 功能 |
|------|------|------|
| `probe_rect_boss.ngc` | 矩形 | 從上方探測矩形凸台四邊 → 計算 XY 中心 + X/Y 寬度 → 寫入 XY 零點 |
| `probe_round_boss.ngc` | 圓形 | 從上方探測圓形凸台四邊×2 次（共 8 觸）→ 計算 XY 中心 + 平均直徑 → 寫入 XY 零點 |

#### 2.3.7 Pocket（口袋中心探測）

| 檔案 | 形狀 | 起始位置 | 功能 |
|------|------|---------|------|
| `probe_rect_pocket.ngc` | 矩形 | 左壁邊緣 | 探測矩形口袋四壁 → 計算 XY 中心 + X/Y 寬度 → 寫入 XY 零點 |
| `probe_rect_pocket_center_start.ngc` | 矩形 | 概略中心（已在口袋內） | 同上，適合已經在口袋內的情況 |
| `probe_round_pocket.ngc` | 圓形 | 左壁邊緣 | 探測圓形口袋四壁 → 計算 XY 中心 + 平均直徑 → 寫入 XY 零點 |
| `probe_round_pocket_center_start.ngc` | 圓形 | 概略中心（已在口袋內） | 同上 |

#### 2.3.8 Ridge / Valley（凸脊 / 凹槽中心探測）

| 檔案 | 類型 | 軸 | 起始 | 功能 |
|------|------|---|------|------|
| `probe_ridge_x.ngc` | 凸脊 | X | 上方 | 從上方探測 X 軸凸脊兩側 → 計算中心 + 寬度 → 寫入 X 零點 |
| `probe_ridge_y.ngc` | 凸脊 | Y | 上方 | 從上方探測 Y 軸凸脊兩側 → 計算中心 + 寬度 → 寫入 Y 零點 |
| `probe_valley_x.ngc` | 凹槽 | X | 左壁邊緣 | 探測 X 軸凹槽兩壁 → 計算中心 + 寬度 → 寫入 X 零點 |
| `probe_valley_x_center_start.ngc` | 凹槽 | X | 概略中心 | 同上，已在凹槽內 |
| `probe_valley_y.ngc` | 凹槽 | Y | 後壁邊緣 | 探測 Y 軸凹槽兩壁 → 計算中心 + 寬度 → 寫入 Y 零點 |
| `probe_valley_y_center_start.ngc` | 凹槽 | Y | 概略中心 | 同上，已在凹槽內 |

#### 2.3.9 Edge Angle — Corner（角落角度探測：XY 零點 + 旋轉補正）

| 檔案 | 角落位置 | 探測動作 |
|------|---------|---------|
| `probe_corner_x_plus_edge_angle.ngc` | 後左 | Y- 邊 → X+ 邊在兩個 Y 位置 → 計算 XY 零點 + 邊緣角度 |
| `probe_corner_x_minus_edge_angle.ngc` | 前右 | Y+ 邊 → X- 邊在兩個 Y 位置 → 計算 XY 零點 + 邊緣角度 |
| `probe_corner_y_plus_edge_angle.ngc` | 前左 | X+ 邊 → Y+ 邊在兩個 X 位置 → 計算 XY 零點 + 邊緣角度 |
| `probe_corner_y_minus_edge_angle.ngc` | 後右 | X- 邊 → Y- 邊在兩個 X 位置 → 計算 XY 零點 + 邊緣角度 |

#### 2.3.10 Edge Angle — Top（頂面邊緣角度：單邊兩點測量）

| 檔案 | 邊 | 功能 |
|------|---|------|
| `probe_top_front_edge_angle.ngc` | 前（Y+） | Y+ 邊在兩個 X 位置探測 → 計算邊緣角度 → 可選旋轉 WCO |
| `probe_top_back_edge_angle.ngc` | 後（Y-） | Y- 邊在兩個 X 位置探測 → 計算邊緣角度 |
| `probe_top_left_edge_angle.ngc` | 左（X+） | X+ 邊在兩個 Y 位置探測 → 計算邊緣角度 |
| `probe_top_right_edge_angle.ngc` | 右（X-） | X- 邊在兩個 Y 位置探測 → 計算邊緣角度 |

#### 2.3.11 Probe Calibration（探針校準）

| 檔案 | 參考件 | 功能 |
|------|--------|------|
| `probe_cal_round_boss.ngc` | 已知直徑圓形凸台 | 雙次 X/Y 探測 → 計算校準偏移 → 寫入 #3032 |
| `probe_cal_round_pocket.ngc` | 已知直徑圓形口袋 | 雙次 X/Y 探測 → 計算校準偏移 → 寫入 #3032 |
| `probe_cal_square_boss.ngc` | 已知 X/Y 寬度方形凸台 | 探測四邊 → 計算校準偏移 → 寫入 #3032 |
| `probe_cal_square_pocket.ngc` | 已知 X/Y 寬度方形口袋 | 探測四壁 → 計算校準偏移 → 寫入 #3032 |
| `probe_cal_reset.ngc` | — | 重置校準偏移（#3032 = 0） |

#### 2.3.12 Tool Setter / Tool Touch Off（對刀儀）

| 檔案 | 功能 |
|------|------|
| `probe_spindle_nose.ngc` | 裸主軸鼻端向下探測對刀儀 → 建立主軸零高基準（#3010）→ 自動設定 Z 最大行程 |
| `tool_touch_off.ngc` | 移動到對刀儀位置 → 探測目前刀具長度 → G10 L1 寫入刀長偏移 → G43 啟用 |
| `toolsetter_wco.ngc` | M6 換刀 → 在對刀儀上探測新刀具 → 依對刀儀高度設定 Z 零點到 Active WCS → 暫停等待防塵罩復位 |

#### 2.3.13 參數更新輔助

| 檔案 | 功能 |
|------|------|
| `touch_probe_param_update.ngc` | 將所有觸發探針參數（刀號、進給速度、安全距離、模式、校準偏移等）寫入持久化變數 #3014~#3036 |
| `tool_setter_param_update.ngc` | 將所有對刀儀參數（進給速度、最大行程、退回距離、主軸零高、破損容許等）寫入持久化變數 #3004~#3039 |

### 2.4 通用輔助副程式（8 個）

| 檔案 | 功能 |
|------|------|
| `go_to_zero.ngc` | 快速回工件零點：Z0 先（G53 機械座標），再 XY0（工件座標），4/5 軸含 A0/C0；M73 保存/恢復冷卻 |
| `go_to_home.ngc` | 快速回機械原點：全軸 G53 G0 歸零（Z 先，再 XY，再 A/C）；M73 保存/恢復冷卻 |
| `go_to_g30.ngc` | Z 先 G53 G0 Z0，再 G30 移到預設第二參考點（換刀位）；M73 保存/恢復冷卻 |
| `set_g30_position.ngc` | 記錄目前位置為 G30 參考點（G30.1），更新 UI widget 顯示 X/Y/Z 換刀位（#5181/#5182/#5183） |
| `on_abort.ngc` | 中止處理：重置為 G90 絕對模式 + G40 取消刀徑補正 + G49 取消刀長補正 |
| `program_coolant.ngc` | 計算可程控冷卻液噴嘴角度（依刀長/刀徑/噴嘴距離/角度偏移）→ M68 E0 輸出類比值 |
| `update_programmable_coolant_params.ngc` | 保存 4 個冷卻液設定參數到持久化變數 #3000~#3003 |
| `reset_all_data.ngc` | 重置所有探測結果資料（XY 中心、寬度、各軸探測位置、直徑、角度）為零 |
| `x_data_reset.ngc` | 重置 X 軸探測結果（中心、寬度、X+/X- 位置）為零 |
| `y_data_reset.ngc` | 重置 Y 軸探測結果（中心、寬度、Y+/Y- 位置）為零 |

---

## 3. python/ — Python REMAP 腳本

| 檔案 | 功能 |
|------|------|
| `toplevel.py` | LinuxCNC 解釋器頂層初始化：import remap 模組讓重映射碼（T/M6）可用 |
| `remap.py` | REMAP 模組：一行 `from stdglue import *`，重新匯出 stdglue 所有功能 |
| `stdglue.py` | **解釋器 REMAP 膠水碼**：`prepare_prolog/epilog`（T 準備刀具）+ `change_prolog/epilog`（M6 換刀）+ v2.9/v2.10 相容性 + 可選 `build_hal()` HAL 元件 |
| `__pycache__/*.pyc` | Python 編譯快取（自動產生） |

---

## 4. nc_files/ — G-Code 程式檔

| 檔案 | 功能 |
|------|------|
| `Drill.cnc` | 鑽孔加工程式（7.3 KB） |

---

## 5. user_buttons/ — PB 自訂按鈕面板

### user_buttons/template_user_buttons/

| 檔案 | 功能 |
|------|------|
| `template_user_buttons.py` | QWidget 載入 .ui 檔，初始化 LinuxCNC status + tool table 插件 |
| `template_user_buttons.ui` | Qt Designer UI 設計檔 |
| `template_user_buttons_ui.py` | PyQt5 自動產生的 UI 程式碼：8 個按鈕 — RELOAD PGM / CLEAR PGM / FLOOD / MIST / FEED HOLD / SINGLE BLOCK / BLOCK DELETE / M01 BREAK |
| `__pycache__/*.pyc` | Python 編譯快取 |

---

## 6. user_atc_buttons/ — PB ATC 按鈕面板

### user_atc_buttons/template_user_atc_buttons/（Carousel 旋轉刀庫）

| 檔案 | 功能 |
|------|------|
| `template_user_atc_buttons.py` | QWidget 載入 .ui，Carousel ATC 控制面板 |
| `template_user_atc_buttons.ui` | Qt Designer UI 設計檔 |
| `template_user_atc_buttons_ui.py` | PyQt5 UI：ATC REV/FWD（M11/M12）、RETRACT/EXTEND、CLAMP/RELEASE TOOL、ORIENT/UNLOCK SPINDLE、MOVE HEAD ABOVE CAROUSEL、MOVE TOOL TO CAROUSEL HEIGHT、REF CAROUSEL |

### user_atc_buttons/template_user_rack_atc_buttons/（Rack 排列刀庫）

| 檔案 | 功能 |
|------|------|
| `template_user_rack_atc_buttons.py` | QWidget 載入 .ui，Rack ATC 控制面板 |
| `template_user_rack_atc_buttons.ui` | Qt Designer UI 設計檔 |
| `template_user_rack_atc_buttons_ui.py` | PyQt5 UI：AIR BLAST / RETR DUST BOOT / CLAMP/RELEASE TOOL / ORIENT/UNLOCK SPINDLE / HEAD UP/DOWN / REF RACK DATA |
| `__pycache__/*.pyc` | Python 編譯快取 |

---

## 7. user_dro_display/ — PB DRO 顯示（6 種軸配置）

每個子資料夾提供一種軸配置的 DRO，PB 根據 INI 的 `DRO_DISPLAY` 自動選擇。

| 子目錄 | 軸配置 | 檔案 |
|--------|--------|------|
| `xyz_dros/` | **3 軸 XYZ**（目前使用） | `dros_xyz.py` + `.ui` + `_ui.py` + `offset_dros_xyz.ui` + `_ui.py` |
| `xyza_dros/` | 4 軸 XYZA | `dros_xyza.py` + `.ui` + `_ui.py` + `offset_dros_xyza.ui` + `_ui.py` |
| `xyzab_dros/` | 5 軸 XYZAB | `dros_xyzab.py` + `.ui` + `_ui.py` + `offset_dros_xyzab.ui` + `_ui.py` |
| `xyzac_dros/` | 5 軸 XYZAC | `dros_xyzac.py` + `.ui` + `_ui.py` + `offset_dros_xyzac.ui` + `_ui.py` |
| `xyzbc_dros/` | 5 軸 XYZBC | `dros_xyzbc.py` + `.ui` + `_ui.py` + `offset_dros_xyzbc.ui` + `_ui.py` |
| `user_dros/` | 自訂 DRO | `dros_user.py` + `.ui` + `_ui.py` + `offset_dros_user.ui` + `_ui.py` |

每個 DRO 包含：
- `dros_*.py` — QWidget 載入 .ui，初始化 LinuxCNC 插件
- `dros_*.ui` — Qt Designer 主 DRO 佈局（5 欄：ZERO / G5X WORK / MACHINE / DTG / REF）
- `dros_*_ui.py` — PyQt5 自動產生程式碼（**勿手動修改**）
- `offset_dros_*.ui` — Qt Designer Offset DRO 佈局（每軸：ZERO + DRO + Machine + Work Offset + G52/G92 + Tool Offset）
- `offset_dros_*_ui.py` — PyQt5 自動產生程式碼

---

## 統計

| 類別 | 檔案數 |
|------|--------|
| 根目錄設定檔 | 8 |
| macros_metric_sim/ NGC 副程式 | 88 |
| python/ REMAP 腳本 | 5（含 __pycache__） |
| nc_files/ G-Code | 1 |
| user_buttons/ PB 按鈕 | 4 |
| user_atc_buttons/ PB ATC 按鈕 | 7 |
| user_dro_display/ PB DRO | 31 |
| **合計** | **144** |
| BACK/ 備份（已排除） | 36 |
