# CLAUDE.md

本檔案提供 Claude Code (claude.ai/code) 在此專案中的開發指引。

## 角色定義
你是 LinuxCNC + EtherCAT 工控專家，熟悉 CiA 402 伺服驅動器協議、EtherCAT 拓樸設定、HAL 組件配置、G-code 程式設計。

## 程式碼修改規範
）每次修改程式碼時（包含前端和後端，必須在修改處加上註解，格式為：
// [YYYY-MM-DD] 說明修改內容

## 專案終極目標（Ultimate Goal）

### 第一部分：機台操作介面（對齊 Probe Basic Mill）

| 目標功能 | 說明 |
|---------|------|
| DRO | X/Y/Z/A/B/C 六軸顯示；Work / Machine 座標切換；DTG（Distance To Go）即時綁定後端 |
| JOG | X/Y/Z/A/B/C 六軸手動移動；連續 + 寸動模式完整切換 |
| 主軸控制（Spindle） | RPM 顯示與 Override 滑桿（M3/M4/M5）；速度百分比覆蓋 |
| 進給率覆蓋（Feed Override） | Feed Rate 百分比滑桿，正確連動後端 |
| Cycle Start / Stop / Feed Hold | 完整加工循環控制，狀態連動 UI |
| Single Block（單節執行） | 每次 Cycle Start 僅執行一行 G-Code |
| MDI 送出 | SEND 按鈕送出指令至 LinuxCNC；保留指令歷史紀錄 |
| G54–G59 Offsets（工件座標系） | 工件補償管理介面；可切換與設定六組座標系 |
| Tool Table（刀具表） | 完整刀具資料庫管理；與 LinuxCNC 刀具表同步 |
| Probing 探測循環 | Outside Corners、Inside Corners、Boss/Pocket、Ridge/Valley、Edge Angle、Calibrate 等 |
| 警報明細（Alarm Detail） | 警報明細列表；可清除單筆；支援明細展開 |

### 第二部分：機台設定介面（輸出 LinuxCNC 設定檔）

| 目標功能 | 說明 |
|---------|------|
| 軸設定（Axis Parameters） | 軸數選擇（X/Y/Z/A/B/C）；螺距、脈衝數、最大速度、最大加速度、軟體極限；原點復歸模式（Immediate / HomeSwitch / LimitSwitch）；DI Index 指派 |
| EtherCAT 硬體掃描 | 自動偵測匯流排上的 Slave；顯示 Index、VendorId、ProductCode、Name、Category |
| 軸指派（Axis Mapping） | 將掃描到的 EtherCAT Slave 指派至各 CNC 軸；依 VID/PID/Index 驗證拓樸一致性 |
| IO 邏輯設定（IN/OUT MAP） | 輸入模組 Pin 功能映射（NC 反向）；輸出模組 Pin 功能映射（Active Low）；標準訊號下拉選單 |
| 輸出設定檔 | 生成 LinuxCNC 所需的 INI（主設定）、HAL（硬體抽象層）、XML（EtherCAT 拓樸）、PostGUI HAL |
| 上傳後端並重啟 | 將生成的設定檔透過 `/api/config/update` 上傳後端；呼叫 `/api/machine/restart` 重啟；輪詢確認 LinuxCNC 啟動成功 |

---

## 語言規則

回答一律使用繁體中文。

##開發工作流程規則

每完成一個功能修改後（包含前端和後端），自動執行 /compact 壓縮對話，更新 CLAUDE.md 的功能實作狀態表標記為已完成，然後 commit and push

## Build（建置）& Run（執行）

```bash
# Build（建置）
dotnet build CncController/CncController.csproj

# Run（執行）
dotnet run --project CncController/CncController.csproj
```

Target framework（目標框架）：`net10.0-windows`，需要 .NET 10 SDK 與 Windows（WPF）。

本專案目前沒有自動化測試。

## Architecture（架構）

這是一個 WPF（Windows Presentation Foundation，視窗桌面應用）程式，作為 LinuxCNC（開源 CNC 控制系統）後端的 GUI（圖形操作介面）前端。後端運行於 `http://192.168.0.137:5000`（硬寫於 `MachineControlService`）。

**MVVM（Model-View-ViewModel，模型-視圖-視圖模型）架構，使用 CommunityToolkit.Mvvm：**
- ViewModel（視圖模型）繼承 `ObservableObject`，以 `[ObservableProperty]` / `[RelayCommand]` Source Generator（原始碼產生器）自動建立屬性與命令。
- `MainViewModel` 為根 ViewModel，持有子 ViewModel（`SettingsViewModel`、`MonitorViewModel`、`HistoryViewModel`），管理頁面導航，並驅動 `DispatcherTimer`（排程計時器）進行約 500ms 一次的狀態輪詢（Polling）。
- View（視圖）透過 `DataContext`（資料內容）綁定 ViewModel；Code-behind（程式碼後置）盡量保持最小化。

**Service（服務層），以手動 Singleton（單例）模式透過 `Instance` 屬性存取，非 DI Container（相依注入容器）：**
- `MachineControlService` — HTTP Client（超文字傳輸協定客戶端），負責所有機台命令與狀態輪詢（`GET /v2/status`、`POST /api/...`）。將最後一次狀態快取於 `_lastCachedStatus`，供本地端防呆判斷使用。
- `ConfigurationService` — 載入/儲存本地端 `MachineConfig.json`，並產生上傳至後端的 LinuxCNC 設定檔（INI、HAL、XML）。
- `HardwareScanService` — EtherCAT（即時乙太網路工業通訊協定）Slave（從站）掃描；具備離線開發用的模擬模式（Simulation Fallback）。
- `AlarmService` — 集中式日誌系統，依嚴重程度分級；驅動 UI 上的跑馬燈（Marquee）警報顯示。
- `AuthService` — 角色權限控制（Role-based Access Control），角色：`Operator`（操作員）、`Engineer`（工程師）、`Admin`（管理員）、`Developer`（開發者）；管控設定頁面的存取。
- `LocalizationService` — 多語言支援（繁體中文 zh-TW），基於 Resource Dictionary（資源字典）。

**核心資料流（Key Data Flow）：**
```
DispatcherTimer（排程計時器）→ MachineControlService.GetStatusAsync()
    → 更新 MainViewModel.Status（MachineStatus : ObservableObject）
    → UI Binding（資料綁定）自動刷新
```

**設定工作流程（Settings Workflow）：** 硬體掃描 → 指派 Slave（從站）至各軸（`AxisMappingViewModel`）→ 設定 I/O（輸出入）→ 儲存 → `ConfigurationService` 產生 LinuxCNC 設定檔 → 上傳 → 後端重啟。

## 專案特定規則（Project-Specific Rules）

- `DiscoveredSlave`（已掃描從站）的 `Index`（索引）、`VendorId`（廠商 ID）、`ProductCode`（產品碼）一律顯示原始整數值，**禁止轉換為十六進位（Hex）格式**。UI 直接呈現數值，不加 `0x...` 前綴。

## 重要檔案（Key Files）

| 檔案 | 說明 |
|------|------|
| `Services/MachineControlService.cs` | 所有 HTTP 通訊；後端 URL（網址）在此設定 |
| `Services/ConfigurationService.cs` | 機台設定載入/儲存；生成 INI、HAL、XML、PostGUI HAL 並上傳後端重啟 |
| `Services/AppSettings.cs` | 應用程式設定 Singleton；讀取/建立 `appsettings.json`（`ServerUrl` 等）；三個 Service 統一從此取得後端 URL |
| `Services/AlarmService.cs` | 集中式日誌；`AllLogs`（歷史 ≤500 筆）、`ActiveAlarms`（跑馬燈用）；Error/Warning/Info 同步寫入 `logs/cnc-yyyy-MM-dd.log` |
| `ViewModels/MainViewModel.cs` | 根 ViewModel；輪詢計時器、頁面導航、電源/急停命令、硬體自動驗證 |
| `ViewModels/SettingsViewModel.cs` | 設定頁面協調；聚合 6 個子 ViewModel；`GenerateAndDeploy` 產生設定檔 |
| `ViewModels/MonitorViewModel.cs` | G-Code 載入上傳預覽；`LoadLocalFileCommand`、`LoadFromCam()` |
| `ViewModels/HistoryViewModel.cs` | 操作歷史；`LogsView`（ICollectionView）+ `SetFilterCommand` 五級過濾 |
| `ViewModels/HardwareDiscoveryViewModel.cs` | EtherCAT 掃描；填充 `Slaves` 集合 |
| `ViewModels/AxisMappingViewModel.cs` | 軸與 EtherCAT Slave 對應；`LoadMapping`、`GenerateAxisTable` |
| `ViewModels/AxisParameterViewModel.cs` | 軸機械/運動/原點復歸參數；`Axes` 集合 |
| `ViewModels/IoMonitorViewModel.cs` | 即時 IO 監控；解析 `Servo_IO`，CiA 402 狀態機判斷 |
| `Models/MachineModels.cs` | `MachineConfig`、`AxisSetting`、`HardwareMapping`、`DiscoveredSlave`、`IoMapItem`、`PinConfig` |
| `Models/MachineStatus.cs` | 綁定至主 UI 的機台即時狀態（Observable，可觀察） |
| `VersionConfig.cs` | 版本字串；每次里程碑標記時更新 |
| `Resources/Languages/Lang.zh-TW.xaml` | 所有 UI 文字（繁體中文） |
| `Resources/Themes/Theme.Dark.xaml` | 深色主題色彩與 Brush（筆刷）定義 |

## NuGet 套件（NuGet Packages）

- `CommunityToolkit.Mvvm` 8.4.0 — MVVM 基底類別與 Source Generator（原始碼產生器）
- `HelixToolkit.Wpf` 3.1.2 — 3D 視覺化（可視化）
- `Microsoft.Xaml.Behaviors.Wpf` 1.1.135 — XAML Behavior（行為）與 Trigger（觸發器）支援

## 後端 Server（伺服器）

原始碼位於 `Server/`，運行於 LinuxCNC 機台（Ubuntu RT）。

**技術棧（Tech Stack）：** Python 3 + Flask（輕量 Web 框架）+ LinuxCNC NML（中立訊息語言，進程間通訊協定）
**預設位址：** `http://192.168.0.137:5000`（前端硬寫於 `MachineControlService.cs`）

### 檔案結構

| 檔案 | 說明 |
|------|------|
| `server.py` | 主伺服器（Flask REST API + LinuxCNC NML 整合） |
| `guardian.py` | 進程守護（Process Guardian）；Exit Code（結束碼）42 = API 重啟請求，其他非 0 = 崩潰自動重啟 |
| `smart_scan.py` | EtherCAT 掃描工具；解析 `ethercat slaves -v` 輸出，產出 `frontend_topology.json` |

### 啟動方式

```bash
python3 guardian.py   # 帶守護（正式環境）
python3 server.py     # 直接啟動（除錯用）
```

### API 端點（API Endpoints）

| 方法 | 路由（Route） | 功能 | 主要參數 |
|------|------|------|---------|
| GET | `/v2/status` | 機台即時狀態 | — |
| GET | `/v2/errors` | 錯誤快取（最多 20 筆） | — |
| POST | `/v2/motion/jog` | JOG（手動移動） | `axis`（軸，0–5）, `speed`（速度，mm/min）, `dist`（距離，mm；0=連續） |
| POST | `/v2/program/run` | 執行 G-Code | `line`（起始行號）, `file_name`（檔名） |
| POST | `/v2/program/pause` | 暫停 | — |
| POST | `/v2/program/resume` | 繼續 | — |
| POST | `/v2/program/stop` | 停止 | — |
| POST | `/v2/machine/reset` | 狀態重置（ESTOP 急停解除 / ON-OFF 切換） | — |
| POST | `/v2/machine/estop` | 緊急停止（Emergency Stop） | — |
| POST | `/api/machine/restart` | 完全重啟 LinuxCNC | — |
| GET/POST | `/api/ethercat/scan` | 掃描 EtherCAT 拓撲（Topology） | — |
| POST | `/api/config/update` | 更新 INI / HAL / XML 設定檔 | `inicontent`, `halcontent`, `xmlcontent`, `postguicontent` |
| POST | `/api/files/upload` | 上傳 G-Code 檔案 | `name`（檔名）, `content`（內容） |

### `/v2/status` 回傳格式

```json
{
  "status": "Success",
  "version": "...",
  "data": {
    "Connected": true,
    "Task_State": "ON|OFF|ESTOP",
    "Interp_State": "IDLE|RUNNING|PAUSED",
    "Position": { "X": 0.0, "Y": 0.0, "Z": 0.0, "A": 0.0, "B": 0.0, "C": 0.0 },
    "Feedrate": 0.0,
    "Spindle_Speed": 0.0,
    "File": "current_program.ngc",
    "Servo_IO": {
      "0": { "DI": "0x1234", "Status": "0x5678" }
    }
  }
}
```

### 重要路徑（LinuxCNC 機台上）

| 項目 | 路徑 |
|------|------|
| LinuxCNC INI（主設定檔） | `~/linuxcnc/configs/SGCAM_PB/3axis.ini` |
| G-Code 檔案目錄 | `~/linuxcnc/nc_files/` |
| EtherCAT 設定（HAL 用） | `./ethercat-conf.xml` |
| 前端拓撲輸出 | `./frontend_topology.json` |
| ESI XML 定義（從站描述檔） | `./esi_snippets/` |

---

## 功能實作狀態（對照 Probe Basic Mill）

參考規格：[Probe Basic Mill Interface](https://kcjengr.github.io/probe_basic/mill_interface.html) / [Probing（探測）](https://kcjengr.github.io/probe_basic/probing.html)

### ✅ 已完成

| 功能 | 實作位置 |
|------|---------|
| G-Code 載入、預覽、上傳 | `MonitorView` + `MonitorViewModel.LoadLocalFileCommand` / `LoadFromCam()` |
| 加工循環控制（Cycle Start / Stop / Feed Hold，進給保持） | `CycleControl.xaml` + `MainViewModel.CycleStartCommand` / `FeedHoldCommand` / `StopCommand` |
| 電源 / 急停（Power / E-Stop） | `TogglePowerCommand` / `ToggleEstopCommand`；急停未解除時鎖定電源操作 |
| DRO 顯示（X/Y/Z/A/B/C + DTG） | `DroDisplay.xaml`；六軸座標 + DTG 均綁定後端 `Status.X/Y/Z/DtgX/DtgY/DtgZ`；G54–G59 快選列 |
| 手動 JOG（X/Y/Z 三軸，連續 + 寸動） | `JogPanel.xaml` + `JogStartCommand` / `JogStopCommand`；防呆：MouseDown 才發送 JogStop |
| 冷卻液 Flood（M8/M9）| `CycleControl` + `ToggleFloodCommand`；實際送出 `SendMdiCommandAsync("M8"/"M9")`；`IsFloodOn` DataTrigger 變藍 |
| MIST 霧化冷卻（M7/M9） | `CycleControl` + `ToggleMistCommand`；送 M7 開 / M9 關；M9 時同步清除 `IsFloodOn`（2026-02-23） |
| 系統狀態顯示與警報輪播（Marquee） | `HeaderBar.xaml`；三層優先級（警報 > 連線狀態 > 機台邏輯） |
| 使用者登入 / 登出 / 角色權限（4 種角色） | `AuthService` + `LoginWindow`；Admin/Developer 可編輯設定 |
| 操作歷史（History） | `HistoryView.xaml` + `HistoryViewModel`；五級過濾（ALL/INFO/WARN/ERROR/DEBUG）；Clear Active Alarms 清除跑馬燈 |
| 機台設定介面（6 個 Tab） | `SettingsView.xaml` + `SettingsViewModel` + 子 ViewModel |
| ↳ Tab 1 HARDWARE SCAN | `HardwareDiscoveryViewModel`；EtherCAT 掃描，自動在開機時執行並廣播 |
| ↳ Tab 2 AXIS MAPPING | `AxisMappingViewModel`；將 Slave 指派至各軸；依 VID/PID/Index 還原 |
| ↳ Tab 3 AXIS PARAMETERS | `AxisParameterViewModel`；螺距、脈衝數、速度、加速度、軟體極限、原點復歸（3 種模式）、IO DI Index |
| ↳ Tab 4 IO MONITOR | `IoMonitorViewModel`；即時 DI 燈號 + CiA 402 伺服狀態解析 |
| ↳ Tab 5 IN MAP | `IoMapItem` + `PinSettings`；輸入模組 Pin 功能映射（NC 反向） |
| ↳ Tab 6 OUT MAP | `IoMapItem` + `PinSettings`；輸出模組 Pin 功能映射（Active Low） |
| 設定檔生成與部署 | `ConfigurationService.SaveConfigAsync`：生成 INI / HAL / XML / PostGUI HAL → 上傳後端 → 重啟 → 輪詢確認啟動 |
| MDI（Manual Data Input，手動資料輸入）送出 | `MonitorView`（TextBox→可編輯 ComboBox，含歷史下拉）+ `MonitorViewModel.SendMdiCommand`；ViewModel 層 `ValidateAction` + Service 層雙重守衛；`MdiHistory` 最近 20 筆；DataContext 根本 Bug 已修正（移除孤兒 VM） |
| 加工計時器（Cycle Timer） | `MainViewModel`；RUNNING 時計時，IDLE 時停止 |
| 開機硬體自動驗證 | `MainViewModel.AutoValidateHardware`；讀設定 → 掃描 → 驗證拓樸 → 通知 SettingsVM |
| **[工控安全] 異常處理強化** | 消除所有空 `catch{}`；`AlarmService` 加入 `lock` + `try/catch`；Fire-and-Forget 均包裹 `try/catch` |
| **[工控安全] 急停優先通道** | `_estopClient`（獨立 2s HttpClient）；觸發後立即樂觀更新 `IsEstop=true`；取消進行中 JOG |
| **[工控安全] 運動指令前置守衛** | `CanExecuteMotion()`（ViewModel 層）+ `ValidateAction()`（Service 層）雙重防線；套用至 `JogStart`/`CycleStart` |
| **[工控安全] 原子狀態更新** | `PollMachineStatus` 先計算 `newIsPower/newIsEstop/newIsReady` 快照再統一套用，杜絕 UI 讀到中間態 |
| **[工控安全] HttpClient 分離** | `_pollingClient`（3s）/ `_estopClient`（2s）/ 局部 client（設定10s / 上傳20s）各自獨立 |
| **[工控安全] 連續失敗計數** | `_consecutiveFailCount`：連續 ≥3 次失敗才判定 Disconnected，避免短暫波動誤報 |
| **[工控安全] 狀態轉換驗證表** | `MachineAction` enum + `ValidateAction()`；`JogAsync`/`CycleStartAsync` 呼叫作為 Service 層防線 |
| **[工控安全] 密碼外部化** | SHA-256（Salt:Password）雜湊，儲存於外部 `passwords.json`；原始碼無明文密碼 |
| **[工控安全] IP 外部化** | `AppSettings.cs` 讀取 `appsettings.json`（`ServerUrl`）；三個 Service 統一使用，首次執行自動建立 |
| **[工控安全] 日誌持久化** | Error/Warning/Info 透過 ThreadPool 非同步寫入 `logs/cnc-yyyy-MM-dd.log`（每日滾動） |
| **[工控安全] 部署集合快照** | `GenerateAndDeploy` 迭代前對 `AxisMaps`/`InMaps`/`OutMaps` 呼叫 `.ToList()` 取快照 |
| **Offsets Tab（G54–G59 工件座標系）** | `OffsetsView.xaml` + `OffsetsViewModel`；兩欄版面（左 DataGrid 只顯示 XYZ + 詳細面板）；`SelectOffsetCommand` 送 MDI；`ReloadTableCommand` 從 `/v2/offsets` 讀取；建構子自動 `AutoLoad`；後端 `Active_WCS` 同步；`MachineStatus.ActiveCoordSystem` 跟蹤（2026-02-23） |
| **DRO G54–G59 快選列** | `DroDisplay.xaml` Row 4；六個按鈕綁定 `OffsetsVM.SelectOffsetCommand`；DataTrigger 高亮 Active 者（2026-02-23） |
| **GO TO HOME（原點復歸）** | `CycleControl.xaml` 原 CLEAR PGM 改為 GO TO HOME；`HomeAllCommand` → `HomeAsync(-1)`；後端 `POST /v2/machine/home` 呼叫 `cnc_cmd.home(-1)`（2026-02-23） |
| **HeaderBar EXIT 選單** | File 選單加子項「EXIT（關閉程式）」；`ExitAppCommand` → `Application.Current.Shutdown()`（2026-02-23） |
| **後端 /v2/offsets 端點** | `server.py` 新增 `read_work_offsets()`（讀 .var 參數檔）+ `GET /v2/offsets`；`/v2/status` 加 `Active_WCS` 欄位（2026-02-23） |
| **後端 /v2/machine/home 端點** | `server.py` 新增 `POST /v2/machine/home`；使用 `cnc_cmd.home(axis)` 而非 G28 MDI；支援 -1=全軸 / 0~5=單軸（2026-02-23） |

### ❌ 尚未實作

| 功能 | 說明 |
|------|------|
| Probing（探測循環） | Outside Corners、Inside Corners、Boss/Pocket、Ridge/Valley、Edge Angle、Rotary Axis、Calibrate 全部未實作 |
| ATC（Auto Tool Changer，自動刀庫） | 無換刀介面、刀庫狀態顯示 |
| Tool Table（刀具表） | `ToolInfo` 僅靜態顯示，無完整刀具資料庫管理 |
| Conversational（對話式加工） | 無 Facing、Holes、Pattern 等簡易程式產生器 |
| Single Block / Block Delete / M01 | `CycleControl` 有按鈕但無 Command 綁定，無後端對應 |
| Spindle Override（主軸轉速覆蓋控制） | `SliderControl` 有 UI 但值 hardcode 100%，無後端綁定 |
| Feed Override 後端連動 | `SliderControl` 有 UI 但值 hardcode 120%，無後端綁定 |
| Offsets SET TO ZERO / CLEAR / SAVE | `OffsetsView` 底部按鈕為 TODO stub，尚未實作 G10 L20 指令送出 |

### ⚠️ 已實作但需加強

| 功能 | 現況 | 待改善 |
|------|------|--------|
| Offsets 右欄座標值 | MC Current / WC / G53/G52 欄全部 hardcode "0.000" | 需從後端取得 WCS 位置值再綁定 |
| 刀具資訊（Tool Info） | 靜態顯示刀號，尺寸可編輯 | 未與 LinuxCNC 刀具表同步，儲存邏輯缺失 |
| 3D 視圖 | HelixToolkit 框架已載入（座標系 + 網格 + 刀具圓錐） | 無刀具路徑模擬，無即時刀具位置顯示 |
| JOG | X/Y/Z 三軸，防呆完整 | 缺 A/B/C 旋轉軸；連續/步進切換 UI 不完整 |
| 警報系統 | 跑馬燈輪播；History 頁可過濾 | 無警報明細列表；無清除單筆功能 |
| G-Code 預覽 | 純文字顯示 | 無行號高亮，無執行中行追蹤 |
| HAL 生成—軸 Index 映射 | X→0、Y→2、Z→3 寫死於 `GenerateHal` | 未依 `AxisMappingViewModel` 的實際選擇動態生成 |
| MachineConfigViewModel | 骨架存在，僅有 6 個 bool 屬性（EnableX~C） | 未連接到任何 View，無實際功能 |

### 建議開發優先順序

1. **Offsets SET TO ZERO / G10 指令** — 完成座標系歸零功能
2. **Offsets 右欄座標值連通** — MC Current / WC 改為綁定後端真實值
3. **Feed Override / Spindle Override 連通** — SliderControl 綁定後端即時數值
4. **Probing 探測循環** — 差異化功能，工程師常用
5. **Tool Table（刀具表）管理** — ATC 前置需求

---

## 每日工作紀錄

### 2026-02-23（今天完成）

| 項目 | 說明 |
|------|------|
| **Offsets Tab 全新頁面** | `OffsetsView.xaml` + `OffsetsViewModel.cs`；兩欄版面（左 DataGrid G54–G59 表格 + 右側詳細面板）；建構子自動 `ReloadTable()` 載入後端 offset 值 |
| **DRO G54–G59 快選列** | `DroDisplay.xaml` 新增 Row 4，六個按鈕送 MDI 切換座標系，DataTrigger 高亮 Active |
| **MIST 霧化冷卻** | `ToggleMistCommand`（M7/M9）；`IsMistOn` 狀態 DataTrigger 變藍；M9 時同步清除 `IsFloodOn` |
| **GO TO HOME 原點復歸** | 後端 `POST /v2/machine/home` + `cnc_cmd.home(-1)`；前端 `HomeAsync(-1)` 取代錯誤的 G28 MDI |
| **HeaderBar EXIT 選單** | File 選單 → EXIT（關閉程式）→ `Application.Current.Shutdown()` |
| **後端 /v2/offsets** | `read_work_offsets()` 從 .var 讀取 G54–G59；`GET /v2/offsets` 回傳 Active + Offsets 結構 |
| **後端 Active_WCS** | `/v2/status` 新增 `Active_WCS` 欄位，前端自動同步至 `MachineStatus.ActiveCoordSystem` |
| **CLAUDE.md 狀態修正** | 掃描全專案：Flood 已連通（非 Comment Out）；DTG 已綁定後端（非 hardcode）；移除錯誤標記 |
| **VersionConfig 更新** | 版本號 `2026.02.23_OFFSETS_MIST_HOME` |
| **程式碼註解規範** | 所有修改處補齊 `// [2026-02-23] 說明修改內容` 格式 |

### 2026-02-24（預計明天）

| 優先順序 | 項目 | 說明 |
|---------|------|------|
| 1 | Offsets SET TO ZERO | 實作 `G10 L20 P? X0 Y0 Z0` 指令送出；完成 SET TO ZERO X/Y/Z 與 ZERO ALL 按鈕功能 |
| 2 | Offsets 右欄連通 | MC Current / WC / G53/G52 三欄改為綁定後端真實值（需從 `/v2/status` 或 `/v2/offsets` 擴充資料） |
| 3 | Feed Override 連通 | `SliderControl` 綁定後端即時 Feedrate Override 百分比，加入滑桿互動 |
| 4 | Spindle Override 連通 | `SliderControl` 綁定後端即時 Spindle Override 百分比 |
| 5 | Single Block 實作 | 連接按鈕至後端 MDI 控制（需評估 LinuxCNC 的 single-block 模式切換 API） |

---

## 工控安全重構紀錄（Safety Refactoring — 已全部完成）

基於工業控制機安全規範審計，共發現 **嚴重 4 項、高危 10 項、中危 15 項**，已分三批完成修復。

### 完成記錄

| Commit | 批次 | 項目 |
|--------|------|------|
| `653108d` | 第一優先（嚴重） | 消除空 catch、急停獨立通道、SaveConfig 錯誤可見、密碼外部化 |
| `e6f31fd` | 第一優先補 | 補修 IoMonitorViewModel 殘留空 catch |
| `6a52e96` | 第二優先（高危） | HttpClient 分離、原子狀態更新、運動前置檢查、AlarmService lock、Timer try/catch、連續失敗計數 |
| `6e3ac2b` | 第三優先（中危） | IP 外部化、日誌持久化、集合快照、狀態轉換驗證表 |

### 1. 異常處理（Exception Handling）✅

| 等級 | 問題 | 狀態 | 實作位置 |
|------|------|------|---------|
| 🔴 嚴重 | 空 `catch { }` 吞沒所有異常 | ✅ 已修復 | 所有 catch 改為記錄 `ex.GetType().Name + ex.Message` 至 `AlarmService` |
| 🔴 嚴重 | `SaveConfigAsync` 異常使用者不可見 | ✅ 已修復 | 改寫入 `AlarmService.AddLog(LogType.Error)` 並 `throw` 讓呼叫端知道 |
| 🟠 高危 | Fire-and-Forget `AutoValidateHardware` 失敗被忽視 | ✅ 已修復 | 外層 `try/catch`，失敗時設 `IsSystemReady = false` 並推警報 |
| 🟡 中危 | `AlarmService.AddLog` 集合操作可能拋 `InvalidOperationException` | ✅ 已修復 | `Dispatcher.Invoke` 內加 `lock (_logLock)` + `try/catch` |

### 2. 通訊逾時（Communication Timeout）✅

| 等級 | 問題 | 狀態 | 實作位置 |
|------|------|------|---------|
| 🟠 高危 | 全域 `_httpClient.Timeout` 在 `SaveConfigAsync` 被修改，影響輪詢 | ✅ 已修復 | `_pollingClient`（3s）、`_estopClient`（2s）、`configClient`（局部10s）各自獨立 |
| 🟡 中危 | 單次網路波動即標記 Disconnected | ✅ 已修復 | `_consecutiveFailCount`：連續 3 次失敗才判定 Disconnected |
| 🟡 中危 | 輪詢無防重疊機制，`async void` 異常不被捕捉 | ✅ 已修復 | `StatusTimer_Tick` 以 `_timer.Stop/Start` 在 finally 保護，並加入 `try/catch` |

### 3. 急停優先權（E-Stop Priority）✅

| 等級 | 問題 | 狀態 | 實作位置 |
|------|------|------|---------|
| 🔴 嚴重 | 急停後 UI 未立即反映，等下次輪詢才更新 | ✅ 已修復 | `ToggleEstop` 觸發後立即樂觀更新 `IsEstop = true`、`IsSystemReady = false` |
| 🟠 高危 | 急停與普通指令共用 `HttpClient` | ✅ 已修復 | `_estopClient`（2s 超時）獨立通道，同時取消 `_jogCts` |
| 🟡 中危 | JOG 未驗證急停狀態 | ✅ 已修復 | `CanExecuteMotion()` 守衛套用至 `JogStart`、`CycleStart`；Service 層 `ValidateAction()` 為第二防線 |

### 4. 狀態機設計（State Machine Design）✅

| 等級 | 問題 | 狀態 | 實作位置 |
|------|------|------|---------|
| 🟠 高危 | `IsPower`/`IsEstop`/`IsSystemReady` 依序更新，UI 可能讀到中間態 | ✅ 已修復 | `PollMachineStatus` 先計算 `newIsPower/newIsEstop/newIsReady` 快照，再統一套用 |
| 🟠 高危 | 無狀態轉換驗證，`CycleStartAsync` 未檢查機台狀態 | ✅ 已修復 | `MachineAction` enum + `ValidateAction()` 方法（`JogAsync`、`CycleStartAsync` 呼叫） |
| 🟠 高危 | `_lastCachedStatus` 快取作為防呆唯一依據 | ✅ 已改善 | 前端 `CanExecuteMotion()` + Service 層 `ValidateAction()` 雙重保障；後端為最終仲裁 |

### 5. 執行緒安全（Thread Safety）✅

| 等級 | 問題 | 狀態 | 實作位置 |
|------|------|------|---------|
| 🟠 高危 | `AlarmService.AddLog` 高頻輪詢下集合競賽 | ✅ 已修復 | `lock (_logLock)` 保護所有 `AddLog` 內集合操作 |
| 🟠 高危 | `_httpClient.Timeout` 跨執行緒共享被修改 | ✅ 已修復 | 同第二點，各操作使用獨立 `HttpClient` |
| 🟡 中危 | `GenerateAndDeploy` 迭代 ObservableCollection 期間可能修改 | ✅ 已修復 | 迭代前對 `AxisMaps`/`InMaps`/`OutMaps` 呼叫 `.ToList()` 取快照 |
| 🟡 中危 | `async void` Tick Handler 異常不被捕捉 | ✅ 已修復 | `_cycleTimer.Tick` 與 `HardwareValidationCompleted` 均加入 `try/catch` |

### 6. 其他安全問題

| 等級 | 問題 | 狀態 | 備註 |
|------|------|------|------|
| 🔴 嚴重 | 所有 API 呼叫無身份驗證 | ⏳ 待評估 | 需後端同時支援；區域網路環境下優先級次之 |
| 🔴 嚴重 | 密碼硬寫於原始碼 | ✅ 已修復 | SHA-256（Salt:Password）雜湊，儲存於外部 `passwords.json` |
| 🟠 高危 | HTTP 明文傳輸 | ⏳ 待評估 | 區域網路環境威脅模型較低；後續評估是否啟用 HTTPS |
| 🟡 中危 | 伺服器 IP 硬寫於三個 Service | ✅ 已修復 | 統一改用 `AppSettings.Instance.ServerUrl`，讀取 `appsettings.json` |
| 🟡 中危 | 日誌僅存於記憶體，應用關閉後遺失 | ✅ 已修復 | Error/Warning/Info 透過 ThreadPool 非同步寫入 `logs/cnc-yyyy-MM-dd.log` |

### 重構優先順序（全部完成）

**第一優先（嚴重）— ✅ 完成（commit `653108d`, `e6f31fd`）：**
1. ✅ 消除所有空 `catch { }`，改為記錄至 `AlarmService`
2. ✅ 急停指令使用獨立 `HttpClient`（`_estopClient`），不受其他請求阻塞
3. ✅ `SaveConfigAsync` 錯誤提升至使用者可見的警報
4. ✅ 密碼從原始碼移至外部 `passwords.json`（SHA-256 雜湊）

**第二優先（高危）— ✅ 完成（commit `6a52e96`）：**
5. ✅ 為不同操作建立獨立 `HttpClient` 實例（`_pollingClient` / `_estopClient` / 局部 client）
6. ✅ 狀態更新改為原子操作（先計算快照，再統一套用）
7. ✅ 所有運動指令加入 `IsEstop` / `IsPower` 前置檢查（`CanExecuteMotion()`）
8. ✅ `AlarmService` 集合操作加入 `lock (_logLock)` 保護
9. ✅ Fire-and-Forget 非同步呼叫加入 `try/catch`（`_cycleTimer`、`HardwareValidationCompleted`）
10. ✅ 輪詢加入 `_timer.Stop/Start` 防重疊機制與連續失敗計數器（≥3 次才斷線）

**第三優先（中危）— ✅ 完成（commit `6e3ac2b`）：**
11. ✅ 伺服器 IP 抽出至 `appsettings.json`（新增 `AppSettings.cs`）
12. ✅ 日誌持久化至每日滾動檔（`logs/cnc-yyyy-MM-dd.log`）
13. ✅ 斷線判定改為連續失敗計數器（同 Item 10，於第二優先一併完成）
14. ✅ 部署期間對 `AxisMaps`/`InMaps`/`OutMaps` 做 `.ToList()` 快照再迭代
15. ✅ 狀態轉換驗證表（`MachineAction` enum + `ValidateAction()`，`JogAsync`/`CycleStartAsync` 呼叫）
