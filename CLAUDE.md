# CLAUDE.md

本檔案提供 Claude Code 在此專案中的開發指引。


---

See @memory.md for current bugs, progress, and decisions.
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
| **ViewModel** | `ViewModels/ToolTableViewModel.cs` | 刀具表 CRUD、LOAD/UNLOAD/M6G43/TOUCH OFF |
| **ViewModel** | `ViewModels/AtcViewModel.cs` | ATC 自動刀庫（MANUAL ATC + ATC AUTOMATIC） |
| **ViewModel** | `ViewModels/ProbingViewModel.cs` | 探測循環（Outside Corners 9 宮格 + 參數/結果） |
| **Model** | `Models/MachineModels.cs` | MachineConfig、AxisSetting、DiscoveredSlave 等 |
| **Model** | `Models/MachineStatus.cs` | 機台即時狀態（Observable） |
| **Model** | `Models/GCodeLineItem.cs` | G-Code 逐行模型（行號 + 高亮標記） |
| **Helper** | `Helpers/GCodeParser.cs` | G-Code 刀具號解析器（ATC PROGRAM TOOLS） |
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
| GET | `/v2/tool/table` | 讀取刀具表（cnc_stat.tool_table + tool.tbl 註解） |
| POST | `/v2/tool/save` | 寫入刀具表（tool.tbl + load_tool_table()） |
| POST | `/v2/probe/run` | 探測循環（edge/outside_corner/center + G38.2） |
| POST | `/v2/program/block_delete` | 切換 Block Delete 開關 |
| POST | `/v2/program/optional_stop` | 切換 Optional Stop (M01) 開關 |

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
- 機台類型 UI + 即時連動（繁中下拉選單 + DRO/JOG/Offsets/AxisParameters 動態切換）
- OffsetsView 對齊 PB 版（7 欄表格 + G59.1-G59.3 擴展座標系 + MAN/AUTO/MDI 模式切換）
- Tool Table 刀具表管理（DataGrid CRUD + LOAD/UNLOAD/M6G43/TOUCH OFF + 後端 /v2/tool/table & /v2/tool/save）
- ATC 自動刀庫頁面（MANUAL ATC 8 按鈕 + ATC AUTOMATIC 5 按鈕 + 雙模式切換 + ATC_Back.png 背景）
- MAN/AUTO/MDI 模式切換按鈕移至 JogPanel（全頁面可用）
- Probing 探測循環（8 分頁完整實作 + PROBE HELP 圖片瀏覽 + 後端 /v2/probe/run + HAL probe-input）
  - Outside/Inside Corners 九宮格（9 按鈕 WPF 向量繪圖）
  - Boss & Pocket（DIAM + X/Y 偏移輸入 + boss/pocket 兩種模式）
  - Ridge & Valley（HINT + X/Y 偏移輸入 + ridge/valley 兩種模式）
  - Edge Angle 3×3 九宮格 + SET ROTATION WCO + EDGE WIDTH
  - Calibrate（Ring/Square Inside/Outside + CAL ON AVG/X/Y ERROR）
  - PROBE HELP（7 張圖片循環瀏覽 PREV/NEXT）
  - 左面板上中下三段佈局（WORK OFFSETS / PROBING PARAMETERS / 結果）對齊 PB 版
  - 白底黑字輸入框 + 標籤靠右 + 工控大字體
- Block Delete / M01 Break（toggle 開關 + 後端 set_block_delete / set_optional_stop + DataTrigger 藍色高亮）
- G-Code 行號高亮（ItemsControl 逐行顯示 + 行號 + 黃底高亮執行中行 + VirtualizingStackPanel）
- ATC PROGRAM TOOLS（GCodeParser 解析 T 號 + LOAD TOOLS 按鈕 + 自動載入 + 去重排序）
- ATC 設定三分頁（ATC BASIC/ATC AXIS/ATC IO + AtcType 三種刀庫 + 伺服/IO/時序/排刀參數 + 即時連動）
- SPINDLE 設定分頁（EtherCAT Slave + 剛性攻牙 G33.1 + M19 定向 + EncoderPPR/MaxRPM）
- CachedContentControl 主分頁快取（消除切換延遲）
- Dashboard 底部五大區塊對齊 PB 版（D_1~D_5 欄寬/按鈕/控件全面對齊）
  - CycleControl：CLEAR PGM + 按鈕加大 45px
  - SliderControl：4 條 Slider（V/F/S/R）+ Spindle Load + 重置按鈕
  - JogConfig：JOG 標籤 + JOG 速度 Slider + FEEDRATE MM/M + SPINDLE RPM + REV/STOP/FWD
  - 主軸正反轉控制（SpindleFwd/Rev/Stop → M3/M4/M5）

### ❌ 尚未實作

#### 必須有（上線前必備）

| 功能 | 說明 | 難度 |
|------|------|------|
| 程式檔案管理 | 機台端 NC 檔案列表/刪除/重命名（目前只能上傳，無法瀏覽） | 低 |
| 巨集變數監控 | #1~#5999 變數讀取/修改（調試換刀巨集、探測參數必備） | 中 |
| ATC 即時狀態回讀 | 讀 HAL pin 真實 IO → 刀盤位置/感測器/夾刀確認（不能只靠本地模擬） | 中 |
| 刀具壽命管理 | 累計切削時間/次數 → 到壽命提醒換刀 | 中 |
| 主軸暖機程式 | M3 逐步升速（冷機直接高速傷軸承） | 低 |
| 備份/還原 | 一鍵備份 INI/HAL/刀具表/WCS/巨集變數，還原到指定時間點 | 中 |

#### 應該有（提升可靠度）

| 功能 | 說明 | 難度 |
|------|------|------|
| 3D 刀具路徑預覽 | 解析 G-Code 畫出刀具路徑（目前 3D 視圖空的） | 高 |
| 加工時間統計 | 單件時間/累計時間/預估剩餘（生產排程用） | 低 |
| 維護保養提醒 | 潤滑油/濾網/皮帶 — 依運轉時數提醒（可設定週期） | 中 |
| 報警履歷分析 | 分類統計（哪個報警最頻繁） | 低 |
| 斷電續切 | 記錄中斷行號 → 重開機從斷點繼續 | 高 |

#### 加分項（差異化）

| 功能 | 說明 | 難度 |
|------|------|------|
| Conversational 對話式加工 | 填參數自動產生 G-Code（鑽孔陣列/面銑/溝槽/螺紋） | 高 |
| 遠端監控 | 手機/網頁看機台狀態（WebSocket 推播） | 高 |
| 能耗監控 | 主軸/伺服功率統計（ESG 節能報表） | 中 |
| Probing Tool Setter | TOOL SETTER 分頁（基本 UI + 參數面板已完成，待實機測試） | 中 |
| Probing Rotary Axis | ROTARY AXIS 分頁（目前 disabled） | 中 |

### ⚠️ 需加強

| 功能 | 待改善 |
|------|--------|
| 3D 視圖 | 無刀具路徑模擬 |
| Velocity / Rapid Override | V/R Slider 暫靜態（無後端連動），F/S 已連通 |

### 📋 開發優先順序

1. **程式檔案管理** — NC 檔案列表/刪除/重命名
2. **巨集變數監控** — #1~#5999 讀寫
3. **ATC 即時狀態回讀** — HAL pin 真實 IO
4. **加工時間統計** — 單件/累計/預估
5. **刀具壽命管理** — 切削時間/次數追蹤
6. **主軸暖機程式** — 逐步升速
7. **備份/還原** — 一鍵備份還原

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

### 2026-03-04

| 項目 | 說明 |
|------|------|
| **PROBING 探測循環（完整 8 分頁）** | ProbingViewModel + ProbingView：Outside/Inside Corners 九宮格 + Boss & Pocket + Ridge & Valley + Edge Angle + Calibrate + PROBE HELP |
| **Outside/Inside Corners** | 3×3 九宮格 WPF 向量繪圖（紫球+綠十字+箭頭+灰方塊），9 個探測按鈕 |
| **Boss & Pocket** | DIAM + X/Y 偏移輸入框 + boss（外→內探測）/ pocket（內→外探測）雙模式 |
| **Ridge & Valley** | HINT + X/Y 偏移輸入 + ridge（脊）/ valley（谷）雙模式 |
| **Edge Angle** | 3×3 九宮格 + SET ROTATION WCO 按鈕 + EDGE WIDTH 輸入 + atan2 角度計算 |
| **Calibrate** | Ring/Square Inside/Outside（2×2 視覺按鈕）+ CAL ON AVG XY/X/Y ERROR（3 按鈕）+ CALIBRATION WIDTH X/Y |
| **PROBE HELP** | 7 張圖片（Image(1)~(7).png）循環瀏覽 + PREV/NEXT 按鈕 |
| **左面板佈局（對齊 PB 版）** | 上中下三段：WORK OFFSETS（G54~G59.3 + PROBE POSITION ONLY）/ PROBING PARAMETERS（5 行標籤靠右+白底輸入框）/ 4 按鈕+4×3 結果 |
| **工控大字體** | WCS 16px / 參數標籤 14px / 輸入框 16px / 結果值 16px / 按鈕 14px / 子頁籤 14px |
| **後端 /v2/probe/run** | edge / outside_corner / center / boss / pocket / ridge / valley / edge_angle / calibrate 9 種探測類型 |
| **ProbeResult + ProbeParameters 模型** | Angle / EdgeWidth / WidthX / WidthY / Diameter / OffsetX / OffsetY / EdgeWidth |
| **HAL probe-input** | ConfigurationService 自動接線 `motion.probe-input` |
| **版本號** | `2026.03.04_PROBING` |

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
| **機台類型 UI** | 繁體中文下拉選單 + 設定存讀連通（`bf1b481`） |
| **機台類型即時連動** | DRO/JOG/Offsets/AxisParameters 依 MachineType 動態顯示/隱藏（`6c97e67`） |
| **修正雙擊 exe 無法開啟** | BoolToVis 資源移至 App.xaml 全域（`d7b96f2`） |
| **設定連動延遲** | 所有連動延遲至 UPDATE 按鈕 + 手動 Scan 更新 IO Slave 下拉（`578850e`） |
| **IO Monitor 修正** | 修正下拉選單過早觸發映射表重建 + IO 卡片改為標註不過濾（`ac6294b`） |
| **修正 SAVE TABLE 值歸零** | 後端改讀記憶體值（`cnc_stat.g5x_offset`）取代 .var 檔（僅關機寫入）（`c8c1840`） |
| **OffsetsView 對齊 PB 版** | 7 欄表格（X/Y/Z/A/B/C + Name）+ G59.1-G59.3 擴展座標系 + MAN/AUTO/MDI 模式切換按鈕（`34ff2e7`） |
| **版本號** | `2026.02.24_OFFSETS_PB` |

### 2026-02-25

| 項目 | 說明 |
|------|------|
| **修正 CLEAR ALL/SELECTED → RELOAD 顯示舊值** | 新增 server.py in-memory WCS cache（`_wcs_cache`）；MDI 送出 G10 L2 後即時更新 cache；`read_work_offsets()` 優先級：.var < cache < cnc_stat active WCS（`5a4e14b`） |

### 2026-03-03

| 項目 | 說明 |
|------|------|
| **TOOL 分頁（對齊 PB 版）** | ToolTableViewModel + ToolTableView：DataGrid（全軸 offset + FNT/BAK ANG + ORIENT + REMARK）+ CRUD（ADD/DELETE/SAVE/RELOAD）+ 右側 TOOL CHANGE PANEL（LOAD/UNLOAD/M6G43/TOUCH OFF）+ TOOL_BACK.png 背景 |
| **ToolEntry 模型** | MachineModels.cs 新增 ToolEntry（ObservableObject，全軸 offset + Diameter + FrontAngle/BackAngle/Orientation/Remark） |
| **後端刀具表端點** | `/v2/tool/table`（GET）讀取 cnc_stat.tool_table + tool.tbl 註解；`/v2/tool/save`（POST）寫入 tool.tbl + cnc_cmd.load_tool_table() |
| **MachineControlService 擴充** | GetToolTableAsync() + SaveToolTableAsync()（ApiResponse<List<ToolEntry>>） |
| **ATC 自動刀庫分頁** | AtcViewModel + AtcView：ATC_Back.png 背景 + 三欄佈局（左面板/中央/右面板）；MANUAL ATC（8 按鈕：AIR BLAST / RETR DUST BOOT / CLAMP TOOL / RELEASE TOOL / ORIENT SPINDLE / UNLOCK SPINDLE / HEAD UP / HEAD DOWN）+ PROGRAM TOOLS（預留 ListBox） |
| **ATC 右面板** | ATC AUTOMATIC CONTROL PANEL：LOAD SPINDLE（T{n} M6）/ UNLOAD SPINDLE（T0 M6）/ STORE TOOL IN RACK / M6 G43（T{n} M6 G43）/ TOUCH OFF CURRENT TOOL（G10 L11 P{n} Z0）+ MDI |
| **MAN/AUTO/MDI 搬遷** | 模式切換按鈕從 OffsetsView 移至 JogPanel 底部（全頁面可用）；SetModeCommand 從 OffsetsViewModel 移至 MainViewModel |
| **JogPanel 寬度修正** | 180→250，避免 X+/X- 按鈕被裁切 |
| **ConfigurationService 修正** | HAL：isServo && !isPulseGen 條件；INI：新增 OFFSET_COLUMNS 動態軸欄位 |
| **版本號** | `2026.03.03_TOOL_ATC` |

### 2026-03-05

| 項目 | 說明 |
|------|------|
| **Dashboard 底部對齊 PB 版（D_1~D_5）** | DashboardPanel 欄寬 350\|250\|490\|430\|*；CycleControl / SliderControl / JogConfig 全面重寫 |
| **DashboardPanel 欄寬** | 220\|180\|550\|400\|* → 350\|250\|490\|430\|*（ToolInfo 截斷問題一併解決） |
| **CycleControl 對齊 D_1** | GO TO HOME → CLEAR PGM（ClearProgramCommand）；按鈕高度 38→45px；DesignWidth 220→350 |
| **SliderControl 完全重寫 D_4** | Spindle Load 顯示 + 4 條 Slider（Velocity/Feed/Spindle/Rapid Override 0~200%）+ V/F/S/R 100% 重置按鈕；ProgressBar 改為 Slider 可拖拉 |
| **JogConfig 對齊 D_5** | Cont.→JOG 標籤；增量值 10.0/1.0/0.1→0.1/0.01/0.001；新增 JOG 速度 Slider 0~100%；FEEDRATE MM/M + SPINDLE RPM 左右分欄；REV/STOP/FWD 改用 BaseBtnStyle |
| **主軸控制** | SpindleFwd（M3）/ SpindleRev（M4）/ SpindleStop（M5）+ JogSpindleRpm 參數 |
| **MainViewModel 新增** | ClearProgramCommand、ResetFeedOverride/SpindleOverride/VelocityOverride/RapidOverride、SpindleFwd/Rev/Stop、JogSpeedPercent、JogSpindleRpm、VelocityOverride、RapidOverride |
| **MachineStatus 新增** | SpindleLoad（主軸負載百分比） |
| **版本號** | `2026.03.05_DASHBOARD_PB` |
| **TOOL SETTER 分頁** | ProbingView 新增 TOOL SETTER 垂直 Tab + TOOL_BACK.png 背景切換 + 兩欄參數面板（6 模式按鈕）|
| **探針模擬 comp→near** | sim_probe.hal 改用 `near` 組件（`\|pos-target\|<=0.05` 觸發），comp 的 `>=` 比較向負方向立刻觸發 |
| **探針模擬自包含** | GenerateSimProbeHal(MachineConfig) 動態解析位置訊號名稱 + loadrt/addf/wiring 全部自包含 |
| **模擬 probe-in 衝突修正** | GenerateHal 模擬模式 skip `net probe-in` 整條接線，避免 OUT pin 衝突 |
| **後端 threaded** | `app.run(threaded=True)` 探測不再阻塞 status 輪詢 |
| **Probe timeout 延長** | 前端 RunProbeAsync HTTP timeout 30s→120s |
| **Probe 錯誤傳播** | 後端 `_probe_send_mdi_and_wait` 回傳 LinuxCNC 錯誤；`_probe_edge` 傳播真實錯誤到前端 |
| **Probe DEBUG log** | 前端 ExecuteProbe 記錄探測參數+結果到 HISTORY DEBUG |

### 2026-03-04

| 項目 | 說明 |
|------|------|
| **Probing 探測循環（Outside Corners）** | ProbingViewModel + ProbingView：9 宮格按鈕（edge/outside_corner/center）+ WPF 向量繪圖（紫球+綠十字+箭頭+灰方塊）+ 左面板 7 參數 + 結果 + MDI |
| **ProbeResult / ProbeParameters 模型** | MachineModels.cs 新增探測結果（Tripped/X/Y/Z/Error）+ 探測參數（TraverseSpeed~ExtraDepth） |
| **MachineControlService.RunProbeAsync** | 獨立 30s timeout HttpClient，POST /v2/probe/run |
| **後端 /v2/probe/run 端點** | _probe_edge（單軸邊緣）+ _probe_outside_corner（雙軸外角）+ _probe_center（4 邊中心）+ G38.2 探測 |
| **ProbingView 佈局** | 4 列 4 欄：子頁籤（8 個，僅 OUTSIDE CORNERS 啟用）+ WCS 選擇（G54~G57）+ Porbing_BACK.png 底圖 + 垂直 Tab（TOUCH PROBE/TOOL SETTER） |
| **HAL probe-input** | ConfigurationService：probe-in 訊號自動追加 motion.probe-input 接線 |
| **Probing Inside Corners** | ProbingViewModel 新增 9 個 InsideCorner RelayCommand + ProbingView INSIDE CORNERS 分頁啟用 + 9 宮格 WPF 向量繪圖（牆壁+口袋+探針內部）+ DataTrigger 切換 Outside/Inside |
| **後端 Inside Corner 探測** | server.py 新增 `_probe_inside_corner()`（方向反轉：NW→X-,Y+）+ inside_edge 路由映射（N→S, S→N, E→W, W→E）|
| **Probing Boss & Pocket** | ProbingViewModel 新增 6 個 RelayCommand（BossX/BossY/BossXY + PocketX/PocketY/PocketXY）+ ProbingView BOSS AND POCKET 分頁啟用 + 3×2 WPF 向量繪圖 + Hint 結果面板 |
| **後端 Boss/Pocket 探測** | server.py `_probe_boss(axes)` 支援 X/Y/XY 軸選擇 + `_probe_center(axes)` 同步支援 + 6 種 route（boss_x/y/xy + pocket_x/y/xy） |
| **Probing Ridge & Valley** | 6 個 RelayCommand（RidgeX/Y/XY + ValleyX/Y/XY）+ 3×2 向量繪圖 + DIST hint；後端 ridge_x/y/xy 複用 _probe_boss，valley 複用 _probe_center |
| **Probing Edge Angle** | 6 個 RelayCommand（AngleX+/X-/Y+/Y-/XY-F/XY-B）+ `_probe_edge_angle()`（沿邊 2 點 atan2 計算角度）+ SET ROTATION WCO（G10 L2 R） + EDGE WIDTH hint |
| **Probing Calibrate** | CalOnXyTurret/CalXEdge/CalXBore + ProbeCalReset + `_probe_calibrate()` + 校正欄位（OffsetX/Y/Diameter/CalibrationWidth）+ 校正環視覺化 |
| **ProbeResult 擴展** | 新增 Angle / EdgeWidth 欄位（後端 → 前端） |
| **Block Delete / M01** | MainViewModel ToggleBlockDelete/ToggleOptionalStop + MachineControlService SetBlockDeleteAsync/SetOptionalStopAsync + CycleControl 按鈕 DataTrigger 藍色高亮 |
| **後端 Block Delete** | /v2/status 新增 Block_Delete/Optional_Stop/Current_Line + POST /v2/program/block_delete + /v2/program/optional_stop |
| **G-Code 行號高亮** | MonitorViewModel GCodeLines（ObservableCollection<GCodeLineItem>）+ MachineStatus.CurrentLine 訂閱 + MonitorView ItemsControl 逐行（行號+黃底高亮+VirtualizingStackPanel） |
| **ATC PROGRAM TOOLS** | GCodeParser.ExtractToolNumbers（Regex T\d+ 去重排序）+ AtcViewModel LoadProgramTools + Navigate "Atc" 自動載入 + LOAD TOOLS 按鈕 |
| **版本號** | `2026.03.04_BLOCKDEL_HIGHLIGHT_ATC` |

### 2026-03-09

| 項目 | 說明 |
|------|------|
| **Phase 2 ATC INI/HAL/NGC 生成** | ConfigurationService 新增 INI `[ATC]` 區段（POCKETS/Z 高度/Rack 參數）、`[RS274NGC]` REMAP（M6/M10~M26）、HAL ATC IO 接線（6 DO + 5 DI） |
| **NGC 巨集生成** | `GenerateAtcNgc()` 依 AtcType 動態生成 toolchange.ngc / m21.ngc / m22.ngc / m13.ngc（Rack/Carousel 分流） |
| **ATC IO 衝突檢查** | HAL 生成時比對 ATC pin 與 GENERAL IO 同 Slave 有功能的 pin，衝突 → 警告+跳過；`IsRealFunction()` 排除 "Pin N" 預設佔位名 |
| **ATC IO 預設值倒數** | DO: 31~26、DI: 31~27（從 31 倒數），避免與 coolant/spindle(0~5) 衝突 |
| **NUM_DIO=32** | INI `[EMCMOT]` + HAL `loadrt motmod num_dio=32`，建立 32 個 digital IO pin 供 M64/M65/M66 使用 |
| **GENERAL IO skip ATC pin** | 同 Slave 時 GENERAL IO 跳過 ATC 佔用的 DO/DI pin，避免 HAL pin 重複 link |
| **server.py NGC 部署** | `/api/config/update` 新增 NgcFiles 處理，寫入 `macros_metric_sim/` 目錄 |
| **全域字體統一重構** | Theme.Dark.xaml 統一變數系統（FontFamily.Default/Mono + FontSize 7 級 + Brush）；26 個 XAML 頁面全面替換 hardcoded 值為 DynamicResource |
| **ComboBox 顯示站號** | ATC AXIS/SPINDLE/ATC IO 設定頁 ComboBox 改用 `DisplayName`（`#站號: 名稱 (VendorId)`） |
| **CarouselControl / SpindleToolControl** | 新增 code-behind（轉盤視覺化 + 主軸刀具顯示） |
| **版本號** | `2026.03.09_ATC_PHASE2_FONT` |

### 2026-03-06

| 項目 | 說明 |
|------|------|
| **探針已觸發修正** | 後端 `_probe_edge()` 執行 G38.2 前先 `cnc_stat.poll()` + 檢查 `probe_val`，避免 "Probe is already tripped" 錯誤 |
| **Probe Input 即時狀態** | 後端 `/v2/status` 新增 `Probe_Input`（讀取 `cnc_stat.probe_val`）；前端 `MachineStatus.IsProbeInput` + 狀態輪詢映射 |
| **探針模擬改手動觸發** | 移除自動位置觸發（near/or2），改為手動按鈕 `sets probe-in 0/1`；`GenerateSimProbeHal()` 簡化為 `net probe-in motion.probe-input` + `sets probe-in 0` |
| **SIM TRIGGER 按鈕** | ProbingView 新增 PROBE INPUT LED 指示燈（綠/灰）+ SIM TRIGGER/SIM RELEASE 按鈕；ProbingViewModel `ToggleProbeInput()` 透過 `/v2/hal/setp` 切換 |
| **後端非阻塞探測** | `_probe_send_mdi_and_wait()` 從 `wait_complete()` 改為非阻塞輪詢（sleep 0.1s + poll interp_state），解決 Flask 單線程阻塞造成斷線 |
| **WCS 寫入移至後端** | 探測成功後 G10 L20 直接在 `v2_probe_run()` 內執行，解決前端分離 HTTP 請求造成 "MDI running" 時序錯誤 |
| **探針參數持久化** | `SaveProbeSettings()` / `LoadProbeSettings()` 儲存至 `probe_settings.json`（TOUCH PROBE + TOOL SETTER 全部參數） |
| **探測頁面全繁中翻譯** | TOUCH PROBE + TOOL SETTER 所有標籤翻譯為繁體中文 + 單位標註（mm/min、mm）；含結果欄位、子分頁名稱、按鈕文字 |
| **字體加大** | ParamLbl/ResultLbl 14→16、ParamTxt 高度 30→34、狀態文字 11→15+Bold、TOOL SETTER 按鈕 12→14 |
| **G38.2 timeout 延長** | 後端探測超時 30s→60s（手動模擬需更多時間） |
| **版本號** | `2026.03.06_PROBE_MANUAL_SIM` |
