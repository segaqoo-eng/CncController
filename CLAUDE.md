# CLAUDE.md

本檔案提供 Claude Code (claude.ai/code) 在此專案中的開發指引。

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
| `Services/AlarmService.cs` | 集中式日誌；`AllLogs`（歷史 ≤500 筆）、`ActiveAlarms`（跑馬燈用） |
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
| DRO（Digital Read Out，數位讀數）顯示（X/Y/Z/A/B/C） | `DroDisplay.xaml`；後端 A/B/C 資料已解析並存入 `MachineStatus` |
| 手動 JOG（X/Y/Z 三軸，連續 + 寸動） | `JogPanel.xaml` + `JogStartCommand` / `JogStopCommand`；防呆：MouseDown 才發送 JogStop |
| 冷卻液（Flood）UI 切換 | `CycleControl` + `IsFloodOn` 旗標切換（後端 MDI M8/M9 待連通） |
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
| MDI（Manual Data Input，手動資料輸入）輸入框 UI | `MonitorView`（TextBox + SEND 按鈕已存在） |
| 加工計時器（Cycle Timer） | `MainViewModel`；RUNNING 時計時，IDLE 時停止 |
| 開機硬體自動驗證 | `MainViewModel.AutoValidateHardware`；讀設定 → 掃描 → 驗證拓樸 → 通知 SettingsVM |

### ❌ 尚未實作

| 功能 | 說明 |
|------|------|
| MDI 送出 | SEND 按鈕無 `Command` 綁定；`MachineControlService` 也缺 `SendMdiCommandAsync` |
| 冷卻液後端連通 | `ToggleFlood` 只切換 `IsFloodOn` 旗標，M8/M9 的 HTTP 呼叫已被 Comment Out |
| Offsets（工件補償）管理 | 無 G54–G59 工件座標系切換與設定介面 |
| Probing（探測循環） | Outside Corners、Inside Corners、Boss/Pocket、Ridge/Valley、Edge Angle、Rotary Axis、Calibrate 全部未實作 |
| ATC（Auto Tool Changer，自動刀庫） | 無換刀介面、刀庫狀態顯示 |
| Tool Table（刀具表） | `ToolInfo` 僅靜態顯示，無完整刀具資料庫管理 |
| Conversational（對話式加工） | 無 Facing、Holes、Pattern 等簡易程式產生器 |
| Single Block / Block Delete / M01 | `CycleControl` 有按鈕但未實作 |
| Mist（霧化冷卻） | 按鈕存在但無命令 |
| Clear Program（清除程式） | 按鈕存在但無命令 |
| Spindle Override（主軸轉速覆蓋控制） | 無 RPM 控制介面 |
| Feed Override 後端連動 | `SliderControl` 存在，未確認是否與後端正確連通 |

### ⚠️ 已實作但需加強

| 功能 | 現況 | 待改善 |
|------|------|--------|
| DRO 顯示 | X/Y/Z 輪詢正常；A/B/C 已解析 | DTG（Distance To Go）欄位硬寫 "0.000" 未綁定後端；無工件座標系切換（G54–G59） |
| 刀具資訊（Tool Info） | 靜態顯示刀號，尺寸可編輯 | 未與 LinuxCNC 刀具表同步，儲存邏輯缺失 |
| 3D 視圖 | HelixToolkit 框架已載入（座標系 + 網格 + 刀具圓錐） | 無刀具路徑模擬，無即時刀具位置顯示 |
| MDI（手動資料輸入） | 有輸入框 | SEND 無命令，無指令歷史紀錄 |
| JOG | X/Y/Z 三軸，防呆完整 | 缺 A/B/C 旋轉軸；連續/步進切換 UI 不完整 |
| 警報系統 | 跑馬燈輪播；History 頁可過濾 | 無警報明細列表；無清除單筆功能 |
| G-Code 預覽 | 純文字顯示 | 無行號高亮，無執行中行追蹤 |
| HAL 生成—軸 Index 映射 | X→0、Y→2、Z→3 寫死於 `GenerateHal` | 未依 `AxisMappingViewModel` 的實際選擇動態生成 |

### 建議開發優先順序

1. **MDI 送出** — 後端加 `SendMdiCommandAsync`，SEND 按鈕綁定命令，成本最低
2. **冷卻液後端連通** — 解除 `ToggleFlood` 的 M8/M9 Comment Out
3. **DTG 綁定後端** — 讓 DRO 資訊完整（需後端 `/v2/status` 新增 DTG 欄位）
4. **Offsets / G54–G59 工件座標系** — CNC 基本操作必備
5. **Probing 探測循環** — 差異化功能，工程師常用
6. **Tool Table（刀具表）管理** — ATC 前置需求

---

## 工控安全重構建議（Safety Refactoring）

基於工業控制機安全規範審計，以下為各面向的已知問題與重構方向。共發現 **嚴重 4 項、高危 10 項、中危 15 項**。

### 1. 異常處理（Exception Handling）

| 等級 | 問題 | 位置 | 重構方向 |
|------|------|------|---------|
| 🔴 嚴重 | 空 `catch { }` 吞沒所有異常，JSON 反序列化失敗無法診斷 | `MachineControlService.cs:66-78, 119, 298` | 改為 `catch (Exception ex)` 並寫入 `AlarmService`；至少記錄 `ex.GetType().Name` + `ex.Message` |
| 🔴 嚴重 | `SaveConfigAsync` 異常僅寫 `Debug.WriteLine`，使用者無法看到部署失敗 | `ConfigurationService.cs:41-71` | 將錯誤提升至 `AlarmService.AddLog(LogType.Error, ...)`；回傳 `bool` 或拋出讓呼叫端處理 |
| 🟠 高危 | `_ = AutoValidateHardware()` Fire-and-Forget，啟動時硬體驗證失敗被忽視 | `MainViewModel.cs:160` | 包裹 `try/catch` 並記錄異常；驗證失敗時設定 `IsSystemReady = false` 並推送警報 |
| 🟡 中危 | 異常只記錄 `ex.Message`，缺少堆疊追蹤（Stack Trace） | `MachineControlService.cs:167-189` 等多處 | 在 `LogType.Debug` 級別額外記錄 `ex.ToString()` 以保留完整堆疊 |
| 🟡 中危 | `AlarmService.AddLog` 內部 LINQ 查詢與集合修改間可能拋出 `InvalidOperationException` | `AlarmService.cs:98-106` | 在 `Dispatcher.Invoke` 內加 `try/catch` 保護，避免日誌系統自身崩潰 |

**通用規則：**
- 禁止空 `catch { }`；最低限度需記錄至 `AlarmService`
- Service 層方法應回傳 `(bool Success, string Error)` 元組或使用 Result Pattern
- Fire-and-Forget 的 `async void` / `_ = Task` 必須包裹 `try/catch`

### 2. 通訊逾時（Communication Timeout）

| 等級 | 問題 | 位置 | 重構方向 |
|------|------|------|---------|
| 🟠 高危 | `SaveConfigAndRestartAsync` 直接修改全域 `_httpClient.Timeout`，影響正在進行的輪詢請求 | `MachineControlService.cs:301-325` | 為不同操作建立獨立 `HttpClient` 實例：`_pollingClient`（3s）、`_uploadClient`（20s）、`_configClient`（10s） |
| 🟡 中危 | 輪詢頻率 100ms 但 HTTP 逾時 3s，網路延遲時可堆積 30+ 個並發請求 | `MainViewModel.cs:130` | 加入 `SemaphoreSlim(1,1)` 或 `_isPolling` 旗標防止重疊；考慮輪詢間隔提高至 200–500ms |
| 🟡 中危 | 單次網路波動即標記為 `Disconnected`，無重試機制 | `MachineControlService.cs:59-102` | 實作連續失敗計數器（如 3 次失敗才判定斷線）；加入指數退避（Exponential Backoff） |
| 🟡 中危 | `WaitForLinuxCNC` 超時後無日誌記錄 | `SettingsViewModel.cs:421-435` | 超時時記錄 `LogType.Error`，並區分「完全無回應」與「回應但未就緒」 |

**通用規則：**
- 禁止在執行時修改共用 `HttpClient` 的 `Timeout`；需要不同逾時的操作使用獨立實例
- 輪詢迴圈必須有防重疊機制（`SemaphoreSlim` 或 `bool` 旗標 + `Interlocked`）
- 斷線判定應基於連續失敗次數，非單次失敗

### 3. 急停優先權（E-Stop Priority）

| 等級 | 問題 | 位置 | 重構方向 |
|------|------|------|---------|
| 🔴 嚴重 | 急停依靠軟體邏輯檢查 `IsEstop`，該值來自最多 100ms 前的快取，高速連點可能繞過 | `MainViewModel.cs:493-521` | UI 急停按鈕加 `IsEnabled` 綁定防連點；急停指令發送後立即設定 `IsEstop = true`（樂觀更新），等下次輪詢確認 |
| 🟠 高危 | 急停指令與普通指令（JOG、MDI）使用同一 `SendV2CommandAsync`，無優先級隊列 | `MachineControlService.cs:194-195` | 為急停建立獨立 `HttpClient`（`_estopClient`），不受其他請求阻塞；或實作 `CancellationToken` 取消所有進行中的非急停請求 |
| 🟡 中危 | JOG 指令未驗證急停狀態，急停時仍可發送 JOG | `MainViewModel.cs:522-538` | 在 `JogStart` 開頭加入 `if (IsEstop) return;` 檢查；所有運動指令統一經過 `CanExecuteMotion()` 防呆 |

**通用規則：**
- 急停必須使用獨立通訊通道，不與常規指令共享 `HttpClient`
- 所有運動指令（JOG、CycleStart、MDI）在發送前必須檢查 `IsEstop` 與 `IsPower`
- 急停發送後應立即做樂觀狀態更新（`IsEstop = true`），不等輪詢
- UI 上急停按鈕永遠不被 `Disable`，任何時刻都可觸發

### 4. 狀態機設計（State Machine Design）

| 等級 | 問題 | 位置 | 重構方向 |
|------|------|------|---------|
| 🟠 高危 | 多個狀態屬性（`IsPower`、`IsEstop`、`IsConnected`、`IsSystemReady`）依序更新，非原子操作，UI 可能讀到中間狀態 | `MainViewModel.cs:330-377` | 引入 `MachineStateSnapshot` 不可變物件，一次性計算所有狀態後原子替換；UI 綁定改為讀取 Snapshot 的屬性 |
| 🟠 高危 | 無狀態轉換驗證，`CycleStartAsync` 未檢查機台是否已復歸或是否有錯誤 | `MachineControlService.cs:232-263` | 建立允許的轉換表（如 `OFF→ON` 需先解除 ESTOP），在發送指令前驗證 |
| 🟠 高危 | `_lastCachedStatus` 最多延遲 100ms，JOG 等指令依此快取做防呆判斷 | `MachineControlService.cs:40, 84-86` | 快取判斷僅作為「建議」而非「強制」；關鍵安全判斷（如急停中不可運動）應同時仰賴後端拒絕 + 前端防呆雙重保障 |
| 🟡 中危 | `TaskState`、`InterpState`、`IsMoving` 為獨立屬性，無統一驗證機制 | `MachineStatus.cs` | 考慮加入 `Validate()` 方法在每次更新後檢查狀態一致性；不一致時記錄警報 |

**通用規則：**
- 機台狀態更新應為原子操作：計算完整新狀態 → 一次性替換 → 通知 UI
- 狀態轉換需有明確的允許轉換表（State Transition Table）
- 前端防呆 + 後端拒絕 = 雙重保障；僅靠一端不可靠
- `_lastCachedStatus` 的時效性必須被呼叫端理解，不可當作即時真值

### 5. 執行緒安全（Thread Safety）

| 等級 | 問題 | 位置 | 重構方向 |
|------|------|------|---------|
| 🟠 高危 | `AlarmService.AddLog` 內 LINQ 查詢與集合修改之間存在時間窗口，高頻輪詢下可能競賽 | `AlarmService.cs:76-132` | 使用 `lock` 保護整個 `AddLog` 內的集合操作；或改用 `ConcurrentQueue` + 定期批次 Flush 至 `ObservableCollection` |
| 🟠 高危 | `_httpClient.Timeout` 在 `SaveConfigAndRestartAsync` 中被修改，與輪詢執行緒共享 | `MachineControlService.cs:30-31, 301-325` | （同通訊逾時 #1）使用獨立 `HttpClient` 實例 |
| 🟡 中危 | `GenerateAndDeploy` 迭代 `ObservableCollection`（`AxisMaps`、`InMaps`），若使用者同時在 UI 修改可能拋出異常 | `SettingsViewModel.cs:296-391` | 部署開始時對集合做快照（`ToList()`）再迭代；或部署期間鎖定 UI 輸入 |
| 🟡 中危 | `DispatcherTimer` 內的 `async void` Tick Handler，若 `PollMachineStatus` 超過間隔時間可能重疊 | `MainViewModel.cs:224-266` | 已有 `_timer.Stop/Start` 保護，但 `async void` 的異常不會被捕捉；改為 `try/catch` 包裹全部邏輯 |
| 🟡 中危 | `AuthService.CurrentUser` setter 無鎖定保護 | `AuthService.cs:6-16` | 加入 `lock` 或使用 `volatile`；實務上因 WPF 單執行緒模型風險較低，但仍應防禦性處理 |

**通用規則：**
- `ObservableCollection` 的修改必須在 UI 執行緒（`Dispatcher`）上進行
- 跨執行緒共享的欄位使用 `lock`、`Interlocked`、或 `Concurrent*` 集合
- 長時間操作開始前對 UI 綁定的集合做快照（`ToList()`）
- `async void` 事件處理器必須有 `try/catch` 保護

### 6. 其他安全問題

| 等級 | 問題 | 位置 | 重構方向 |
|------|------|------|---------|
| 🔴 嚴重 | 所有 API 呼叫無身份驗證，任何人連到後端即可操控機台 | 所有 HTTP 呼叫 | 後端加入 API Token / JWT 驗證；前端在 `HttpClient.DefaultRequestHeaders` 附帶 Token |
| 🔴 嚴重 | 密碼硬寫在 `AuthService`（`"1111"`、`"2222"`、`"8888"`、`"dev999"`） | `AuthService.cs:28-51` | 密碼改為 bcrypt/Argon2 雜湊後儲存於外部加密設定檔；禁止原始碼內含明文密碼 |
| 🟠 高危 | HTTP 明文傳輸，無 SSL/TLS 加密 | 所有 `http://` 呼叫 | 評估在區域網路環境下的威脅模型；若需加密則後端啟用 HTTPS |
| 🟡 中危 | 伺服器 IP `192.168.0.137:5000` 硬寫在三個 Service 中 | `MachineControlService.cs:34`、`ConfigurationService.cs:26`、`HardwareScanService.cs:58` | 抽出至 `appsettings.json` 或 `MachineConfig.json`，統一讀取 |
| 🟡 中危 | 日誌僅存於記憶體（`AllLogs` ≤500 筆），應用關閉後遺失 | `AlarmService.cs` | 加入檔案持久化（每日滾動日誌檔）；關鍵操作（急停、電源、設定部署）必須寫入持久日誌 |

### 重構優先順序（依風險等級排序）

**第一優先（嚴重）— 立即處理：**
1. 消除所有空 `catch { }`，改為記錄至 `AlarmService`
2. 急停指令使用獨立 `HttpClient`，不受其他請求阻塞
3. `SaveConfigAsync` 錯誤提升至使用者可見的警報
4. 密碼從原始碼移至外部加密儲存

**第二優先（高危）— 短期修復：**
5. 為不同操作建立獨立 `HttpClient` 實例（輪詢 / 上傳 / 設定 / 急停）
6. 狀態更新改為原子操作（`MachineStateSnapshot`）
7. 所有運動指令加入 `IsEstop` / `IsPower` 前置檢查
8. `AlarmService` 集合操作加入 `lock` 保護
9. Fire-and-Forget 非同步呼叫加入 `try/catch`
10. 輪詢加入防重疊機制與連續失敗計數

**第三優先（中危）— 中期改善：**
11. 伺服器 IP 抽出至設定檔
12. 日誌持久化至檔案
13. 斷線判定改為連續失敗計數器
14. 部署期間對集合做快照再迭代
15. 狀態轉換驗證表
