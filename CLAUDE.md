# CLAUDE.md

本檔案提供 Claude Code 在此專案中的開發指引。
See @memory.md for current bugs, progress, and decisions.

---

## 1. Project Overview

**LinuxCNC EtherCAT CNC Controller** — WPF 前端 + Python Flask 後端，控制 LinuxCNC 機台。

| 層 | 技術 | 執行環境 |
|---|---|---|
| 前端 | WPF (.NET 10) + CommunityToolkit.Mvvm 8.4.0 + HelixToolkit.Wpf 3.1.2 | Windows 觸控工控機 |
| 後端 | Python 3 Flask + LinuxCNC NML IPC | Ubuntu RT Linux（192.168.0.137:5000） |
| 通訊 | HTTP REST（500ms 輪詢） | LAN |
| 驅動 | EtherCAT + CiA 402 伺服 | LinuxCNC HAL |

**核心技術棧：** CiA 402 伺服協議、EtherCAT 拓樸、LinuxCNC HAL/INI/XML、WPF MVVM Source Generator、Python Flask

**語言規則：** 回答一律使用**繁體中文**。

---

## 2. Directory Structure

```
CncController/                      ← WPF 前端專案根
├── App.xaml(.cs)                   ← 全域資源/轉換器/啟動
├── MainWindow.xaml(.cs)            ← 殼層（InputBindings 快捷鍵 ESC/F1/F2）
├── VersionConfig.cs                ← 版本號常數
├── Converters/
│   ├── FileSizeConverter.cs        ← 檔案大小格式化
│   ├── HexFormatConverter.cs       ← 十六進位顯示
│   └── StringEqualConverter.cs     ← RadioButton ↔ string 雙向（App.xaml 全域註冊）
├── Helpers/
│   ├── BindingProxy.cs             ← DataContext 代理（DataGrid Column 綁定用）
│   ├── GCodeParser.cs              ← G-Code 刀具號解析（ATC PROGRAM TOOLS）
│   └── GCodePathParser3D.cs        ← G-Code 3D 路徑解析（G0/G1/G2/G3 → Point3D 線段）
├── Models/
│   ├── GCodeLineItem.cs            ← G-Code 逐行模型（LineNumber + IsCurrentLine）
│   ├── MachineEnums.cs             ← MachineType / AtcType / CarouselControlMode 列舉
│   ├── MachineModels.cs            ← ~750 行，28+ 類別（MachineConfig/AxisSetting/ToolEntry/ProbeResult/...）
│   ├── MachineStatus.cs            ← ~200 個 [ObservableProperty]（即時狀態 DTO）
│   └── UserModels.cs               ← 使用者/權限模型
├── Services/                       ← 手動 Singleton（Instance 屬性，非 DI）
│   ├── MachineControlService.cs    ← ~1165 行，HTTP 通訊（70+ 公開方法，4 個 HttpClient）
│   ├── ConfigurationService.cs     ← ~830 行，INI/HAL/XML/PostGUI/NGC 生成
│   ├── AlarmService.cs             ← 集中日誌 ≤500 筆 + 跑馬燈 + 每日 log 檔
│   ├── AppSettings.cs              ← appsettings.json（ServerUrl/Language/Theme）
│   ├── AuthService.cs              ← 4 級角色權限 + SHA-256 密碼
│   ├── HardwareScanService.cs      ← EtherCAT 掃描結果快取
│   ├── LocalizationService.cs      ← 多語言切換（ResourceDictionary）
│   └── ThemeService.cs             ← 主題切換（3 主題 + 持久化）
├── ViewModels/                     ← 22 個 ViewModel
│   ├── MainViewModel.cs            ← ~1330 行，根 VM（輪詢/導航/JOG/電源/急停/統計/斷電續切）
│   ├── SettingsViewModel.cs        ← 設定頁協調（聚合 6+ 子 VM）
│   ├── MonitorViewModel.cs         ← ~420 行（G-Code 載入/MDI/3D 路徑/檔案管理）
│   ├── HistoryViewModel.cs         ← ~112 行（LOG/STATS 雙 Tab + 報警統計）
│   ├── OffsetsViewModel.cs         ← G54-G59 座標系管理
│   ├── ToolTableViewModel.cs       ← 刀具表 CRUD + 刀具壽命
│   ├── AtcViewModel.cs             ← ATC 自動刀庫（MANUAL + AUTOMATIC）
│   ├── ProbingViewModel.cs         ← ~650 行（27+ 探測命令 + Tool Setter）
│   ├── MacroVariablesViewModel.cs  ← 巨集變數監控
│   ├── MaintenanceViewModel.cs     ← ~102 行（維護保養 CRUD）
│   ├── BackupViewModel.cs          ← 備份/還原
│   ├── SpindleWarmupViewModel.cs   ← 主軸暖機
│   ├── FileManagerViewModel.cs     ← 程式檔案管理
│   ├── AxisMappingViewModel.cs     ← 軸-Slave 對應
│   ├── AxisParameterViewModel.cs   ← 軸運動/機械參數
│   ├── IoMonitorViewModel.cs       ← 即時 IO + CiA 402 狀態
│   ├── HardwareDiscoveryViewModel.cs ← EtherCAT 掃描 UI
│   ├── MachineConfigViewModel.cs   ← 機台設定 IO MAP
│   ├── AtcBasicSettingsViewModel.cs ← ATC 基本設定
│   ├── AtcAxisSettingsViewModel.cs  ← ATC 軸設定
│   ├── AtcIoSettingsViewModel.cs    ← ATC IO 設定
│   └── SpindleSettingsViewModel.cs  ← 主軸設定
├── Views/
│   ├── MainView.xaml               ← 主頁面（DRO + Dashboard + 內容區）
│   ├── CachedContentControl.cs     ← 分頁快取（消除切換延遲）
│   ├── Layouts/
│   │   ├── HeaderBar.xaml          ← 頂部狀態列 + 選單（Tools/Theme/Language/Exit）
│   │   ├── DashboardPanel.xaml     ← 底部 5 欄（D_1~D_5）
│   │   └── JogPanel.xaml           ← 右側 JOG 面板 + MAN/AUTO/MDI 按鈕
│   ├── Components/
│   │   ├── DroDisplay.xaml         ← DRO 5 欄（ZERO/G5X WORK/MACHINE/DTG/REF）
│   │   ├── CycleControl.xaml       ← D_1: CYCLE START/HOLD/STOP + 電源/急停 + 統計
│   │   ├── SliderControl.xaml      ← D_4: V/F/S/R Override Slider + Spindle Load
│   │   ├── JogConfig.xaml          ← D_5: JOG 速度/步距 + 主軸 FWD/REV/STOP
│   │   ├── ToolInfo.xaml           ← D_2: 即時刀具資訊 + GO TO ZERO/G30
│   │   ├── AxisMappingView.xaml    ← 設定: 軸指派
│   │   └── MachineConfigView.xaml  ← 設定: IO MAP
│   ├── Controls/
│   │   ├── CarouselControl.xaml    ← ATC 轉盤視覺化
│   │   ├── SpindleToolControl.xaml ← ATC 主軸刀具顯示
│   │   └── ToolDisplayControl.xaml ← ATC 刀具圖示
│   ├── Pages/                      ← 主要頁面（12 個）
│   │   ├── MonitorView.xaml        ← G-Code 編輯器 + 3D HelixViewport3D
│   │   ├── SettingsView.xaml       ← ~805 行，設定 TabControl（SCAN/MAPPING/AXIS/IO/IN MAP/OUT MAP/SPINDLE/ATC/MACRO VAR/BACKUP/MAINTENANCE）
│   │   ├── HistoryView.xaml        ← LOG/STATS 雙分頁
│   │   ├── OffsetsView.xaml        ← G54-G59 表格 + 右欄即時座標
│   │   ├── ToolTableView.xaml      ← 刀具表 + TOOL LIFE 雙 Tab
│   │   ├── AtcView.xaml            ← ATC 三欄（MANUAL/中央視覺/AUTOMATIC）
│   │   ├── ProbingView.xaml        ← 探測 8 分頁（Outside/Inside/Boss/Ridge/Angle/Calibrate/Help/ToolSetter）
│   │   ├── FileManagerView.xaml    ← 程式檔案列表
│   │   ├── MacroVariablesView.xaml ← 巨集變數二欄 DataGrid
│   │   └── Atc*SettingsView.xaml   ← ATC 設定三分頁
│   └── Windows/
│       ├── LoginWindow.xaml        ← 登入對話框
│       └── SpindleWarmupWindow.xaml ← 暖機 Modal
├── Resources/
│   ├── Languages/
│   │   ├── Lang.zh-TW.xaml         ← ~389 個 i18n key（繁中）
│   │   └── Lang.en-US.xaml         ← ~389 個 i18n key（英文）
│   └── Themes/
│       ├── Theme.Dark.xaml          ← 全域 Style（BaseBtnStyle/BaseRadioBtnStyle/NavBtnStyle/...）+ 55+ Brush token
│       ├── Theme.Industrial.xaml    ← 湖水綠主題
│       └── Theme.Cyber.xaml         ← 螢光青主題
└── Images/                         ← 背景圖（ATC_Back/TOOL_BACK/Porbing_BACK/SGCAM_logo/...）

Server/                             ← Python 後端
├── server.py                       ← ~3200 行，53 Flask 路由 + 33 內部函式 + 5 背景執行緒
├── guardian.py                     ← 進程守護（Exit Code 42 = API 觸發重啟）
└── smart_scan.py                   ← EtherCAT 掃描 → frontend_topology.json

SGCAM_PB/                           ← 參考用 ProbotBuild 原始碼（唯讀）
AIrefPic/                           ← UI 參考圖片（使用者提到參考圖必查此處）
```

---

## 3. Key Classes & Locations

### ViewModels（22 個）

| ViewModel | 檔案 | 行數 | DataContext 綁定 |
|-----------|------|------|-----------------|
| MainViewModel | ViewModels/MainViewModel.cs | ~1330 | MainView.xaml（根 VM） |
| MonitorViewModel | ViewModels/MonitorViewModel.cs | ~420 | MonitorView.xaml |
| ProbingViewModel | ViewModels/ProbingViewModel.cs | ~650 | ProbingView.xaml |
| SettingsViewModel | ViewModels/SettingsViewModel.cs | — | SettingsView.xaml（聚合 6+ 子 VM） |
| HistoryViewModel | ViewModels/HistoryViewModel.cs | ~112 | HistoryView.xaml |
| OffsetsViewModel | ViewModels/OffsetsViewModel.cs | — | OffsetsView.xaml |
| ToolTableViewModel | ViewModels/ToolTableViewModel.cs | — | ToolTableView.xaml |
| AtcViewModel | ViewModels/AtcViewModel.cs | — | AtcView.xaml |
| MaintenanceViewModel | ViewModels/MaintenanceViewModel.cs | ~102 | SettingsView MAINTENANCE Tab |
| BackupViewModel | ViewModels/BackupViewModel.cs | — | SettingsView BACKUP Tab |
| MacroVariablesViewModel | ViewModels/MacroVariablesViewModel.cs | — | MacroVariablesView.xaml |
| FileManagerViewModel | ViewModels/FileManagerViewModel.cs | — | FileManagerView.xaml |
| SpindleWarmupViewModel | ViewModels/SpindleWarmupViewModel.cs | — | SpindleWarmupWindow.xaml |

### Services（8 個，手動 Singleton）

| Service | 說明 | HttpClient Timeout |
|---------|------|--------------------|
| MachineControlService | HTTP 通訊（70+ 方法） | polling 3s / estop 2s / atc 60s / probe 120s |
| ConfigurationService | INI/HAL/XML/PostGUI/NGC 生成 | config 10s / upload 20s |
| AlarmService | 集中日誌 ≤500 筆 + 跑馬燈 + 每日 log | — |
| AppSettings | appsettings.json（ServerUrl/Language/Theme） | — |
| AuthService | 4 級角色權限 + SHA-256 | — |
| HardwareScanService | EtherCAT 掃描快取 | — |
| LocalizationService | 多語言 ResourceDictionary | — |
| ThemeService | 主題切換（3 主題） | — |

### Models

| 類別 | 檔案 | 說明 |
|------|------|------|
| MachineConfig | MachineModels.cs | 機台完整設定（axes/IO/type/ATC） |
| AxisSetting | MachineModels.cs | 單軸（EtherCAT mapping + 運動參數） |
| MachineStatus | MachineStatus.cs | ~200 個 Observable 屬性（UI 自動綁定） |
| MachineStatusData | MachineModels.cs | /v2/status JSON DTO |
| ToolEntry | MachineModels.cs | 刀具表（ObservableObject） |
| ProbeResult / ProbeParameters | MachineModels.cs | 探測結果/參數 |
| ToolLifeEntry / ToolLifeRaw | MachineModels.cs | 刀具壽命 |
| MachiningStats | MachineModels.cs | 加工統計 DTO |
| MaintenanceItem | MachineModels.cs | 維護保養（ObservableObject） |
| ResumeState | MachineModels.cs | 斷電續切狀態 |
| BackupInfo | MachineModels.cs | 備份資訊 |
| SpindleWarmupConfig / WarmupStep | MachineModels.cs | 暖機設定 |
| AlarmStatItem | HistoryViewModel.cs | 報警統計（巢狀類別） |
| GCodeLineItem | GCodeLineItem.cs | G-Code 行（LineNumber + IsCurrentLine） |

### 後端 server.py 關鍵結構

| 區塊 | 行號 | 說明 |
|------|------|------|
| Flask routes (53 個) | 全檔 | REST API（見下方 API 端點表） |
| _probe_* (9 個) | 1253-1750 | 探測內部函式（edge/corner/center/boss/angle/calibrate） |
| error_sniffer_loop | 321 | 背景：監聽 LinuxCNC 錯誤 |
| read_servo_raw_data | 347 | 背景：讀取 EtherCAT Servo IO |
| _machining_stats_tracker | ~2938 | 背景：加工時間統計（1s 輪詢 RUNNING） |
| _maintenance_tracker | ~3032 | 背景：維護運轉時數（5s 輪詢） |
| _resume_state_tracker | ~3114 | 背景：斷電續切存檔（5s） |

---

## 4. Architecture & Data Flow

```
┌─────────────────────────────────────────────────────────┐
│  WPF 前端（Windows 工控觸控機）                            │
│                                                          │
│  MainViewModel (Root)                                    │
│    ├─ MonitorVM（G-Code/MDI/3D 預覽/檔案管理）            │
│    ├─ HistoryVM（LOG/STATS）                             │
│    ├─ OffsetsVM（G54-G59）                               │
│    ├─ ToolTableVM（刀具表 + 壽命）                        │
│    ├─ AtcVM（手動/自動刀庫）                              │
│    ├─ ProbingVM（27 種探測 + Tool Setter）                │
│    ├─ MacroVariablesVM（#變數讀寫）                       │
│    ├─ FileManagerVM（程式檔案）                           │
│    └─ SettingsVM → 聚合子 VM：                           │
│         HardwareDiscovery / AxisMapping / AxisParameter  │
│         IoMonitor / MachineConfig / SpindleSettings      │
│         AtcBasic/Axis/IoSettings / MacroVariables        │
│         Backup / Maintenance                             │
│                                                          │
│  Services（手動 Singleton，非 DI）                        │
│    ├─ MachineControlService  ← 4 個 HttpClient           │
│    ├─ ConfigurationService   ← INI/HAL/XML/NGC 生成      │
│    ├─ AlarmService / AuthService / AppSettings            │
│    ├─ HardwareScanService / LocalizationService          │
│    └─ ThemeService                                       │
└──────────────────────┬───────────────────────────────────┘
                       │ HTTP REST (500ms 輪詢)
                       ▼
┌─────────────────────────────────────────────────────────┐
│  Flask 後端（LinuxCNC 機台）                               │
│  53 API routes + 5 背景 daemon threads                   │
│  9 個 JSON 持久化檔案                                     │
│  LinuxCNC NML 進程間通訊                                  │
└─────────────────────────────────────────────────────────┘
```

**核心資料流（500ms 輪詢）：**
```
DispatcherTimer (500ms)
  → MachineControlService.GetStatusAsync()
  → GET /v2/status
  → MainViewModel.UpdateMachineData(MachineStatusData)
  → MachineStatus : ObservableObject（~200 屬性）
  → UI Binding 自動刷新
```

**設定部署流程：**
```
硬體掃描 → 軸指派 → 軸參數 → IO 設定
  → ConfigurationService 生成 INI/HAL/XML/PostGUI/NGC
  → HTTP POST /api/config/update → 後端寫入檔案
  → POST /api/machine/restart → 後端重啟 LinuxCNC
  → 前端輪詢確認重啟完成
```

**後端 JSON 持久化（9 檔）：**
`machining_stats.json` / `maintenance.json` / `resume_state.json` / `tool_life.json` / `probe_settings.json` / `MachineConfig.json` / `passwords.json` / `appsettings.json` / `frontend_topology.json`

---

## 5. 後端 API 端點（53 路由）

### 核心控制

| 方法 | 路由 | 功能 |
|------|------|------|
| GET | `/v2/status` | 即時狀態（Position/Task_State/Servo_IO/Active_WCS/Homed/Probe_Input/Program_Total_Lines） |
| GET | `/v2/errors` | 錯誤快取 ≤20 筆 |
| GET | `/v2/offsets` | G54-G59 偏移值 |
| POST | `/v2/motion/jog` | JOG 手動移動 |
| POST | `/v2/program/run` | 執行 G-Code（支援 start_line 參數） |
| POST | `/v2/program/pause` | Feed Hold |
| POST | `/v2/program/resume` | 繼續 |
| POST | `/v2/program/stop` | abort 全停 |
| POST | `/v2/program/step` | 單節執行 |
| POST | `/v2/machine/reset` | ESTOP 解除 / ON-OFF |
| POST | `/v2/machine/estop` | 緊急停止 |
| POST | `/v2/machine/home` | 原點復歸（-1=全軸, 0~5=單軸） |
| POST | `/v2/machine/mode` | MAN/AUTO/MDI 切換 |
| POST | `/v2/mdi` | MDI 指令 |
| POST | `/v2/override/feed` | Feed Override |
| POST | `/v2/override/spindle` | Spindle Override |
| POST | `/v2/program/block_delete` | Block Delete 開關 |
| POST | `/v2/program/optional_stop` | Optional Stop (M01) |

### 刀具 & 探測

| 方法 | 路由 | 功能 |
|------|------|------|
| GET | `/v2/tool/table` | 讀取刀具表 |
| POST | `/v2/tool/save` | 寫入刀具表 |
| GET | `/v2/tool/life` | 刀具壽命 |
| POST | `/v2/tool/life/config` | 壽命設定 |
| POST | `/v2/tool/life/reset` | 壽命歸零 |
| POST | `/v2/probe/run` | 探測循環（9 種類型） |
| POST | `/v2/hal/setp` | HAL 訊號設定 |
| GET | `/v2/atc/status` | ATC 感測器狀態 |

### 巨集變數

| 方法 | 路由 | 功能 |
|------|------|------|
| POST | `/v2/macro/read` | 讀取指定 #id |
| POST | `/v2/macro/write` | MDI 安全寫入 |
| POST | `/v2/macro/readall` | 範圍讀取 |

### 統計 & 維護 & 斷電續切

| 方法 | 路由 | 功能 |
|------|------|------|
| GET | `/v2/machining/stats` | 加工統計 |
| POST | `/v2/machining/stats/reset` | 重置統計 |
| GET | `/v2/maintenance` | 維護保養項目 |
| POST | `/v2/maintenance` | 儲存維護項目 |
| POST | `/v2/maintenance/reset` | 歸零項目時數 |
| GET | `/v2/resume/state` | 斷電續切狀態 |
| POST | `/v2/resume/clear` | 清除續切狀態 |

### 備份 & 設定 & 檔案

| 方法 | 路由 | 功能 |
|------|------|------|
| POST | `/v2/backup/create` | 建立備份 |
| GET | `/v2/backup/list` | 備份清單 |
| POST | `/v2/backup/restore` | 還原備份 |
| POST | `/v2/backup/delete` | 刪除備份 |
| POST | `/api/config/update` | 更新 INI/HAL/XML/NGC |
| POST | `/api/machine/restart` | 重啟 LinuxCNC |
| POST | `/api/files/upload` | 上傳 G-Code |
| GET/POST | `/api/ethercat/scan` | EtherCAT 掃描 |
| GET | `/v2/program/list` | 程式檔案清單 |
| GET | `/v2/program/read` | 讀取程式內容 |
| POST | `/v2/program/delete` | 刪除程式 |
| POST | `/v2/program/rename` | 重命名程式 |
| POST | `/v2/program/load` | 載入程式到 LinuxCNC |

---

## 6. High-Risk Areas

### 最高風險檔案

| 檔案 | 行數 | 風險原因 |
|------|------|---------|
| **server.py** | ~3200 | 單體 Flask，53 路由 + 5 背景執行緒 + NML 連線，修改易產生副作用 |
| **MainViewModel.cs** | ~1330 | 根 VM，500ms 輪詢 + JOG + 電源/急停 + 導航 + 統計，觸及面最廣 |
| **MachineControlService.cs** | ~1165 | 70+ 公開方法 + 4 個 HttpClient timeout 設定，API 變更必須前後端同步 |
| **ConfigurationService.cs** | ~830 | INI/HAL/XML/NGC 生成，寫錯 = 機台無法啟動 |
| **MachineModels.cs** | ~750 | 28+ 類別共用，修改一個影響多處 |
| **ProbingViewModel.cs** | ~650 | 27+ 探測命令，參數傳遞鏈長（VM → Service → 後端 → G38.2） |

### 高風險操作

| 操作 | 風險 | 防護 |
|------|------|------|
| 急停 / abort | 機台立即停止 | `_estopClient` 獨立 2s timeout + 樂觀更新 |
| JOG 連續移動 | 超行程/碰撞 | VM `CanExecuteMotion()` + Service `ValidateAction()` 雙重守衛 |
| 設定部署 | 寫錯 INI/HAL = 機台無法啟動 | 部署前生成完整檔案 → 上傳 → 重啟 → 輪詢確認 |
| G10 寫入 WCS | 座標偏移錯誤 | 後端 `wait_complete()` 等待 MDI 完成 + 前端 300ms 延遲再讀 |
| 斷電續切 | 從錯誤行恢復 = 撞刀 | 彈窗確認 + 可取消 |

---

## 7. 程式碼修改規範（必須遵守）

### 日期註解
每次修改程式碼（前端 & 後端），必須在修改處加上：
```
// [YYYY-MM-DD] 說明修改內容
```

### UI 樣式規範

#### 分頁選取色
- **子分頁 / Tab 選取**：統一使用 `BaseRadioBtnStyle`（繼承即可），選取色 **#5E70FF 藍紫漸層**（#7A8AFF→#5E70FF→#4A5CE0）
- **按鈕 toggle**：使用 `Brush.Checked`（#007ACC 藍），如 SINGLE BLOCK / FLOOD / MIST
- **主導航列**：使用 `NavBtnStyle`（已繼承 BaseRadioBtnStyle）
- **禁止**在新分頁中自訂選取色

#### 區塊標頭
- **面板標題**：`<Border Style="{StaticResource PanelTitleBorder}">` + `<TextBlock Style="{StaticResource PanelTitleText}"/>`
- **群組標題**：`<TextBlock Style="{StaticResource GroupHeaderText}"/>`
- **禁止** inline 寫死標頭背景色

#### 按鈕樣式
- 一律繼承 `BaseBtnStyle`（深灰漸層 #5E5E5E→#3A3A3E→#2A2A2E）
- **禁止** inline `Background="#xxx"`
- **唯一例外**：`<!-- KEEP: status -->` 標記的狀態按鈕（DataTrigger 動態色彩）

#### 按鈕 Style 繼承鏈
```
BaseBtnStyle (Theme.Dark.xaml 全域)
├── AtcBtnStyle → PanelBtnStyle (AtcView 本地)
├── CycleBtnStyle (CycleControl 本地)
├── ToolBtnStyle → PanelBtnStyle (ToolTableView 本地)
├── OffsetBtnStyle (Theme.Dark.xaml 全域) → WcsBtnStyle (OffsetsView 本地)
├── JogArrowBtn → JogRotaryBtn (JogPanel 本地)
└── 直接使用：DRO ZERO/REF、Settings SCAN/UPDATE、Monitor SEND、HeaderBar RETRY、Probing SIM
```

#### KEEP: status 按鈕例外清單
| 按鈕 | 檔案 | DataTrigger 色彩 |
|------|------|-----------------|
| CYCLE START | CycleControl | InterpState=RUNNING → 綠 |
| FEED HOLD | CycleControl | InterpState=PAUSED → 橘 |
| STOP | CycleControl | IsPressed → 紅 |
| POWER | CycleControl | IsPower=True → 綠 |
| E-STOP | CycleControl | IsEstop=False → 紅 |
| REF X/Y/Z/A/B/C | DroDisplay | IsXHomed~IsCHomed=True → 綠 / False → 紅 |
| REF ALL | DroDisplay | IsAllHomed=True → 綠 / False → 紅 |
| FWD / REV | JogConfig | SpindleDirection=1/-1 → 藍 |
| 夾刀 / 鬆刀 | AtcView | IsDrawbarOn=True/False → 藍 |

### 多語言（i18n）— 強制規則
- **所有新增 UI 文字**必須 `{DynamicResource Str.xxx}`
- 同步新增至 `Lang.zh-TW.xaml` **和** `Lang.en-US.xaml`
- **禁止** XAML 硬編碼中文
- Key 命名：`Str.Nav.*` / `Str.Btn.*` / `Str.Label.*` / `Str.Atc.*` / `Str.Probe.*` / `Str.Setting.*` / `Str.Tool.*` / `Str.Offset.*` / `Str.Header.*` / `Str.History.*` / `Str.Maint.*` / `Str.Macro.*` / `Str.Warmup.*` / `Str.Backup.*`
- 英文技術用語（MDI / G54 / M6 G43 / X+ / SPINDLE RPM）可保持 inline

---

## 8. 專案特定規則

- `DiscoveredSlave` 的 `Index`、`VendorId`、`ProductCode` **一律顯示原始整數值，禁止轉十六進位**
- MVVM：ViewModel 用 `[ObservableProperty]` / `[RelayCommand]`，View Code-behind 最小化
- Service 用手動 Singleton（`Instance` 屬性），非 DI Container
- 後端 URL 統一從 `AppSettings.Instance.ServerUrl` 取得
- RadioButton Tab 切換一律用 `StringEqualConverter`（App.xaml 全域註冊）+ `DataTrigger Visibility`

---

## 9. Common Modification Patterns

### 新增一個主頁面
1. `ViewModels/` 新增 `XxxViewModel.cs`（繼承 `ObservableObject`）
2. `Views/Pages/` 新增 `XxxView.xaml`（`d:DataContext` 綁定 VM）
3. `MainViewModel.cs` 新增 VM 屬性 + `Navigate()` case
4. `MainView.xaml` 新增 `<RadioButton>` 導航 + `CachedContentControl` 內容
5. `Lang.zh-TW.xaml` + `Lang.en-US.xaml` 新增 `Str.Nav.Xxx`

### 新增一個設定子 Tab
1. `ViewModels/` 新增 `XxxViewModel.cs`
2. `SettingsViewModel.cs` 新增 `public XxxViewModel XxxVM { get; } = new();`
3. `SettingsView.xaml` 的 `<TabControl>` 新增 `<TabItem>`
4. i18n 新增 `Str.Setting.Xxx`

### 新增後端 API 端點
1. `server.py` 新增 `@app.route()` + 處理函式
2. `MachineControlService.cs` 新增 `XxxAsync()` 方法
3. `MachineModels.cs` 新增 DTO 類別（如需）
4. ViewModel 呼叫 Service 方法
5. 更新此 CLAUDE.md API 端點表

### 新增探測類型
1. `server.py` 新增 `_probe_xxx()` 內部函式 + route mapping
2. `ProbingViewModel.cs` 新增 `[RelayCommand]` + 按鈕
3. `ProbingView.xaml` 新增分頁內容
4. `ProbeResult` / `ProbeParameters` 視需要擴展

---

## 10. Build & Run

```bash
# 前端（WPF，需 .NET 10 SDK + Windows）
dotnet build CncController/CncController.csproj
dotnet run --project CncController/CncController.csproj

# 後端（LinuxCNC 機台 Ubuntu RT）
python3 guardian.py   # 正式（帶守護，Exit Code 42 = API 觸發重啟）
python3 server.py     # 除錯用（直接執行）
```

**NuGet 套件：**
- `CommunityToolkit.Mvvm` 8.4.0 — MVVM Source Generator
- `HelixToolkit.Wpf` 3.1.2 — 3D 視覺化
- `Microsoft.Xaml.Behaviors.Wpf` 1.1.135 — XAML Behavior/Trigger

---

## 11. 工控安全機制

| 機制 | 說明 |
|------|------|
| 急停獨立通道 | `_estopClient`（2s timeout），觸發後樂觀更新 + 取消 JOG |
| 雙重運動守衛 | VM `CanExecuteMotion()` + Service `ValidateAction()` |
| 原子狀態更新 | 先計算快照再統一套用，杜絕 UI 中間態 |
| HttpClient 分離 | polling(3s) / estop(2s) / config(10s) / upload(20s) / atc(60s) / probe(120s) |
| 連續失敗計數 | ≥3 次才判定 Disconnected |
| 異常處理 | 零空 catch，全部記錄至 AlarmService |
| 日誌持久化 | 每日滾動 `logs/cnc-yyyy-MM-dd.log` |
| 密碼外部化 | SHA-256 雜湊存於 `passwords.json` |
| IP 外部化 | `appsettings.json` → `AppSettings.Instance.ServerUrl` |

---

## 12. 功能完成狀態

### ✅ 已完成（全部核心 + 應該有 + 加分部分）

<details>
<summary>展開完整清單（40+ 項）</summary>

- DRO 5 欄對齊 PB 版 + 單軸歸零/原點復歸 + Homed 紅綠燈
- JOG X/Y/Z + A/B/C 旋轉軸（連續 + 寸動）
- 機台類型定義（3/4/5/6 軸可配置）+ UI 即時連動
- 加工循環控制（Cycle Start / Stop / Feed Hold / Single Block）
- 電源 / 急停（樂觀更新 + 獨立通道）
- MDI 送出（歷史記錄 ComboBox）
- 冷卻液 Flood + MIST
- G-Code 載入/預覽/上傳 + 行號高亮（VirtualizingStackPanel）
- Offsets G54-G59 表格 + SET TO ZERO / CLEAR / SAVE + 右欄即時座標
- Feed / Spindle Override（Slider + 後端連通）
- GO TO HOME / GO TO ZERO / G30
- 系統狀態顯示 + 警報跑馬燈
- 使用者登入/登出/角色權限（4 種）
- 操作歷史（五級過濾）+ LOG/STATS 雙 Tab + 報警統計 Top 10
- 機台設定（11 個子 Tab：SCAN/MAPPING/AXIS/IO/IN MAP/OUT MAP/SPINDLE/ATC×3/MACRO VAR/BACKUP/MAINTENANCE）
- IO Monitor + IN/OUT MAP 即時 LED
- 設定檔生成部署（INI/HAL/XML/PostGUI/NGC）+ 重啟
- 開機硬體自動驗證
- 全套工控安全重構
- ToolInfo 對齊 PB 版（即時刀具資訊 + G43/G49 高亮）
- OffsetsView 對齊 PB 版（7 欄 + G59.1-G59.3 擴展）
- Tool Table CRUD + TOOL LIFE 雙 Tab
- ATC 自動刀庫（MANUAL 8 按鈕 + AUTOMATIC 5 按鈕 + 夾刀/鬆刀 M24/M25）
- ATC 設定三分頁（BASIC/AXIS/IO）+ NGC 巨集生成 + IO 衝突檢查
- ATC 即時狀態回讀（感測器 LED + sim_atc.hal）
- Probing 完整 8 分頁（Outside/Inside/Boss&Pocket/Ridge&Valley/EdgeAngle/Calibrate/Help/ToolSetter）
- Block Delete / M01 Break
- ATC PROGRAM TOOLS（GCodeParser 解析 T 號）
- SPINDLE 設定（EtherCAT + 剛性攻牙 + M19）
- CachedContentControl 分頁快取
- Dashboard D_1~D_5 對齊 PB 版
- 主軸正反轉控制 + 按鈕動態色彩
- 程式檔案管理（列表/預覽/載入/刪除/重命名）
- 巨集變數監控（#1~#5999 讀寫 + DataGrid）
- 主軸暖機程式（SpindleWarmupWindow）
- 備份/還原（後端 + 前端 JSON）
- 刀具壽命管理（後端 1s 追蹤 + 進度條 + 壽命設定）
- StringEqualConverter 全域轉換器
- 加工時間統計（累計/循環次數/預估剩餘）
- 維護保養提醒（CRUD + 運轉時數追蹤 + 進度條）
- 3D 刀具路徑預覽（HelixViewport3D + G0/G1/G2/G3）
- 斷電續切（後端 5s 存檔 + 開機彈窗 + RunFromLine）
- 主題切換（3 主題 + 持久化）+ 全域 DynamicResource 色彩統一
- 多語言持久化 + 主題/語言選單打勾

</details>

### ❌ 尚未實作

| 功能 | 說明 | 難度 |
|------|------|------|
| Conversational 對話式加工 | 填參數自動產生 G-Code（鑽孔陣列/面銑/溝槽） | 高 |
| 遠端監控 | 手機/網頁看機台狀態（WebSocket 推播） | 高 |
| 能耗監控 | 主軸/伺服功率統計（ESG 節能報表） | 中 |
| Probing Tool Setter | 基本 UI 已完成，待實機測試連通 | 中 |
| Probing Rotary Axis | 分頁目前 disabled | 中 |

### ⚠️ 需加強

| 項目 | 說明 |
|------|------|
| Velocity / Rapid Override | V/R Slider 暫靜態（無後端連動），F/S 已連通 |

---

## 13. 快捷鍵（MainWindow.xaml InputBindings）

| 按鍵 | Command | 說明 |
|------|---------|------|
| ESC | EmergencyAbortCommand | 全機停止（abort + 清除警報） |
| F1 | TogglePowerCommand | 電源開/關 |
| F2 | ToggleEstopCommand | 急停 |
