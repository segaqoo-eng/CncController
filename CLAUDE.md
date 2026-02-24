# CLAUDE.md

本檔案提供 Claude Code 在此專案中的開發指引。

---

## 角色定義

你是 **LinuxCNC + EtherCAT 工控專家**，同時精通 WPF/MVVM 前端開發。

**核心技術棧：**
- CiA 402 伺服驅動器協議、EtherCAT 拓樸設定
- LinuxCNC HAL/INI/XML 組件配置、G-code 程式設計
- WPF + CommunityToolkit.Mvvm（MVVM Source Generator）
- Python Flask 後端（LinuxCNC NML 整合）

**語言規則：** 回答一律使用**繁體中文**。

---

## 程式碼修改規範

每次修改程式碼（前端 & 後端），必須在修改處加上註解：
```
// [YYYY-MM-DD] 說明修改內容
```

---

## Build & Run

```bash
# 前端（WPF，需 .NET 10 SDK + Windows）
dotnet build CncController/CncController.csproj
dotnet run --project CncController/CncController.csproj

# 後端（LinuxCNC 機台 Ubuntu RT）
python3 guardian.py   # 正式環境（帶守護）
python3 server.py     # 除錯用
```

---

## 架構概覽

```
┌─────────────────────────────────────────────────────┐
│  WPF 前端（Windows）                                  │
│  MainViewModel (Root)                                │
│    ├─ SettingsViewModel（6 子 Tab VM）                │
│    ├─ MonitorViewModel（G-Code 載入/預覽/MDI）        │
│    ├─ HistoryViewModel（操作歷史/日誌過濾）            │
│    └─ OffsetsViewModel（G54-G59 工件座標系）           │
│                                                      │
│  Services（手動 Singleton，非 DI）                    │
│    ├─ MachineControlService  ← HTTP 通訊             │
│    ├─ ConfigurationService   ← INI/HAL/XML 生成      │
│    ├─ HardwareScanService    ← EtherCAT 掃描         │
│    ├─ AlarmService           ← 集中式日誌/警報        │
│    ├─ AuthService            ← 角色權限（4 級）       │
│    ├─ LocalizationService    ← 多語言（zh-TW）        │
│    └─ AppSettings            ← 外部化設定             │
└──────────────────────┬──────────────────────────────┘
                       │ HTTP (500ms 輪詢)
                       ▼
┌─────────────────────────────────────────────────────┐
│  Flask 後端（LinuxCNC 機台，192.168.0.137:5000）      │
│  server.py + guardian.py + smart_scan.py              │
│  LinuxCNC NML 進程間通訊                              │
└─────────────────────────────────────────────────────┘
```

**核心資料流：**
```
DispatcherTimer (500ms) → MachineControlService.GetStatusAsync()
  → GET /v2/status → 更新 MainViewModel.Status (MachineStatus : ObservableObject)
  → UI Binding 自動刷新
```

**設定工作流程：**
硬體掃描 → 軸指派 → 軸參數 → IO 設定 → 儲存 → ConfigurationService 生成 INI/HAL/XML → 上傳 → 後端重啟

---

## 專案特定規則

- `DiscoveredSlave` 的 `Index`、`VendorId`、`ProductCode` **一律顯示原始整數值，禁止轉換為十六進位**
- MVVM 模式：ViewModel 用 `[ObservableProperty]` / `[RelayCommand]`，View Code-behind 最小化
- Service 用手動 Singleton（`Instance` 屬性），非 DI Container
- 後端 URL 統一從 `AppSettings.Instance.ServerUrl` 取得

---

## 重要檔案速查

### 前端（CncController/）

| 類別 | 檔案 | 說明 |
|------|------|------|
| **Service** | `Services/MachineControlService.cs` | HTTP 通訊、狀態輪詢、所有機台命令 |
| **Service** | `Services/ConfigurationService.cs` | 機台設定 CRUD、生成 INI/HAL/XML/PostGUI HAL |
| **Service** | `Services/AppSettings.cs` | 外部化設定（`appsettings.json`：`ServerUrl` 等） |
| **Service** | `Services/AlarmService.cs` | 集中日誌（≤500 筆）+ 跑馬燈 + 每日 log 檔 |
| **ViewModel** | `ViewModels/MainViewModel.cs` | 根 VM、輪詢、導航、電源/急停、硬體自動驗證 |
| **ViewModel** | `ViewModels/SettingsViewModel.cs` | 設定頁協調、聚合 6 子 VM、`GenerateAndDeploy` |
| **ViewModel** | `ViewModels/MonitorViewModel.cs` | G-Code 載入/上傳/預覽、MDI 送出 |
| **ViewModel** | `ViewModels/OffsetsViewModel.cs` | G54–G59 座標系管理 |
| **ViewModel** | `ViewModels/HistoryViewModel.cs` | 操作歷史、五級過濾 |
| **ViewModel** | `ViewModels/AxisMappingViewModel.cs` | 軸與 EtherCAT Slave 對應 |
| **ViewModel** | `ViewModels/AxisParameterViewModel.cs` | 軸機械/運動/原點復歸參數 |
| **ViewModel** | `ViewModels/IoMonitorViewModel.cs` | 即時 IO 監控、CiA 402 狀態解析 |
| **Model** | `Models/MachineModels.cs` | MachineConfig、AxisSetting、DiscoveredSlave 等 |
| **Model** | `Models/MachineStatus.cs` | 機台即時狀態（Observable） |
| **Resource** | `Resources/Languages/Lang.zh-TW.xaml` | 全 UI 文字（繁體中文） |
| **Resource** | `Resources/Themes/Theme.Dark.xaml` | 深色主題 |

### 後端（Server/）

| 檔案 | 說明 |
|------|------|
| `server.py` | Flask REST API + LinuxCNC NML 整合 |
| `guardian.py` | 進程守護（Exit Code 42 = API 重啟） |
| `smart_scan.py` | EtherCAT 掃描 → `frontend_topology.json` |

---

## 後端 API 端點

| 方法 | 路由 | 功能 |
|------|------|------|
| GET | `/v2/status` | 機台即時狀態（含 Position、Task_State、Servo_IO、Active_WCS、Homed） |
| GET | `/v2/errors` | 錯誤快取（≤20 筆） |
| GET | `/v2/offsets` | G54–G59 工件座標偏移值 |
| POST | `/v2/motion/jog` | JOG 手動移動（axis, speed, dist） |
| POST | `/v2/program/run` | 執行 G-Code |
| POST | `/v2/program/pause` | 暫停 |
| POST | `/v2/program/resume` | 繼續 |
| POST | `/v2/program/stop` | 停止 |
| POST | `/v2/machine/reset` | ESTOP 解除 / ON-OFF 切換 |
| POST | `/v2/machine/estop` | 緊急停止 |
| POST | `/v2/machine/home` | 原點復歸（-1=全軸, 0~5=單軸） |
| POST | `/api/machine/restart` | 完全重啟 LinuxCNC |
| GET/POST | `/api/ethercat/scan` | 掃描 EtherCAT 拓撲 |
| POST | `/api/config/update` | 更新 INI/HAL/XML 設定檔 |
| POST | `/api/files/upload` | 上傳 G-Code 檔案 |

---

## 工控安全機制（已全部完成）

| 機制 | 說明 |
|------|------|
| 急停獨立通道 | `_estopClient`（2s timeout），觸發後樂觀更新 + 取消 JOG |
| 雙重運動守衛 | VM 層 `CanExecuteMotion()` + Service 層 `ValidateAction()` |
| 原子狀態更新 | 先計算快照再統一套用，杜絕 UI 中間態 |
| HttpClient 分離 | polling(3s) / estop(2s) / config(10s) / upload(20s) 各自獨立 |
| 連續失敗計數 | ≥3 次才判定 Disconnected |
| 異常處理 | 零空 catch，全部記錄至 AlarmService |
| 日誌持久化 | 每日滾動 `logs/cnc-yyyy-MM-dd.log` |
| 密碼外部化 | SHA-256 雜湊存於 `passwords.json` |
| IP 外部化 | `appsettings.json` → `AppSettings.Instance.ServerUrl` |

---

## 功能實作狀態

### ✅ 已完成

- DRO 對齊 PB 版（5 欄：ZERO | G5X WORK | MACHINE | DTG | REF + 單軸歸零/原點復歸 + Homed 紅綠燈）
- JOG X/Y/Z + A/B/C 旋轉軸（連續 + 寸動）+ 防呆
- 機台類型定義（MachineType 枚舉：3/4/5/6 軸可配置）
- 加工循環控制（Cycle Start / Stop / Feed Hold）
- 電源 / 急停（含樂觀更新 + 獨立通道）
- MDI 送出（含歷史記錄 ComboBox）
- 冷卻液 Flood（M8/M9）+ MIST（M7/M9）
- G-Code 載入/預覽/上傳
- Offsets Tab（G54–G59 表格 + 詳細面板 + 自動載入）
- Offsets SET TO ZERO / CLEAR / SAVE（G10 L20/L2 指令送出）
- Offsets 右欄即時座標（MC Current / Work Coord / Offset 綁定後端真實值）
- Feed Override 連通（+/- 按鈕、ProgressBar 綁定後端即時百分比）
- Spindle Override 連通（+/- 按鈕、ProgressBar 綁定後端即時百分比）
- Single Block 單節執行（CycleStart 切換 run/step，按鈕高亮狀態）
- GO TO HOME 原點復歸
- 系統狀態顯示 + 警報跑馬燈
- 使用者登入/登出/角色權限（4 種）
- 操作歷史（五級過濾）+ 加工計時器
- 機台設定介面（6 Tab：掃描/軸指派/軸參數/IO 監控/IN MAP/OUT MAP）
- IO Monitor 卡片連動機台類型（依軸映射過濾 Slave + 卡片標註軸名）
- 設定檔生成與部署（INI/HAL/XML/PostGUI → 上傳 → 重啟 → 輪詢確認）
- 開機硬體自動驗證
- 全套工控安全重構
- ToolInfo 版面對齊 PB 版（即時刀具號/刀長/刀徑 + G43/G49 高亮 + GO TO ZERO/G30 按鈕）

### ❌ 尚未實作

| 功能 | 說明 |
|------|------|
| Probing 探測循環 | Outside/Inside Corners、Boss/Pocket、Ridge/Valley、Edge Angle、Calibrate |
| ATC 自動刀庫 | 無換刀介面 |
| Tool Table 刀具表 | 僅靜態顯示，無完整管理 |
| Conversational 對話式加工 | 無 Facing/Holes/Pattern 產生器 |
| Block Delete / M01 | 按鈕存在但無 Command 綁定 |

### ⚠️ 需加強

| 功能 | 待改善 |
|------|--------|
| 3D 視圖 | 無刀具路徑模擬 |
| G-Code 預覽 | 無行號高亮/執行中行追蹤 |
| Tool Info | ~~未與 LinuxCNC 刀具表同步~~ ✅ 已綁定即時資料（刀具號/刀長/刀徑），尚缺完整刀具表管理 |
| Rapid Override | SliderControl 第三列暫靜態 100% |

### 📋 開發優先順序

1. **Probing 探測循環** — 工程師常用差異化功能
2. **Tool Table 管理** — ATC 前置需求
3. **Block Delete / M01** — 連接按鈕至後端
4. **G-Code 行號高亮** — 執行中行追蹤

---

## 開發工作流程

每完成一個功能修改後（前端 + 後端）：
1. 自動執行 `/compact` 壓縮對話
2. 更新 CLAUDE.md 功能實作狀態表
3. `git commit` + `git push`

---

## NuGet 套件

- `CommunityToolkit.Mvvm` 8.4.0 — MVVM + Source Generator
- `HelixToolkit.Wpf` 3.1.2 — 3D 視覺化
- `Microsoft.Xaml.Behaviors.Wpf` 1.1.135 — XAML Behavior/Trigger

---

## 每日工作紀錄

### 2026-02-23

- Offsets Tab 全新頁面（OffsetsView + OffsetsViewModel）
- DRO G54–G59 快選列
- MIST 霧化冷卻（M7/M9）
- GO TO HOME 原點復歸（後端 /v2/machine/home）
- HeaderBar EXIT 選單
- 後端 /v2/offsets 端點 + Active_WCS
- 版本號 `2026.02.23_OFFSETS_MIST_HOME`

### 2026-02-24

| 項目 | 說明 |
|------|------|
| **Offsets SET TO ZERO** | `SetToZeroAxisCommand`（G10 L20 P<n> X0/Y0/Z0/ALL）；選定 WCS 後透過 MDI 歸零 |
| **Offsets CLEAR SELECTED/ALL** | `ClearSelectedCommand`（G10 L2 P<n> 六軸歸零）；`ClearAllCommand`（遍歷 G54–G59 全部清零） |
| **Offsets SAVE TABLE** | `SaveTableCommand`（G10 L2 將 DataGrid 編輯值回寫至 LinuxCNC） |
| **Offsets 右欄連通** | MC Current 綁定 `MachineStatus.X/Y/Z`（機台座標）；Work Coord 綁定 `WorkX/Y/Z`（工件座標）；Offset 綁定 `SelectedRow.X/Y/Z` |
| **後端 Work_Position** | `/v2/status` 新增 `Work_Position`（= actual_position - g5x - g92 - tool） |
| **Feed Override 連通** | SliderControl 改為 ProgressBar 綁定 `Status.FeedOverride` + +/- 按鈕（每次 ±10%） |
| **Spindle Override 連通** | 同上，綁定 `Status.SpindleOverride` + +/- 按鈕 |
| **後端 Override 端點** | `POST /v2/override/feed` + `POST /v2/override/spindle`（`cnc_cmd.feedrate` / `spindleoverride`） |
| **Single Block 模式** | `IsSingleBlock` 開關；CycleStart 依此切換 `CycleStartAsync` / `StepProgramAsync` |
| **後端 Step 端點** | `POST /v2/program/step`（`cnc_cmd.auto(AUTO_STEP)`） |
| **版本號更新** | `2026.02.24_OFFSETS_OVERRIDE_SINGLEBLOCK` |
| **修正 GO TO HOME** | 後端加入 `teleop_enable(0)` 切換 Joint Mode，解決 home 指令被忽略 |
| **修正 Offsets 寫入** | `SelectOffset` 同步 `SelectedRow`，解決 SET TO ZERO 未選擇座標系 |
| **G10 動態軸數** | G10 指令改依 `EnabledAxes` 動態組合，不再寫死 XYZ |
| **MachineType 枚舉** | 新增 `MachineType`（ThreeAxis/FourAxisA/FiveAxisTrunnion/SixAxis 等） |
| **DRO 多軸動態** | A/B/C 軸行依 `IsAxisA/B/CEnabled` 自動顯示/隱藏 |
| **JOG A/B/C** | JogPanel 新增旋轉軸 JOG 按鈕（axis=3/4/5），依啟用狀態顯示 |
| **HAL 軸映射修正** | 移除寫死 X→0/Y→2/Z→3，改從 `Mappings.ChannelIndex` 動態取得 Slave Index |
| **MachineStatus 補齊** | 新增 DtgA/B/C + WorkA/B/C 屬性（六軸完整支援） |
| **Offsets DataGrid** | 新增 A/B/C 欄位，顯示全部六軸 offset 值 |
| **IO Monitor 連動** | 卡片依軸映射過濾（3 軸只顯示 3 張）+ 標題標註軸名（例如 "X Axis Slave #0"） |
| **版本號** | `2026.02.24_IOMONITOR_LINKAGE` |
| **ToolInfo PB 版** | 版面對齊 PB：T [N] / M6 G43 / G43-G49 高亮 / LENGTH / DIAM 即時綁定後端 |
| **後端刀具資訊** | `/v2/status` 新增 `Tool_Number`（tool_in_spindle）/ `Tool_Length`（tool_offset[2]）/ `Tool_Diameter`（tool_table） |
| **GO TO ZERO / G30** | `GoToZeroCommand`（G53 G0 X0 Y0 Z0）、`GoToG30Command`（G30）—— ToolInfo 按鈕 |
| **版本號** | `2026.02.24_TOOLINFO_PB` |
| **DRO 對齊 PB 版** | 5 欄佈局：ZERO X/Y/Z | G5X WORK（WorkX）| MACHINE（X）| DTG | REF X/Y/Z；標題動態顯示 G5X；移除 G54–G59 快選列 |
| **後端 Homed 狀態** | `/v2/status` 新增 `Homed` dict（~~joint[i].homed~~ → `cnc_stat.homed[i]`）|
| **DRO ZERO 按鈕** | `DroZeroAxisCommand`（G10 L20 單軸歸零）+ `DroZeroAllCommand`（全軸歸零）|
| **DRO REF 按鈕** | `RefAxisCommand`（單軸原點復歸 + 樂觀更新紅→綠）+ HomeAll 樂觀更新 |
| **Homed 狀態映射** | `MachineStatus.IsXHomed~IsCHomed + IsAllHomed`；底部按鈕紅/綠切換 |
| **版本號** | `2026.02.24_DRO_PB` |
| **修正 Homed 不變綠** | 後端 `cnc_stat.joint[i].homed` → `cnc_stat.homed[i]`（joint 回傳 dict 無屬性），JOG 端點同步修正 |
| **修正 Offsets SAVE 歸零** | 後端 MDI 加 `wait_complete()` 等待執行完畢 + 前端 ReloadTable 前加 300ms 延遲等待 .var 同步 |
| **修正 ToolInfo GO TO HOME** | 按鈕 Command 綁定從 `HomeAllCommand` 修正為 `GoToHomeCommand` |
