# Task: 統一 UI 樣式 + i18n 多語言（CncController WPF）

## 背景

CncController 是一個 WPF/MVVM 的 CNC 機台控制前端專案。
本文件涵蓋以下五類任務，**依序執行，每個 Part 獨立確認後才進行下一個**：

1. **分頁選取樣式**：各種分頁切換機制的選取色、字型各自為政
2. **區塊標頭樣式**：各 View 的標頭文字區塊背景色與字型不統一
3. **按鈕樣式**：部分按鈕有紅綠色，需統一為標準格式
4. **i18n 多語言**：UI 文字寫死在 XAML，需抽出集中管理，方便未來切換語言
5. **ShowMessage / ErrorCode / 提示文字**：C# 程式碼中的動態文字寫死，需集中到 `.resx` 統一管理

> ⚠️ **執行原則：先掃描、先出計畫書、等使用者確認後再動手修改。**
> 任何步驟都不可在未經確認前自行修改程式碼。
> **每個 Part 獨立進行，不可同時修改多個 Part。**

---

## Part 1 — 分頁選取樣式

### 背景知識（給 AI 參考）

已知專案中存在以下幾種可能的分頁切換實作方式，但實際數量與位置請以掃描結果為準：

| 可能的實作方式 | 說明 |
|----------------|------|
| `Button` + `NavigateCommand` | 點擊切換頁面，靠 ViewModel 屬性判斷是否為當前頁 |
| `TabControl` + `TabItem` | WPF 原生分頁，靠 `IsSelected` 判斷 |
| `RadioButton` + `DataTrigger` | 靠 `IsChecked` 切換顯示內容 |
| `ToggleButton` | 靠 `IsChecked` 切換狀態 |

### Step 1 — 掃描所有分頁元素（做完回報，等確認再動手）

掃描 `**/*.xaml`，找出所有「可點擊 + 有選取狀態」的分頁切換元素。

**搜尋關鍵字：**

| 關鍵字 | 意義 |
|--------|------|
| `NavigateCommand` | Button 型導航 |
| `CachedContentControl` | 頁面切換容器 |
| `TabControl` / `TabItem` | 原生分頁 |
| `RadioButton` | RadioButton 型分頁 |
| `ToggleButton` | ToggleButton 型分頁 |
| `IsSelected` / `IsChecked` | 選取狀態屬性 |
| `SelectedIndex` / `SelectedItem` | 分頁綁定屬性 |

找到後，**依實作方式與位置自行分組**，分組參考如下
（不限於此，遇到不符合的請自行新增）：

| 分組名稱 | 視覺特徵 | 可能的位置 |
|----------|----------|------------|
| `NavButton` | 頂部橫列導航、全寬分配、矩形 | MainWindow 頂部 |
| `TabItem` | WPF 原生分頁列 | SettingsView 等 |
| `RadioTabButton` | 獨立按鈕組、有圓角 | ProbingView 等 |

掃描完成後，整理成下表**回報，不要動手修改**：

| 檔案 | 分組 | 元素類型 | 數量 | 目前選取背景色 | 目前未選取背景色 | FontSize | 備註 |
|------|------|----------|------|----------------|------------------|----------|------|
| （AI 填入） | | | | | | | |

### Step 2 — 出計畫書（回報後執行）

根據掃描結果，為每個分組提出統一方案，格式如下：

```
分組：NavButton
位置：MainWindow.xaml
數量：X 個
目前問題：選取色不一致（有 #xxx、#xxx 兩種）
建議 Style Key：NavButtonStyle
建議實作方式：ControlTemplate + DataTrigger 綁定 [確認 property 名稱]
影響範圍：X 個 Button
需要確認：選取判斷用的 ViewModel property 名稱為何？
```

每個分組都要出一段，最後列出：
- 共需新增幾個 Style
- 共需修改幾個檔案
- 有無風險或需要特別注意的地方

### Step 3 — 執行修改（計畫書確認後才執行）

依確認後的計畫書：
1. 在 `App.xaml` 加入共用色彩資源（Brush / Color）
2. 為每個分組建立對應的 Style / ControlTemplate
3. 到各 View 套用 Style，移除 inline 的 `Background` / `Foreground`
4. **不動任何邏輯**：保留 Command binding、ViewModel 屬性、DataTrigger 條件

---

## Part 2 — 區塊標頭樣式

### 背景知識（給 AI 參考）

專案中存在多種「純文字 + 不可點擊」的標頭元素，
外觀為深色背景 + 白色置中文字，WPF 實作通常為 `Border` 包 `TextBlock`
或單獨一個有背景色的 `TextBlock`。

### Step A — 掃描所有標頭元素（做完回報，等確認再動手）

掃描 `**/*.xaml`，找出所有符合以下條件的元素：

**條件：純文字 + 不可點擊**
- `TextBlock` 或 `Label`
- 自身或父層 `Border` 設有 `Background=`（非透明、非空值）
- 沒有 `Command`、`Click`、`IsChecked` 等互動屬性

找到後，**依背景色深淺與元素尺寸自行分組**，分組參考如下
（不限於此，遇到不符合的請自行新增）：

| 分組名稱 | 視覺特徵 | 截圖範例 |
|----------|----------|----------|
| `PanelTitle` | 大區塊標題、全寬、較高、深色背景 | 「自動換刀控制面板」藍色橫條 |
| `GroupHeader` | 小群組標題、次級標題、區塊內部 | 「刀位設定」小藍條 |
| `SectionLabel` | 純說明文字、背景較淺或透明 | 欄位前的說明文字 |

掃描完成後，整理成下表**回報，不要動手修改**：

| 檔案 | 分組 | 元素類型 | 文字內容 | 目前背景色 | FontSize | FontWeight |
|------|------|----------|----------|------------|----------|------------|
| （AI 填入） | | | | | | |

### Step B — 出計畫書（回報後執行）

根據掃描結果，為每個分組提出統一方案，格式如下：

```
分組：PanelTitle
位置：AtcView.xaml, ProbingView.xaml, ...
數量：X 個
目前問題：背景色有 #xxx、#xxx 兩種不一致
建議 Style Key：PanelTitleStyle
建議規格：Background=#xxx, FontSize=14, Bold, 置中, Padding=6,8
XAML 結構：Border + TextBlock（需拆兩個 Style）/ 單 TextBlock
影響範圍：X 個元素，X 個檔案
```

每個分組都要出一段，最後列出：
- 共需新增幾個 Style
- 共需修改幾個檔案
- 有無結構差異需要特別處理

### Step C — 執行修改（計畫書確認後才執行）

依確認後的計畫書：
1. 在 `App.xaml` 為每個分組加入獨立的 Brush 資源與 Style
2. 到各 View 套用 Style，移除 inline 的 `Background`、`Foreground`、`FontSize`、`FontWeight`
3. 若元素為 `Border` + `TextBlock` 組合，Style 拆成兩個，保持 XAML 結構不變

---


---

## Part 3 — 按鈕樣式統一

### 背景說明

專案中所有具有點擊功能的元素（`Button`、`RadioButton` 當按鈕用、`ToggleButton` 等）
預設外觀需統一為標準格式。

**標準按鈕外觀（依截圖）：**
- 背景：深灰 `#3A3A3A`
- 文字：白色 `#FFFFFF`
- 邊框：細線 `#555555`，`BorderThickness="1"`
- 圓角：`CornerRadius="4"`
- Hover：背景變淺 `#4A4A4A`
- Pressed：背景變深 `#2A2A2A`
- Disabled：背景 `#1E1E1E`，文字 `#555555`

**錯誤範例（需修正）：**

| 位置 | 元素 | 目前錯誤 |
|------|------|----------|
| MainView DRO 區 | `REF X` / `REF Y` / `REF Z` | 紅色背景 |
| MainView DRO 區 | `原點復歸 ALL` | 紅色背景 |
| SettingsView ATC | `設定` | 綠色背景 |
| SettingsView ATC | `清除` | 紅色背景 |
| MainView Spindle | `V 100%` / `F 100%` / `S 100%` / `R 100%` | 樣式不統一 |

> **原則：預設狀態下所有按鈕顏色一致，不用紅綠色區分功能。**
> 若需要語意色（如危險操作），請另行提出，不在本次範圍內。

---

### Step A — 掃描（做完回報，等確認再動手）

**先盤點現有資源：**
掃描 `App.xaml` 及所有 `ResourceDictionary`，列出已存在的 Button 相關 Style / Brush，
包含：隱式 `TargetType="Button"` Style、具名 ButtonStyle、色票 Brush Key。

**再掃描所有按鈕元素：**

掃描 `**/*.xaml`，找出所有具有點擊功能的元素：

**搜尋關鍵字：**

| 關鍵字 | 意義 |
|--------|------|
| `<Button` | 標準按鈕 |
| `<RadioButton` | RadioButton 當按鈕用（非分頁用途） |
| `<ToggleButton` | 切換型按鈕 |
| `<RepeatButton` | 長按型按鈕（如 JOG 方向鍵） |
| `Background="#FF` | 有明確背景色設定（紅色 / 綠色等） |
| `Background="Red"` / `Background="Green"` | 直接寫色名 |

找到後，**依外觀特徵自行分組**，分組參考如下
（不限於此，遇到不符合的請自行新增）：

| 分組名稱 | 視覺特徵 | 截圖範例 |
|----------|----------|----------|
| `DefaultButton` | 一般操作按鈕、深灰背景 | `GO TO ZERO`、`全部歸零` |
| `DangerButton` | 目前為紅色、需改為標準色 | `REF X`、`原點復歸 ALL`、`清除` |
| `SuccessButton` | 目前為綠色、需改為標準色 | `設定` |
| `RepeatButton` | 方向 JOG 按鈕、長按功能 | ▲ ▼ ◀ ▶ |
| `ToggleButton` | 有開關狀態 | `JOG`、`MAN/AUTO/MDI` 等 |

掃描完成後，整理成下表**回報，不要動手修改**：

| 檔案 | 分組 | 元素類型 | 文字/內容 | 目前背景色 | 有無 Command | 備註 |
|------|------|----------|-----------|------------|--------------|------|
| （AI 填入） | | | | | | |

---

### Step B — 出計畫書（回報後執行）

根據掃描結果，為每個分組提出統一方案，格式如下：

```
分組：DangerButton（目前紅色，需改為標準色）
位置：MainView.xaml, SettingsView.xaml, ...
數量：X 個
目前問題：Background="#FF0000" 或 Background="Red" inline 寫死
建議 Style Key：統一套用 DefaultButtonStyle
修改方式：移除 inline Background，套用 DefaultButtonStyle
影響範圍：X 個按鈕，X 個檔案
需要確認：這些按鈕改為標準灰色後，使用者是否還能辨識功能？
```

最後列出：
- 共需新增幾個 Style
- 共需修改幾個檔案
- 哪些按鈕改色後可能影響操作辨識度，需使用者特別確認

---

### Step C — 執行修改（計畫書確認後才執行）

依確認後的計畫書：

1. **優先沿用專案現有的 Style 資源**
   先檢查 `App.xaml` 及各 `ResourceDictionary` 是否已有 `Button` 的隱式 Style 或具名 Style，
   若已存在則直接套用，不要重複建立。

2. 若現有 Style 不足（缺少某分組），再依現有色票命名慣例新增，保持風格一致。

3. 到各 View 移除 inline 的 `Background`、`Foreground`、`BorderBrush`，
   套用對應的 Style。

4. `RepeatButton` / `ToggleButton` 各自確認是否已有 Style，
   有則沿用，無則新增。


---

## Part 4 — i18n 多語言實作

### 背景說明

專案目前 UI 文字（中文標籤、按鈕文字、標頭文字等）大多直接寫死在 XAML 中，
例如 `Content="裝刀至主軸"`、`Text="刀庫歸零"`。

若未來需要出英文版或其他語言版本，需逐一翻找 XAML 修改，風險高且耗時。
**i18n（internationalization）** 的目標是將所有 UI 文字集中到 `.resx` 資源檔，
XAML 改用 `x:Static` binding，換語言只需切換資源檔，不動程式碼。

> 注意：專案已有 `.resx` + `x:Static` 的實作基礎，本 Part 目標是**補全尚未抽出的文字**，
> 不是從零開始建立機制。

---

### Step A — 掃描現有 i18n 狀態（做完回報，等確認再動手）

**先盤點現有資源：**
1. 找出專案中所有 `.resx` 檔案，列出檔名與包含的 Key 數量
2. 找出已使用 `x:Static` binding 的 XAML，確認目前覆蓋範圍
3. 確認現有的命名慣例（Key 命名規則，例如 `Atc_ButtonLoad`、`Main_LabelZeroX` 等）

**再掃描尚未抽出的文字：**

掃描 `**/*.xaml`，找出所有直接寫死文字的地方：

| 搜尋目標 | 說明 |
|----------|------|
| `Content="[中文或英文]"` | Button / Label 的文字 |
| `Text="[中文或英文]"` | TextBlock 的文字 |
| `Header="[中文或英文]"` | TabItem / GroupBox 的標題 |
| `ToolTip="[中文或英文]"` | 提示文字 |
| `x:Static` 已綁定的 | 跳過，已完成 |

掃描完成後，整理成下表**回報，不要動手修改**：

| 檔案 | 元素類型 | 屬性 | 目前文字 | 建議 Key 名稱 | 備註 |
|------|----------|------|----------|---------------|------|
| （AI 填入） | | | | | |

同時回報：
- 目前已抽出的比例（已用 `x:Static` / 總文字數）
- 尚未抽出的文字總數
- 是否有重複文字可共用同一個 Key

---

### Step B — 出計畫書（回報後執行）

根據掃描結果提出執行計畫：

```
現有 .resx 檔案：Strings.zh-TW.resx（X 個 Key）
現有覆蓋率：約 X%
尚未抽出：X 個文字，分布在 X 個檔案

建議執行順序：
1. 先補 MainView（最多人看到）
2. 再補 SettingsView
3. 最後補其餘 View

Key 命名規則沿用現有慣例：[View名稱]_[控制項類型][描述]
例：Atc_BtnLoad、Main_LblZeroX

預計新增 Key 數：X 個
預計修改 XAML 數：X 個
```

---

### Step C — 執行修改（計畫書確認後才執行）

依確認後的計畫書，**逐檔處理，每個 View 完成後確認再繼續下一個**：

1. 在對應的 `.resx` 檔案新增 Key-Value
2. 將 XAML 中的寫死文字改為 `{x:Static}`  binding
3. 確認 Designer 與 Runtime 均正常顯示
4. **不動任何 binding 邏輯**，只替換文字來源



---

## Part 5 — ShowMessage / ErrorCode / 提示文字集中管理

### 背景說明

專案中除了 XAML 靜態文字之外，還有三種動態文字寫死在 C# 程式碼裡：

| 類型 | 說明 | 範例 |
|------|------|------|
| `ShowMessage` | 操作回饋、確認提示 | `"操作完成"` / `"請先執行歸零"` |
| `ErrorCode` 錯誤訊息 | 錯誤代碼對應的說明文字 | `"E001: 超出行程範圍"` |
| 提示文字 | ToolTip、狀態列、Log 訊息 | `"正在連線..."` / `"ATC 動作中"` |

這些文字目前若寫死在 C# 中，未來切換語言時需要逐一翻找修改，風險高。
目標是集中到獨立的 `.resx` 資源檔，透過 `ResourceManager` 統一取用。

---

### Step A — 掃描現有狀態（做完回報，等確認再動手）

**先盤點現有資源：**
1. 確認是否已有 `Messages.resx` 或類似的訊息資源檔
2. 確認是否已有統一的 `ShowMessage` / `MessageBox` 封裝方法
3. 確認 ErrorCode 目前是如何定義與顯示的（enum / const / 直接寫字串）

**再掃描寫死的文字：**

搜尋 `**/*.cs` 與 `**/*.xaml`，找出以下模式：

| 搜尋目標 | 說明 |
|----------|------|
| `MessageBox.Show("` | 直接呼叫 MessageBox |
| `ShowMessage("` | 專案封裝的提示方法 |
| `MessageService` / `DialogService` | 訊息服務呼叫 |
| `throw new Exception("` | Exception 訊息字串 |
| `Logger.Log("` / `Debug.WriteLine("` | Log 中的提示文字 |
| `StatusMessage =` / `StatusText =` | 狀態列文字 binding |
| `ToolTip="` | XAML 中的 ToolTip |
| ErrorCode 相關的 `string` 常數或 `switch/case` 對應文字 | 錯誤訊息對照 |

掃描完成後，**依類型分組**整理成下表**回報，不要動手修改**：

| 檔案 | 類型 | 呼叫方式 | 目前文字內容 | 建議 Key 名稱 | 備註 |
|------|------|----------|--------------|---------------|------|
| （AI 填入） | ShowMessage / ErrorCode / ToolTip / Log | | | | |

同時回報：
- 是否已有統一的訊息封裝機制
- ErrorCode 的定義方式（集中 / 分散）
- 重複出現的文字（可共用同一個 Key）

---

### Step B — 出計畫書（回報後執行）

根據掃描結果，分三塊提出方案：

```
【ShowMessage / 提示文字】
數量：X 個，分布在 X 個檔案
建議資源檔：Messages.resx（與 Strings.resx 分開）
Key 命名規則：Msg_[動作][結果]
例：Msg_AtcLoadSuccess、Msg_HomingRequired
實作方式：ResourceManager.GetString("Msg_xxx") 或封裝成 MessageKeys 常數類別

【ErrorCode 錯誤訊息】
數量：X 個
現有定義方式：（enum / const / 直接字串）
建議資源檔：ErrorMessages.resx
Key 命名規則：E[三位數字碼]
例：E001 → "超出行程範圍"
取用方式：ErrorMessages.ResourceManager.GetString($"E{code:D3}")

【ToolTip / 狀態列文字】
數量：X 個
建議合併到 Messages.resx 或 Strings.resx（視數量決定）
```

最後列出：
- 需新增幾個 `.resx` 檔
- 需修改幾個 `.cs` / `.xaml` 檔
- 是否需要調整現有訊息封裝方法

---

### Step C — 執行修改（計畫書確認後才執行）

依確認後的計畫書，**逐類型處理，每類完成確認再繼續**：

1. 建立（或沿用）對應的 `.resx` 資源檔，加入 Key-Value
2. C# 中的寫死字串改為 `ResourceManager.GetString("Key")` 或常數類別取用
3. XAML 中的 ToolTip 改為 `{x:Static}` binding
4. **不動任何商業邏輯**，只替換文字來源
5. 確認各語系切換後顯示正確


- 若某個元素有特殊顏色需求（語意色、狀態色），加 `<!-- KEEP: reason -->` 保留並說明
- 每個 Part 的計畫書需分開確認，確認一個才執行一個
