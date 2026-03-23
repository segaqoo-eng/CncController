# 按鈕防呆規則手冊（Button Guard Rules Manual）

> 本文件詳細說明 CNC 控制器的按鈕防呆機制：什麼狀態下哪些按鈕會被禁用（灰色不可按），以及為什麼。
> 適用於：操作員教育訓練、維護人員參考、開發者規格依據。

---

## 1. 防呆機制概述

### 為什麼需要防呆？

| 問題 | 沒有防呆時 | 有防呆後 |
|------|----------|---------|
| 斷線時按 JOG | 按了→等 3 秒→沒反應→困惑 | 按鈕灰色，一看就知道不能操作 |
| 急停時按 Cycle Start | 按了→被後端拒絕→沒任何回饋 | 按鈕灰色+狀態列顯示「EMERGENCY STOP」|
| 加工中按 JOG | 按了→被內部擋住→沒反應 | 按鈕灰色，避免操作員誤觸 |
| 換刀中按探測 | 按了→可能干擾換刀→撞刀 | 按鈕灰色，物理上不可能誤觸 |

### 防呆的運作方式

```
每 50 毫秒（0.05 秒），系統自動檢查：
  ├─ 是否連線？
  ├─ 是否通電？
  ├─ 是否急停？
  ├─ 是否已歸零？
  ├─ 目前模式？（手動/自動/MDI）
  ├─ 是否正在加工？
  ├─ 是否正在探測？
  ├─ 是否正在換刀？
  └─ 操作員權限等級？

  → 計算出 14 個「可以/不可以」的開關
  → 自動灰掉不該操作的按鈕
  → 自動亮起可以操作的按鈕
```

---

## 2. 10 種機台狀態說明

| # | 狀態 | 畫面提示 | 如何解除 |
|---|------|---------|---------|
| ① | **斷線** (Disconnected) | 狀態列紅色「Disconnected from Server」 | 檢查網路線→按 RETRY |
| ② | **急停** (E-Stop) | 狀態列紅色「EMERGENCY STOP ACTIVE」 | 按 E-STOP 按鈕解除→再按 POWER 開機 |
| ③ | **斷電** (Power Off) | 狀態列橘色「Machine Power Off」 | 按 POWER (F1) 開機 |
| ④ | **未歸零** (Not Homed) | REF 按鈕紅色 | 按 REF ALL 或個別 REF X/Y/Z |
| ⑤ | **MDI 執行中** | — | 等待 MDI 指令完成 |
| ⑥ | **探測中** (Probing) | 狀態文字「探測中...」+ STOP 按鈕出現 | 等待探測完成 或 按 STOP/ESC 中斷 |
| ⑦ | **加工中** (Running) | CYCLE START 按鈕綠色 | 等待完成 或 按 FEED HOLD/STOP |
| ⑧ | **暫停中** (Paused) | FEED HOLD 按鈕橘色 | 按 CYCLE START 繼續 或 按 STOP 放棄 |
| ⑨ | **ATC 換刀中** | ATC 頁面忙碌 | 等待換刀完成 |
| ⑩ | **無程式載入** | 檔名顯示「No File Loaded」| 載入 G-Code 程式 |

---

## 3. 完整防呆矩陣

### 圖例
- ✅ = 可以按（按鈕亮起）
- 🚫 = 不可按（按鈕灰色）
- ✅* = 有條件（見備註）

### 3.1 主頁面按鈕

```
                  正常  斷線  急停  斷電  未歸零  加工中  暫停  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬─────┬────┬─────┬─────┐
 JOG 方向按鈕    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 REF / HOME     │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 CYCLE START    │ ✅*│ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ ✅ │ 🚫  │ 🚫  │
 FEED HOLD      │ 🚫 │ 🚫 │ ── │ ── │  ──  │ ✅  │ 🚫 │ ──  │ ──  │
 STOP           │ 🚫 │ 🚫 │ ── │ ── │  ──  │ ✅  │ ✅ │ ──  │ ──  │
 E-STOP (F2)    │ ✅ │ ✅ │ ✅ │ ✅ │  ✅  │ ✅  │ ✅ │ ✅  │ ✅  │
 POWER (F1)     │ ✅ │ 🚫 │ ✅*│ ✅ │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 ESC 全機停止    │ ✅ │ ✅ │ ✅ │ ✅ │  ✅  │ ✅  │ ✅ │ ✅  │ ✅  │
                └────┴────┴────┴────┴─────┴─────┴────┴─────┴─────┘

  ✅* CYCLE START：需要已歸零 + 已載入程式
  ✅* POWER (急停時)：可以按，用來解除急停後重新開機
  ── = 該狀態下此按鈕不影響（保持前一狀態）
```

### 3.2 底部控制區

```
                  正常  斷線  急停  斷電  未歸零  加工中  暫停  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬─────┬────┬─────┬─────┐
 主軸 FWD/REV   │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 主軸 STOP      │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 JOG 模式按鈕   │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 JOG 速度滑桿   │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 SINGLE BLOCK   │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 FLOOD / MIST   │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 BLOCK DELETE    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 M01 BREAK      │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 Override 滑桿   │ ✅ │ 🚫 │ ✅ │ ✅ │  ✅  │ ✅  │ ✅ │ ✅  │ ✅  │
 Override 重置   │ ✅ │ 🚫 │ ✅ │ ✅ │  ✅  │ ✅  │ ✅ │ ✅  │ ✅  │
                └────┴────┴────┴────┴─────┴─────┴────┴─────┴─────┘
```

### 3.3 DRO 座標區

```
                  正常  斷線  急停  斷電  未歸零  加工中  暫停  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬─────┬────┬─────┬─────┐
 ZERO 按鈕      │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 ZERO ALL       │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 REF X/Y/Z      │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 REF ALL        │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
                └────┴────┴────┴────┴─────┴─────┴────┴─────┴─────┘
```

### 3.4 MONITOR 頁面

```
                  正常  斷線  急停  斷電  加工中  暫停  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬────┬─────┬─────┐
 MDI SEND       │ ✅ │ 🚫 │ 🚫 │ 🚫 │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 程式上傳 SAVE  │ ✅ │ 🚫 │ ✅ │ ✅ │ 🚫  │ 🚫 │ ✅  │ ✅  │
 開啟本機檔案   │ ✅ │ ✅ │ ✅ │ ✅ │ ✅  │ ✅ │ ✅  │ ✅  │
                └────┴────┴────┴────┴─────┴────┴─────┴─────┘
```

### 3.5 OFFSETS 頁面

```
                  正常  斷線  急停  斷電  加工中  暫停  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬────┬─────┬─────┐
 SET TO ZERO    │ ✅ │ 🚫 │ 🚫 │ 🚫 │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 CLEAR / SAVE   │ ✅ │ 🚫 │ 🚫 │ 🚫 │ 🚫  │ 🚫 │ 🚫  │ 🚫  │
 RELOAD（讀取）  │ ✅ │ ✅ │ ✅ │ ✅ │ ✅  │ ✅ │ ✅  │ ✅  │
                └────┴────┴────┴────┴─────┴────┴─────┴─────┘
```

### 3.6 TOOL TABLE 頁面

```
                  正常  斷線  急停  斷電  未歸零  加工中  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬─────┬─────┬─────┐
 SAVE TABLE     │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫  │ 🚫  │
 Load/Unload    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 M6 G43         │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 Tool Setter    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 Add/Delete     │ ✅ │ ✅ │ ✅ │ ✅ │  ✅  │ ✅  │ ✅  │ ✅  │
                └────┴────┴────┴────┴─────┴─────┴─────┴─────┘
```

### 3.7 ATC 頁面

```
                  正常  斷線  急停  斷電  未歸零  加工中  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬─────┬─────┬─────┐
 全部手動按鈕    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 全部自動按鈕    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 刀位設定       │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 MDI SEND      │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫  │ 🚫  │
 SIM 模擬按鈕   │ ✅ │ ✅ │ ✅ │ ✅ │  ✅  │ ✅  │ ✅  │ ✅  │
                └────┴────┴────┴────┴─────┴─────┴─────┴─────┘
```

### 3.8 PROBING 頁面

```
                  正常  斷線  急停  斷電  未歸零  加工中  探測中  換刀中
                ┌────┬────┬────┬────┬─────┬─────┬─────┬─────┐
 全部探測按鈕    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 (39 個方向鈕)  │    │    │    │    │     │     │     │     │
 Tool Setter    │ ✅ │ 🚫 │ 🚫 │ 🚫 │  🚫  │ 🚫  │ 🚫  │ 🚫  │
 MDI SEND      │ ✅ │ 🚫 │ 🚫 │ 🚫 │  ✅  │ 🚫  │ 🚫  │ 🚫  │
 STOP PROBE    │ ── │ ── │ ── │ ── │  ──  │ ──  │ ✅  │ ──  │
 SIM TRIGGER   │ ✅ │ ✅ │ ✅ │ ✅ │  ✅  │ ✅  │ ✅  │ ✅  │
                └────┴────┴────┴────┴─────┴─────┴─────┴─────┘
```

### 3.9 SETTINGS 頁面

```
                  正常  斷線  加工中  探測中  換刀中  權限不足
                ┌────┬────┬─────┬─────┬─────┬───────┐
 SCAN（掃描）    │ ✅ │ 🚫 │  ✅  │  ✅  │  ✅  │  ✅   │
 UPDATE 部署     │ ✅ │ 🚫 │  🚫  │  🚫  │  🚫  │  🚫   │
 RESTART 重啟    │ ✅ │ 🚫 │  🚫  │  🚫  │  🚫  │  🚫   │
 Backup 建立     │ ✅ │ 🚫 │  🚫  │  🚫  │  🚫  │  🚫   │
 Backup 還原     │ ✅ │ 🚫 │  🚫  │  🚫  │  🚫  │  🚫   │
 Backup 刪除     │ ✅ │ 🚫 │  🚫  │  🚫  │  🚫  │  🚫   │
                └────┴────┴─────┴─────┴─────┴───────┘
```

---

## 4. 14 個防呆屬性詳細規則

### 4.1 CanJog — JOG 手動移動

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |

**不需要歸零。** 未歸零時可用 Joint 模式 JOG 移軸去尋找 Home 開關。

**影響按鈕：** JOG X±/Y±/Z±/A±/B±/C±、JOG 模式切換（JOG/0.1/0.01/0.001）、JOG 速度滑桿

---

### 4.2 CanHome — 原點復歸

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |

**影響按鈕：** REF ALL、REF X/Y/Z/A/B/C

---

### 4.3 CanCycleStart — 開始加工

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |
| **All Homed** | **必須已歸零**（未歸零加工座標不正確→撞刀風險） |
| **Has Program OR Paused** | 必須已載入程式，或在暫停狀態（可 Resume） |

**影響按鈕：** CYCLE START

---

### 4.4 CanFeedHold — 暫停進給

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| **Running** | **只在加工中才有意義** |

**影響按鈕：** FEED HOLD

---

### 4.5 CanStop — 停止加工

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| **Running OR Paused** | 正在加工或已暫停才能停止 |

**影響按鈕：** STOP

---

### 4.6 CanMdi — MDI 手動指令

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |

**影響按鈕：** 所有頁面的 MDI SEND、MAN/AUTO/MDI 模式切換、SINGLE BLOCK、FLOOD、MIST、BLOCK DELETE、M01 BREAK

---

### 4.7 CanProbe — 探測操作

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT ATC Busy | 不在換刀 |
| **All Homed** | **必須已歸零**（探測需要精確座標） |

**影響按鈕：** 探測 39 個方向按鈕、Tool Setter 按鈕

---

### 4.8 CanAtc — ATC 換刀操作

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT Probing | 不在探測 |
| **All Homed** | **必須已歸零**（換刀需要精確 Z 高度，否則撞刀） |

**影響按鈕：** ATC 全部手動/自動操作按鈕、刀位設定、ToolTable 的 Load/Unload/M6 G43/Tool Setter

---

### 4.9 CanSpindle — 主軸控制

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Running | 不在加工（加工中主軸由程式控制） |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |

**影響按鈕：** FWD / REV / STOP

---

### 4.10 CanEditOffset — 座標偏移編輯

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| Power ON | 必須通電 |
| NOT E-Stop | 不在急停 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |

**影響按鈕：** Offset SET TO ZERO/CLEAR/SAVE、DRO ZERO 按鈕、ToolTable SAVE

---

### 4.11 CanUpload — 程式上傳

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| NOT Running | 不在加工（防止覆蓋正在執行的程式） |
| NOT Paused | 不在暫停 |

**影響按鈕：** Monitor SAVE/UPLOAD、RELOAD PGM、CLEAR PGM

---

### 4.12 CanDeploy — 設定部署 🔒

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |
| **Role ≥ Engineer** | **需要工程師以上權限** |

**影響按鈕：** Settings UPDATE、GENERATE & RESTART

---

### 4.13 CanBackup — 備份還原 🔒

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |
| NOT Running | 不在加工 |
| NOT Paused | 不在暫停 |
| NOT Probing | 不在探測 |
| NOT ATC Busy | 不在換刀 |
| **Role ≥ Admin** | **需要管理員以上權限** |

**影響按鈕：** Backup CREATE、RESTORE、DELETE

---

### 4.14 CanOverride — 倍率調整

| 條件 | 說明 |
|------|------|
| Connected | 必須連線 |

**最寬鬆的規則。** 加工中也需要調整倍率（加減速），所以只要連線就可以操作。

**影響按鈕：** V/F/S/R 滑桿 + 重置按鈕

---

## 5. 權限對照表

| 功能 | Operator | Engineer | Admin | Developer |
|------|:--------:|:--------:|:-----:|:---------:|
| JOG / Home / Cycle Start | ✅ | ✅ | ✅ | ✅ |
| MDI / Probe / ATC | ✅ | ✅ | ✅ | ✅ |
| 主軸 / Override / Offset | ✅ | ✅ | ✅ | ✅ |
| 程式上傳 | ✅ | ✅ | ✅ | ✅ |
| **設定部署** (UPDATE/RESTART) | 🚫 | ✅ | ✅ | ✅ |
| **備份還原** (Backup) | 🚫 | 🚫 | ✅ | ✅ |
| **使用者管理** | 🚫 | 🚫 | ✅ | ✅ |

---

## 6. 永遠可按的按鈕（不受防呆限制）

以下按鈕在**任何狀態下都可以按**，包括斷線、急停、加工中：

| 按鈕 | 原因 |
|------|------|
| **E-STOP (F2)** | 最高優先級安全按鈕，永遠不鎖 |
| **ESC 全機停止** | 緊急中斷一切動作 |
| **Override 滑桿** | 加工中需要即時調整速度 |
| **SIM 模擬按鈕** | 模擬測試用，不影響實際動作 |
| **SCAN 掃描按鈕** | 唯讀操作，不影響機台 |
| **RELOAD（讀取）** | 唯讀操作 |
| **開啟本機檔案** | 本機操作，不經後端 |
| **刀具表 Add/Delete** | 純表格操作（需另按 SAVE 才生效） |

---

## 7. 操作員常見情境 Q&A

### Q: 按鈕突然全部變灰了？
**A:** 最可能的原因（按順序檢查）：
1. **網路斷線** → 看狀態列是否顯示「Disconnected」→ 按 RETRY
2. **急停觸發** → 看狀態列是否顯示「EMERGENCY STOP」→ 按 E-STOP 解除
3. **機台斷電** → 看狀態列是否顯示「Machine Power Off」→ 按 POWER (F1)

### Q: CYCLE START 按不了（灰色）？
**A:** 檢查以下條件（都要滿足）：
1. 連線正常（狀態列綠色）
2. 機台通電（POWER 亮綠色）
3. 非急停狀態
4. **所有軸已歸零**（REF 按鈕全部綠色）
5. **已載入 G-Code 程式**（檔名不是「No File Loaded」）
6. 沒有探測或換刀正在進行

### Q: 探測按鈕按不了？
**A:** 除了基本條件外，探測額外要求：
- **所有軸必須已歸零**（探測需要精確座標）
- **不在換刀中**（ATC 和探測互鎖）

### Q: ATC 換刀按鈕按不了？
**A:** 除了基本條件外，換刀額外要求：
- **所有軸必須已歸零**（Z 軸不歸零，換刀高度會錯→撞刀）
- **不在探測中**（探測和 ATC 互鎖）

### Q: 設定 UPDATE 按鈕按不了？
**A:** 兩個額外條件：
1. **權限不足** → 目前登入的是 Operator，需要 Engineer 以上
2. **機台忙碌** → 加工中/探測中/換刀中不能部署設定

### Q: 加工中哪些按鈕還能用？
**A：**
- ✅ **FEED HOLD** — 暫停
- ✅ **STOP** — 全停
- ✅ **E-STOP / ESC** — 緊急停止
- ✅ **Override 滑桿** — 即時調速
- 🚫 其他全部鎖定（防止干擾加工）

---

## 8. 技術實作摘要（開發者參考）

### 架構

```
MainViewModel.cs (Core)
  ├─ 14 個 [ObservableProperty] CanXxx
  └─ UpdateCanExecuteStates() ← 每 50ms 由 Polling 呼叫

MainViewModel.Polling.cs
  └─ StatusTimer_Tick() → UpdateCanExecuteStates()

XAML IsEnabled 綁定
  ├─ 同 DataContext：IsEnabled="{Binding CanXxx}"
  └─ 跨 DataContext：IsEnabled="{Binding DataContext.CanXxx,
       RelativeSource={RelativeSource AncestorType=Window}}"
```

### 涉及檔案

| 檔案 | 修改內容 |
|------|---------|
| `ViewModels/MainViewModel.cs` | 14 個 CanXxx 屬性 + UpdateCanExecuteStates() |
| `ViewModels/MainViewModel.Polling.cs` | 呼叫 UpdateCanExecuteStates() |
| `Views/Layouts/JogPanel.xaml` | 15 個 IsEnabled 綁定 |
| `Views/Components/DroDisplay.xaml` | 14 個 IsEnabled 綁定 |
| `Views/Components/CycleControl.xaml` | 10 個 IsEnabled 綁定 |
| `Views/Components/JogConfig.xaml` | 9 個 IsEnabled 綁定 |
| `Views/Components/SliderControl.xaml` | 8 個 IsEnabled 綁定 |
| `Views/Pages/ProbingView.xaml` | 5 個 IsEnabled 綁定 + Style DataTrigger |
| `Views/Pages/AtcView.xaml` | ~25 個 IsEnabled 綁定 |
| `Views/Pages/MonitorView.xaml` | 2 個 IsEnabled 綁定 |
| `Views/Pages/OffsetsView.xaml` | ~10 個 IsEnabled 綁定 |
| `Views/Pages/ToolTableView.xaml` | ~7 個 IsEnabled 綁定 |
| `Views/Pages/SettingsView.xaml` | 5 個 IsEnabled 綁定 |
