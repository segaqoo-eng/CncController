# Soft PLC + ATC 換刀架構規劃

> 文件建立：2026-02-24
> 狀態：規劃階段（僅文件，尚未實作）

---

## 1. Soft PLC 定位

### 1.1 什麼是 Soft PLC

傳統 CNC 控制器內建硬體 PLC（如 FANUC PMC、SIEMENS 內建 PLC），用於處理 M-Code 巨集、
刀庫管理、冷卻液控制、門鎖互鎖等「非運動控制」的邏輯任務。

在 LinuxCNC 架構下，相對應的角色是 **ClassicLadder**（梯形圖 PLC）或 **自訂 HAL Component**。
本專案選擇以 **Python HAL Component + 前端狀態機** 實現 Soft PLC，原因：

| 方案 | 優點 | 缺點 |
|------|------|------|
| ClassicLadder | 標準工業 PLC 語言 | 調試困難、UI 整合差 |
| Python HAL Component | 彈性高、可與 Flask API 整合 | 需自行保證即時性 |
| **混合方案（推薦）** | HAL 處理硬即時、Python 處理邏輯 | 需清晰分層 |

### 1.2 Soft PLC 在系統中的位置

```
┌─────────────────────────────────────────────┐
│  WPF 前端                                    │
│  MainViewModel → ATC 操作 UI                 │
│         ↓ HTTP POST                          │
│  MachineControlService                       │
└──────────────┬──────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────┐
│  Flask 後端 (server.py)                      │
│  /v2/atc/change   → 觸發換刀流程             │
│  /v2/atc/status   → 回報換刀狀態             │
│  /v2/atc/tool-table → 刀具表 CRUD            │
│         ↓ HAL API                            │
│  atc_component.py (HAL userspace comp)       │
└──────────────┬──────────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────────┐
│  LinuxCNC HAL 層                             │
│  iocontrol.0.tool-change → ATC 換刀信號      │
│  iocontrol.0.tool-changed → 換刀完成回報     │
│  iocontrol.0.tool-prepare → 預備刀號         │
│  iocontrol.0.tool-prepared → 預備完成        │
│  motion.spindle-orient → 主軸定向            │
└─────────────────────────────────────────────┘
```

### 1.3 職責分工

| 層級 | 職責 | 即時性要求 |
|------|------|-----------|
| **HAL 層** | 信號傳遞、IO 狀態、伺服位置 | Hard Realtime |
| **Python HAL Component** | 換刀狀態機、安全互鎖、超時監控 | Soft Realtime (~10ms) |
| **Flask API** | 指令分發、狀態查詢、刀具表管理 | Non-Realtime |
| **WPF 前端** | 操作介面、刀號選擇、狀態顯示 | Non-Realtime |

---

## 2. ATC 標準流程

### 2.1 M6 換刀流程（標準 CNC 行為）

```
G-Code: T3 M6    (準備 3 號刀 + 執行換刀)

Step 1: LinuxCNC 解析 T3 → 設定 iocontrol.0.tool-prep-number = 3
Step 2: LinuxCNC 觸發 iocontrol.0.tool-prepare = TRUE
Step 3: ATC Component 收到信號 → 旋轉刀庫至 3 號位
Step 4: 刀庫到位 → ATC Component 回報 iocontrol.0.tool-prepared = TRUE
Step 5: LinuxCNC 觸發 iocontrol.0.tool-change = TRUE
Step 6: ATC 執行換刀動作:
   6a. 主軸上升至安全高度 (Z safe)
   6b. 主軸定向 (M19 / spindle orient)
   6c. 鬆刀 (unclamp) — 氣缸/液壓推動拉爪
   6d. 主軸退出 (Z up) — 刀具脫離主軸
   6e. 刀庫旋轉交換 — 舊刀入庫 + 新刀到位
   6f. 主軸下降 (Z down) — 新刀進入主軸錐孔
   6g. 夾刀 (clamp) — 拉桿鎖緊
   6h. 主軸上升至工作位置
Step 7: ATC Component 回報 iocontrol.0.tool-changed = TRUE
Step 8: LinuxCNC 更新刀具補償 → 繼續執行程式
```

### 2.2 安全檢查要點

| 檢查項目 | 時機 | 失敗動作 |
|---------|------|---------|
| 主軸停止確認 | Step 6b 前 | 中止換刀 + 報警 |
| 主軸定向完成 | Step 6b 後 | 等待超時報警 |
| 刀具鬆開感測器 | Step 6c 後 | 超時報警 |
| 刀具夾緊感測器 | Step 6g 後 | 超時報警 |
| Z 軸安全位置 | Step 6a 確認 | 中止 |
| 刀庫到位感測器 | Step 3/6e | 超時報警 |
| 氣壓/液壓充足 | 全程 | 暫停 + 報警 |
| 護門關閉 | 開始前 | 禁止換刀 |

---

## 3. HAL Signal 規劃

### 3.1 LinuxCNC 內建 IO Control 信號

```
# 換刀核心信號（LinuxCNC 自動管理）
iocontrol.0.tool-prepare      → OUT  準備刀號
iocontrol.0.tool-prepared     ← IN   準備完成
iocontrol.0.tool-change       → OUT  執行換刀
iocontrol.0.tool-changed      ← IN   換刀完成
iocontrol.0.tool-prep-number  → OUT  目標刀號
iocontrol.0.tool-prep-pocket  → OUT  目標刀位
iocontrol.0.tool-number       → OUT  當前刀號
```

### 3.2 自訂 HAL 信號（ATC Component）

```
# ATC 狀態輸出（前端讀取）
atc.0.state              → INT    狀態機代碼 (0=IDLE, 1=PREPARING, ...)
atc.0.current-tool       → INT    目前主軸刀號
atc.0.current-pocket     → INT    目前刀位
atc.0.error              → BIT    錯誤旗標
atc.0.error-code         → INT    錯誤代碼

# 硬體 IO 輸出（控制氣缸/電磁閥）
atc.0.unclamp-cmd        → BIT    鬆刀氣缸
atc.0.magazine-cw        → BIT    刀庫正轉
atc.0.magazine-ccw       → BIT    刀庫反轉
atc.0.arm-extend         → BIT    機械手臂伸出（有手臂式）

# 硬體 IO 輸入（感測器回饋）
atc.0.clamp-sensor       ← BIT    刀具夾緊感測器
atc.0.unclamp-sensor     ← BIT    刀具鬆開感測器
atc.0.magazine-ready     ← BIT    刀庫到位感測器
atc.0.arm-retracted      ← BIT    機械手臂縮回
atc.0.air-pressure-ok    ← BIT    氣壓正常
```

### 3.3 PostGUI HAL 配線範例

```hal
# ATC Component 載入
loadusr -W atc_component

# 連接 LinuxCNC iocontrol 信號
net tool-prepare    iocontrol.0.tool-prepare    => atc.0.prepare-cmd
net tool-prepared   atc.0.prepare-done          => iocontrol.0.tool-prepared
net tool-change     iocontrol.0.tool-change     => atc.0.change-cmd
net tool-changed    atc.0.change-done           => iocontrol.0.tool-changed
net tool-prep-num   iocontrol.0.tool-prep-number => atc.0.prep-number

# 連接硬體 IO（假設 DO slave index=4, DI slave index=5）
net atc-unclamp     atc.0.unclamp-cmd    => lcec.0.4.dout-00
net atc-mag-cw      atc.0.magazine-cw    => lcec.0.4.dout-01
net atc-mag-ccw     atc.0.magazine-ccw   => lcec.0.4.dout-02
net atc-clamp-ok    lcec.0.5.din-00      => atc.0.clamp-sensor
net atc-unclamp-ok  lcec.0.5.din-01      => atc.0.unclamp-sensor
net atc-mag-ready   lcec.0.5.din-02      => atc.0.magazine-ready
net atc-air-ok      lcec.0.5.din-03      => atc.0.air-pressure-ok
```

---

## 4. 刀庫類型

### 4.1 常見 CNC 刀庫類型

| 類型 | 容量 | 換刀時間 | 適用場合 | 控制複雜度 |
|------|------|---------|---------|-----------|
| **旋轉盤式 (Carousel)** | 10~24 把 | 3~5s | 小型 VMC | 低 |
| **鏈條式 (Chain)** | 24~60 把 | 5~8s | 中型 VMC | 中 |
| **圓盤雙臂式 (Double Arm)** | 20~40 把 | 1.5~3s | 高速加工 | 高 |
| **矩陣式 (Matrix)** | 60~120 把 | 8~15s | FMS 系統 | 高 |

### 4.2 建議初期支援

**Phase 1**：旋轉盤式（Carousel） — 最常見、控制邏輯最簡單

特點：
- 刀庫固定在主軸旁或柱頭
- 旋轉至目標刀位後，主軸直接上下取刀
- 不需要機械手臂（無 arm 信號）
- 只需控制：旋轉方向 + 到位感測 + 鬆夾刀

### 4.3 刀庫旋轉邏輯

```
目標刀位: T5
目前位置: T2
刀庫容量: 16 把

正轉距離 = (5 - 2) = 3 步
反轉距離 = 16 - 3 = 13 步

最短路徑 = 正轉 3 步 (CW)
```

---

## 5. 與現有架構整合點

### 5.1 前端整合

| 元件 | 修改內容 |
|------|---------|
| **MainViewModel** | 新增 `CurrentTool`、`AtcState` 屬性；輪詢 `/v2/atc/status` |
| **ToolInfo 元件** | 從靜態顯示改為即時綁定，顯示刀號/刀長/刀徑 |
| **CycleControl** | ATC Tab 按鈕（導航至 ATC 管理頁） |
| **新增 AtcViewModel** | 刀具表 CRUD、手動換刀、刀庫旋轉 |
| **新增 AtcView** | 刀庫俯視圖（圓形佈局）+ 刀具表 DataGrid |

### 5.2 後端整合

| 元件 | 修改內容 |
|------|---------|
| **server.py** | 新增 `/v2/atc/*` 端點群組 |
| **atc_component.py** | 新增 HAL userspace component |
| **ConfigurationService** | HAL 生成需加入 ATC PostGUI 配線 |
| **/v2/status** | 回傳擴充：`Current_Tool`、`ATC_State` |

### 5.3 新增 API 端點

| 方法 | 路由 | 功能 |
|------|------|------|
| GET | `/v2/atc/status` | ATC 狀態（state, current_tool, error） |
| POST | `/v2/atc/change` | 觸發換刀（target_tool） |
| GET | `/v2/atc/tool-table` | 讀取刀具表 |
| POST | `/v2/atc/tool-table` | 更新刀具表 |
| POST | `/v2/atc/magazine/rotate` | 手動旋轉刀庫 |
| POST | `/v2/atc/clamp` | 手動夾刀/鬆刀 |

### 5.4 MachineStatusData 擴充

```csharp
// 新增至 MachineStatusData
public int Current_Tool { get; set; } = 0;
public string ATC_State { get; set; } = "IDLE";  // IDLE/PREPARING/CHANGING/ERROR
public int ATC_Error_Code { get; set; } = 0;
```

---

## 6. 開發里程碑

### Phase 0：基礎設施（預計 1 週）
- [ ] 定義 ATC 狀態機枚舉（IDLE → PREPARING → CHANGING → DONE / ERROR）
- [ ] `/v2/status` 擴充 `Current_Tool` 欄位
- [ ] ToolInfo 元件改為即時綁定
- [ ] 刀具表資料模型（ToolEntry: Number, Pocket, Diameter, Length, Description）

### Phase 1：刀具表管理（預計 1 週）
- [ ] 後端讀寫 `tool.tbl`（LinuxCNC 標準格式）
- [ ] 前端 ToolTableView + ToolTableViewModel
- [ ] DataGrid CRUD（新增/刪除/修改刀具參數）
- [ ] 同步至 LinuxCNC（`cnc_cmd.load_tool_table()`）

### Phase 2：ATC HAL Component（預計 2 週）
- [ ] `atc_component.py`：HAL userspace component
- [ ] 換刀狀態機實作（8 步驟 + 安全檢查）
- [ ] PostGUI HAL 配線模板
- [ ] ConfigurationService 整合（自動生成 ATC HAL）

### Phase 3：前端整合（預計 1 週）
- [ ] AtcView 刀庫俯視圖
- [ ] 手動換刀 UI（選刀號 → 確認 → 執行）
- [ ] ATC 狀態即時顯示（轉動中動畫、錯誤提示）
- [ ] M6 換刀與 G-Code 執行聯動

### Phase 4：進階功能（後續）
- [ ] 刀具壽命管理（加工時間/次數統計）
- [ ] 刀具磨耗補償自動調整
- [ ] 多刀庫類型支援（Chain / Double Arm）
- [ ] 刀長量測整合（Tool Setter）

---

## 7. 安全考量

### 7.1 失敗模式分析

| 故障情境 | 偵測方式 | 應對措施 |
|---------|---------|---------|
| 鬆刀失敗 | unclamp sensor 超時 | 停止流程 + ERR 報警 |
| 夾刀失敗 | clamp sensor 超時 | 停止流程 + ERR 報警 + 禁止主軸啟動 |
| 刀庫卡住 | magazine ready 超時 | 停止旋轉 + ERR 報警 |
| 氣壓不足 | air pressure sensor | 暫停換刀 + WARN 報警 |
| 主軸未停止 | spindle speed ≠ 0 | 拒絕換刀 |
| Z 軸未到安全位 | Z position 判斷 | 先移至安全高度 |
| 通訊中斷 | HAL watchdog | 急停 |

### 7.2 超時設定建議

| 動作 | 建議超時 | 說明 |
|------|---------|------|
| 主軸定向 | 5 秒 | M19 完成 |
| 鬆刀 | 3 秒 | 氣缸動作 |
| 夾刀 | 3 秒 | 氣缸動作 |
| 刀庫旋轉 | 15 秒 | 依刀庫大小調整 |
| 整體換刀 | 30 秒 | 全流程超時保護 |

---

## 8. 檔案結構規劃

```
CncController/
├── ViewModels/
│   └── AtcViewModel.cs          # ATC 管理 ViewModel
├── Views/Pages/
│   └── AtcView.xaml             # ATC 管理頁面
├── Models/
│   └── ToolEntry.cs             # 刀具表資料模型

Server/
├── atc_component.py             # HAL userspace component
├── tool_table.py                # 刀具表讀寫工具
```
