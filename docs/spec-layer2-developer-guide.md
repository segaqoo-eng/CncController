# 第二層：開發者入門指南（給剛學程式的人看）

> 本文件幫助「有基本程式概念但沒碰過這個專案」的人快速上手。
> 假設你知道什麼是變數、函式、if/else，但不一定懂 WPF 或 Flask。

---

## 1. 環境建置 SOP（從零到能 Build）

### 1.1 前端（Windows 觸控螢幕端）

**需要安裝的東西：**

| 軟體 | 版本 | 下載方式 | 用途 |
|------|------|---------|------|
| .NET 10 SDK | 10.x | https://dotnet.microsoft.com/download | 編譯 C# 程式碼 |
| Visual Studio 2022+ | Community（免費） | https://visualstudio.microsoft.com | 開發用 IDE |
| Git | 最新版 | https://git-scm.com | 版本控制 |

**Visual Studio 安裝時勾選：**
- ✅ .NET 桌面開發（包含 WPF）
- ✅ .NET 10 Runtime

**第一次 Build 步驟：**
```
步驟 1：用 Git 把程式碼抓下來
        git clone <專案網址>

步驟 2：用 Visual Studio 開啟
        雙擊 CncController.slnx（方案檔）

步驟 3：等 NuGet 套件自動還原
        （右下角會顯示進度，約 1-2 分鐘）

步驟 4：按 Ctrl+Shift+B（Build）
        成功的話，底部會顯示「Build succeeded」

步驟 5：按 F5（執行）
        會跳出 CNC 控制器的視窗
        （因為沒有連接機台，會顯示 Disconnected）
```

**如果 Build 失敗：**
- 確認 .NET SDK 版本 ≥ 10.0
- 確認 NuGet 還原完成（方案總管右鍵 → 還原 NuGet 套件）
- 確認有裝 WPF 工作負載

---

### 1.2 後端（Linux 機台端）

**需要的環境：**
- Ubuntu + LinuxCNC（已預裝在機台上）
- Python 3
- Flask 套件（`pip3 install flask`）

**執行方式：**
```bash
# 在機台的 Linux 上執行
cd ~/Server/
python3 guardian.py    # 正式環境（帶自動重啟守護）
python3 server.py      # 除錯用（直接執行，Ctrl+C 可停）
```

> 💡 如果你只負責前端，可以不用碰後端。前端連不上後端時只是顯示 Disconnected。

---

## 2. 專案地圖（簡化版）

把整個專案想像成一棟房子：

```
CncController/                ← 🏠 整棟房子（前端 WPF 專案）
│
├── Views/                    ← 🖼️ 房間的裝潢（畫面長什麼樣）
│   ├── Pages/                ←     每個主頁面（MONITOR、ATC、PROBING...）
│   ├── Layouts/              ←     固定不動的外框（頂部列、底部列、JOG 面板）
│   ├── Components/           ←     重複使用的小元件（座標顯示、滑桿、按鈕群）
│   ├── Controls/             ←     自訂的特殊元件（刀盤轉盤圖）
│   └── Windows/              ←     彈出視窗（登入、暖機）
│
├── ViewModels/               ← 🧠 房間的功能（按鈕按下去會發生什麼）
│   ├── MainViewModel*.cs     ←     主頁面的邏輯（拆成 8 個檔案）
│   ├── MonitorViewModel.cs   ←     MONITOR 頁的邏輯
│   ├── AtcViewModel.cs       ←     ATC 頁的邏輯
│   └── ...其他 VM            ←     每個頁面一個
│
├── Models/                   ← 📦 資料的形狀（數據長什麼樣）
│   ├── MachineStatus.cs      ←     機台即時狀態（~200 個屬性）
│   ├── ConfigModels.cs       ←     設定檔的資料結構
│   └── ...其他 Model         ←     每類資料一個檔案
│
├── Services/                 ← 📡 對外溝通的管道
│   ├── MachineControlService ←     跟 Linux 電腦溝通（HTTP）
│   ├── ConfigurationService  ←     生成設定檔（INI/HAL/XML）
│   ├── AlarmService          ←     管理警報和日誌
│   └── ...其他 Service       ←     各種功能服務
│
├── Resources/                ← 🎨 外觀資源
│   ├── Languages/            ←     多語言文字（中文/英文）
│   └── Themes/               ←     主題配色（深色/工業/科技）
│
└── Helpers/                  ← 🔧 工具函式（G-Code 解析等）

Server/                       ← 🖥️ Linux 後端（Python Flask）
├── server.py                 ←     程式進入點
├── shared.py                 ←     共用的東西（全域變數、工具函式）
└── routes/                   ←     API 路由（8 個模組）
    ├── status.py             ←     狀態查詢
    ├── machine.py            ←     機台控制
    ├── program.py            ←     程式管理
    ├── tool.py               ←     刀具管理
    ├── probe.py              ←     探測
    ├── atc.py                ←     刀庫
    ├── config.py             ←     設定部署
    └── stats.py              ←     統計/維護/續切
```

---

## 3. 核心觀念：MVVM 是什麼？

這個專案用 **MVVM 架構**，聽起來很複雜，其實就是把程式碼分成三層：

```
┌────────────┐     看到什麼      ┌────────────┐     資料/邏輯     ┌────────────┐
│   View     │  ──────────────→ │ ViewModel  │  ──────────────→ │   Model    │
│ （.xaml）   │  ←────────────── │  （.cs）    │  ←────────────── │  （.cs）    │
│  畫面/外觀  │    自動更新畫面    │  按鈕邏輯   │    提供資料       │  資料結構   │
└────────────┘                  └────────────┘                  └────────────┘
```

**比喻：**
- **View**（XAML 檔）= 電視螢幕（只負責顯示）
- **ViewModel**（C# 檔）= 遙控器（處理你按了什麼按鈕）
- **Model**（C# 檔）= 電視台（提供節目內容 = 資料）

**實際例子：**
```
你按了 CYCLE START 按鈕
    → View (CycleControl.xaml) 偵測到按鈕點擊
    → 呼叫 ViewModel (MainViewModel.CycleControl.cs) 的 CycleStartCommand
    → ViewModel 呼叫 Service (MachineControlService) 的 RunProgramAsync()
    → Service 送 HTTP POST 到 Linux 後端
    → 後端呼叫 LinuxCNC 開始執行程式
    → 後端回傳成功
    → ViewModel 更新狀態
    → View 自動把按鈕變成綠色（表示正在加工）
```

**關鍵語法（你會常看到的）：**

```csharp
// 在 ViewModel 裡，這一行會自動產生一個公開屬性 SpindleSpeed
// 畫面可以直接綁定這個名字
[ObservableProperty]
private double _spindleSpeed;

// 這一行會自動產生一個 CycleStartCommand，畫面的按鈕可以綁定它
[RelayCommand]
private async Task CycleStart() { ... }
```

```xml
<!-- 在 XAML 裡，{Binding} 就是跟 ViewModel 連接的橋 -->
<!-- 這行的意思：顯示 ViewModel 裡的 SpindleSpeed 值 -->
<TextBlock Text="{Binding SpindleSpeed}" />

<!-- 這行的意思：按鈕按下去就執行 CycleStartCommand -->
<Button Command="{Binding CycleStartCommand}" Content="CYCLE START" />
```

---

## 4. 核心觀念：前後端怎麼溝通？

前端和後端靠 **HTTP** 溝通（跟瀏覽器開網頁的原理一樣）：

```
前端（Windows）                              後端（Linux）

MachineControlService.cs                    Server/routes/*.py
┌─────────────────────┐                    ┌─────────────────────┐
│                     │   HTTP GET          │                     │
│  GetStatusAsync()   │ ──────────────────→ │  GET /v2/status     │
│                     │ ←────────────────── │  回傳 JSON 資料      │
│                     │   {"Position":...}  │                     │
│                     │                    │                     │
│  RunProgramAsync()  │   HTTP POST         │  POST /v2/program/  │
│                     │ ──────────────────→ │       run           │
│                     │ ←────────────────── │  回傳成功/失敗       │
│                     │   {"status":"ok"}   │                     │
└─────────────────────┘                    └─────────────────────┘
```

**通訊頻率：**
- 每 **50 毫秒**（0.05 秒）問一次後端：「現在座標多少？狀態怎樣？」
- 這就是為什麼畫面上的數字會即時跳動

**後端的快取機制（很重要）：**
```
後端並不是每次收到前端的請求才去問機台。
而是有兩個背景程式在不斷收集資料：

背景程式 1（每 20 毫秒）：收集座標、速度、狀態 → 存到快取
背景程式 2（每 1 秒）：收集 IO 信號、編碼器 → 存到快取

前端來問的時候，後端直接從快取回答（<1 毫秒就能回覆）
```

---

## 5. 資料流實例：按下 CYCLE START 後發生了什麼？

完整追蹤一個操作的資料流（從手指到馬達）：

```
                        ╔══════════════════════════════════════╗
                        ║           前端（Windows WPF）         ║
                        ╠══════════════════════════════════════╣
                        ║                                      ║
 1. 手指點按鈕 ────────→ ║ CycleControl.xaml                    ║
                        ║   <Button Command="{Binding          ║
                        ║           CycleStartCommand}">       ║
                        ║          │                           ║
 2. XAML 觸發命令 ──────→ ║          ▼                           ║
                        ║ MainViewModel.CycleControl.cs        ║
                        ║   [RelayCommand]                     ║
                        ║   async Task CycleStart()            ║
                        ║   {                                  ║
 3. VM 呼叫 Service ───→ ║     await _service.RunProgramAsync() ║
                        ║   }                                  ║
                        ║          │                           ║
                        ╚══════════╪═══════════════════════════╝
                                   │ HTTP POST /v2/program/run
                                   ▼
                        ╔══════════════════════════════════════╗
                        ║           後端（Linux Flask）          ║
                        ╠══════════════════════════════════════╣
                        ║                                      ║
 4. Flask 收到請求 ────→ ║ routes/program.py                    ║
                        ║   @bp.route('/v2/program/run', POST) ║
                        ║   def run_program():                 ║
                        ║     cnc_cmd.mode(MODE_AUTO)          ║
 5. 呼叫 LinuxCNC ────→ ║     cnc_cmd.auto(AUTO_RUN, line)     ║
                        ║     return success_response()        ║
                        ║          │                           ║
                        ╚══════════╪═══════════════════════════╝
                                   │ LinuxCNC NML IPC
                                   ▼
                        ╔══════════════════════════════════════╗
                        ║     LinuxCNC → EtherCAT → 馬達       ║
                        ║     （機台開始實際加工）                 ║
                        ╚══════════════════════════════════════╝
```

---

## 6. 檔案對應地圖（改功能要動哪些檔案）

### 6.1 每個頁面的檔案對應

| 頁面 | 畫面檔（View） | 邏輯檔（ViewModel） | 後端路由 |
|------|---------------|-------------------|---------|
| 主頁面 | MainView.xaml | MainViewModel*.cs（8 檔） | routes/status.py |
| 程式監控 | MonitorView.xaml | MonitorViewModel.cs | routes/program.py |
| 檔案管理 | FileManagerView.xaml | FileManagerViewModel.cs | routes/program.py |
| 座標偏移 | OffsetsView.xaml | OffsetsViewModel.cs | routes/status.py |
| 刀具表 | ToolTableView.xaml | ToolTableViewModel.cs | routes/tool.py |
| 自動換刀 | AtcView.xaml | AtcViewModel.cs | routes/atc.py |
| 探測 | ProbingView.xaml | ProbingViewModel.cs | routes/probe.py |
| 巨集變數 | MacroVariablesView.xaml | MacroVariablesViewModel.cs | routes/program.py |
| 歷史記錄 | HistoryView.xaml | HistoryViewModel.cs | routes/stats.py |
| 設定 | SettingsView.xaml | SettingsViewModel.cs + 11 子 VM | routes/config.py |

### 6.2 常見修改場景

#### 「我要在某個頁面加一個按鈕」
```
1. 開啟該頁面的 .xaml 檔（Views/Pages/XxxView.xaml）
2. 在想要的位置加上 <Button>
3. 開啟對應的 ViewModel（ViewModels/XxxViewModel.cs）
4. 新增一個 [RelayCommand] 方法
5. 在 XAML 裡用 Command="{Binding 方法名Command}" 連接
6. 如果按鈕文字要多語言：
   - 去 Resources/Languages/Lang.zh-TW.xaml 加中文
   - 去 Resources/Languages/Lang.en-US.xaml 加英文
   - 按鈕改用 Content="{DynamicResource Str.Btn.XXX}"
```

#### 「我要加一個新的 API」
```
1. 決定放哪個後端模組（看功能歸類）
   例如：刀具相關 → Server/routes/tool.py
2. 在該 .py 檔新增路由函式：
   @bp.route('/v2/tool/新路由', methods=['POST'])
   def new_function(): ...
3. 前端 Services/MachineControlService.cs 新增對應的 Async 方法
4. ViewModel 呼叫該 Service 方法
```

#### 「我要改後端回傳的資料格式」
```
⚠️ 前後端必須同步改！
1. 後端：改 routes/*.py 裡的 return 資料
2. 前端：改 Models/*.cs 裡的 DTO class（屬性名要對應 JSON 欄位）
3. 前端：改 MachineControlService.cs 裡解析 JSON 的地方
```

#### 「我要改畫面的顏色/樣式」
```
1. 全域樣式 → Resources/Themes/Theme.Dark.xaml
2. 特定頁面 → 該頁面 .xaml 的 <Page.Resources> 區段
3. 色彩常數搜尋 → 用 Brush. 開頭的名字（如 Brush.Primary、Brush.Background）
```

---

## 7. 重要檔案速查表

### 最常需要改的檔案（前 10 名）

| 排名 | 檔案 | 行數 | 你什麼時候會改它 |
|------|------|------|----------------|
| 1 | `MachineControlService.cs` | ~1171 | 新增/修改任何跟後端溝通的功能 |
| 2 | `MachineStatus.cs` | ~200 屬性 | 後端回傳的資料有增減 |
| 3 | 各 `*ViewModel.cs` | 各異 | 修改任何頁面的邏輯 |
| 4 | 各 `*View.xaml` | 各異 | 修改任何頁面的外觀 |
| 5 | `Lang.zh-TW.xaml` | ~391 key | 新增/修改中文文字 |
| 6 | `Lang.en-US.xaml` | ~391 key | 新增/修改英文文字 |
| 7 | `Theme.Dark.xaml` | — | 修改全域樣式 |
| 8 | `ConfigurationService.cs` | ~1310 | 修改設定檔生成邏輯 |
| 9 | `Server/routes/*.py` | ~2504 | 修改後端 API |
| 10 | `Server/shared.py` | ~275 | 修改後端共用功能 |

### 碰都不要碰的檔案（除非你很確定在做什麼）

| 檔案 | 風險 | 原因 |
|------|------|------|
| `ConfigurationService.cs` | 🔴 極高 | 生成機台設定檔，寫錯 = 機台無法開機 |
| `AtcNgcGeneratorService.cs` | 🔴 極高 | 生成換刀巨集，寫錯 = 撞刀 |
| `MainViewModel.Safety.cs` | 🟡 高 | 急停/電源邏輯，改壞 = 無法緊急停止 |
| `Server/routes/probe.py` | 🟡 高 | 探測邏輯，改壞 = 探測棒撞壞工件 |

---

## 8. 除錯技巧

### 前端除錯
```
1. Visual Studio → 按 F5 → 啟動 Debug 模式
2. 在程式碼左邊點一下 → 設定中斷點（紅色圓點）
3. 程式跑到那一行會暫停，你可以看變數的值
4. 看 Output 視窗（Ctrl+Alt+O）→ 搜尋 "[ALARM]" 或 "[ERROR]"
```

### 後端除錯
```
1. SSH 連到 Linux 機台
2. python3 server.py（直接執行，不要用 guardian）
3. 終端機會印出所有 API 請求的 log
4. 用瀏覽器打開 http://192.168.0.137:5000/v2/status 看回傳資料
```

### 通訊除錯
```
1. 前端連不上？
   → 確認 appsettings.json 裡的 ServerUrl 是否正確
   → 確認 Linux 電腦有開機、server.py 有在跑
   → 用 ping 192.168.0.137 測試網路

2. 資料不對？
   → 先用瀏覽器直接打 API 看原始回傳
   → 再看 MachineControlService 解析有沒有問題
   → 再看 ViewModel 更新有沒有問題
```

---

## 9. 程式碼規範（必須遵守）

| 規則 | 說明 | 範例 |
|------|------|------|
| 日期註解 | 每次改程式碼要加日期 | `// [2026-03-16] 新增 XXX 功能` |
| 多語言 | 所有畫面文字用 DynamicResource | `{DynamicResource Str.Btn.Save}` |
| 禁止硬編碼中文 | XAML 裡不能直接寫中文 | ❌ `Content="儲存"` → ✅ `{DynamicResource}` |
| 按鈕樣式 | 一律繼承 BaseBtnStyle | 不可以自己寫 `Background="#xxx"` |
| Tab 切換 | 用 RadioButton + StringEqualConverter | 不可以用 TabControl |
| HttpClient | 用既有的長駐 Client | 不可以 `new HttpClient()` |
| Service | 手動 Singleton（Instance 屬性） | 不用 DI Container |

---

## 10. 技術名詞對照（程式層面）

| 你在程式碼裡看到的 | 它是什麼意思 |
|------------------|------------|
| `[ObservableProperty]` | 自動產生一個「會通知畫面更新」的屬性 |
| `[RelayCommand]` | 自動產生一個按鈕可以綁定的命令 |
| `{Binding xxx}` | XAML 跟 ViewModel 的資料連接 |
| `{DynamicResource xxx}` | 動態資源綁定（多語言/主題色） |
| `DataTrigger` | 當某個值改變時，自動改變外觀 |
| `ObservableCollection` | 一種清單，增減項目時畫面會自動更新 |
| `async/await` | 非同步操作（不會卡住畫面） |
| `HttpClient` | C# 用來發 HTTP 請求的工具 |
| `DispatcherTimer` | 定時器（每隔一段時間做一件事） |
| `partial class` | 把同一個 class 拆成多個檔案（方便管理） |
| `Flask Blueprint` | Python Flask 的模組化路由 |
| `NML` | LinuxCNC 的進程間通訊機制 |
| `cnc_cmd` / `cnc_stat` | 後端跟 LinuxCNC 溝通的兩條管道（命令/狀態） |
| `halcmd` | 讀寫 LinuxCNC 硬體信號的命令列工具 |
