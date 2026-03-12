# ATC 即時狀態回讀 + 模擬 IO 測試指南

## 前置條件

- `ConfigurationService.IsAtcSimulation = true`（目前預設）
- `ConfigurationService.IsProbeSimulation = true`（目前預設）
- 後端 server.py 運行中（Ubuntu RT 機台 or 本機模擬）

---

## 測試步驟

### 1. 部署模擬 HAL 檔案

1. 進入 **Settings** 頁面
2. 確認 ATC IO 設定正確（ATC IO 分頁，IoSlaveIndex、DO/DI pin 號碼）
3. 按 **UPDATE** 部署設定檔
   - 前端會自動生成 `sim_atc.hal` 並上傳至後端
   - INI 檔會自動加入 `HALFILE = sim_atc.hal`
4. 等待 LinuxCNC 重啟完成

### 2. 驗證 HAL 信號已建立（SSH 進後端機台）

```bash
# 確認 sim_atc.hal 已部署
cat ~/linuxcnc/configs/ethercat/sim_atc.hal

# 確認 DI 信號存在
halcmd show sig atc-di-carousel-home-in
halcmd show sig atc-di-carousel-out-in
halcmd show sig atc-di-drawbar-clamp-in
halcmd show sig atc-di-drawbar-unclamp-in

# 確認信號連接到 motion.digital-in-XX
halcmd show pin motion.digital-in-31
halcmd show pin motion.digital-in-30
halcmd show pin motion.digital-in-29
halcmd show pin motion.digital-in-28
```

### 3. 測試 ATC 頁面感測器顯示

1. 切換到 **ATC** 頁面
2. 右側面板底部應顯示「**感測器狀態**」區塊，包含 4 個 LED 指示燈：
   - 刀盤歸位（Carousel Home）— 灰色 = OFF
   - 刀盤到位（Carousel Out）— 灰色 = OFF
   - 夾刀確認（Drawbar Clamp）— 灰色 = OFF
   - 鬆刀確認（Drawbar Unclamp）— 灰色 = OFF
3. 每個 LED 右側應有 **SIM** 按鈕（僅 `IsAtcSimulation=true` 時顯示）

### 4. 測試 SIM 模擬切換

| 操作 | 預期結果 |
|------|----------|
| 按「刀盤歸位」旁的 SIM | LED 灰→綠，再按一次 綠→灰 |
| 按「刀盤到位」旁的 SIM | LED 灰→綠，再按一次 綠→灰 |
| 按「夾刀確認」旁的 SIM | LED 灰→綠，再按一次 綠→灰 |
| 按「鬆刀確認」旁的 SIM | LED 灰→綠，再按一次 綠→灰 |

**驗證方式**：按 SIM 後，SSH 進機台確認 HAL 信號值已改變：
```bash
halcmd show sig atc-di-drawbar-clamp-in
# 應顯示 TRUE（按一次後）或 FALSE（再按一次後）
```

### 5. 測試 DO 輸出狀態回讀

左側 MANUAL ATC 按鈕已有 DO 狀態回讀（按鈕高亮）：

| 操作 | 預期結果 |
|------|----------|
| 按「夾刀」 | 夾刀按鈕亮藍（IsDrawbarOn=False） |
| 按「鬆刀」 | 鬆刀按鈕亮藍（IsDrawbarOn=True） |
| 按「伸出」 | 伸出按鈕亮藍 |
| 按「收回」 | 收回按鈕亮藍 |

### 6. 測試 DO + DI 聯動模擬（完整換刀流程）

模擬一次完整換刀流程：

1. 按「鬆刀」→ 鬆刀按鈕亮藍
2. 按「鬆刀確認」SIM → 鬆刀確認 LED 亮綠（模擬感測器確認已鬆開）
3. 按「伸出」→ 伸出按鈕亮藍
4. 按「刀盤到位」SIM → 刀盤到位 LED 亮綠
5. 按 FWD/REV 旋轉刀盤到目標刀位
6. 按「收回」→ 收回按鈕亮藍
7. 按「刀盤到位」SIM 關閉 → LED 灰
8. 按「刀盤歸位」SIM → 刀盤歸位 LED 亮綠
9. 按「夾刀」→ 夾刀按鈕亮藍
10. 按「夾刀確認」SIM → 夾刀確認 LED 亮綠
11. 按「鬆刀確認」SIM 關閉 → LED 灰

### 7. 驗證輪詢機制

- ATC 頁面進入時自動開始 1 秒輪詢（`StartPolling`）
- 離開 ATC 頁面後停止輪詢（`StopPolling`）
- 在 SSH 直接用 `halcmd sets` 改信號值，前端應在 1 秒內更新 LED：
```bash
halcmd sets atc-di-drawbar-clamp-in 1
# → 前端夾刀確認 LED 應自動亮綠
halcmd sets atc-di-drawbar-clamp-in 0
# → 前端夾刀確認 LED 應自動變灰
```

---

## 切換到實機模式

當安裝實體感測器後：

1. 修改 `ConfigurationService.cs` line 28：
   ```csharp
   public const bool IsAtcSimulation = false;
   ```
2. 重新 Build + 部署
3. HAL 檔會自動改為接線實體 EtherCAT DI pin：
   ```
   net atc-di-carousel-home-in lcec.0.{slave}.din-{pin} => motion.digital-in-{pin}
   ```
4. SIM 按鈕自動隱藏
5. LED 指示燈改為顯示實體感測器真實狀態

---

## 相關檔案

| 檔案 | 說明 |
|------|------|
| `Services/ConfigurationService.cs` | `IsAtcSimulation` 旗標 + `GenerateSimAtcHal()` |
| `ViewModels/AtcViewModel.cs` | DI 狀態屬性 + `SimToggle*` 命令 + 1s 輪詢 |
| `Views/Pages/AtcView.xaml` | 右側感測器 LED + SIM 按鈕 |
| `Server/server.py` | `/v2/atc/status` 讀取 DO/DI + `sim_atc.hal` 部署 |
| `Resources/Languages/Lang.zh-TW.xaml` | `Str.Atc.SensorStatus` 等 5 個 key |
