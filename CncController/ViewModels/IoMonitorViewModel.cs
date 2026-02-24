using CncController.Models;
using CncController.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncController.ViewModels
{
    // 單個卡片 (對應一個 Slave)
    public partial class ServoIoCard : ObservableObject
    {
        public int SlaveIndex { get; set; }

        // [2026-02-24] 軸名稱（由軸映射決定，例如 "X Axis"）
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        private string _axisName = "";

        // [2026-02-24] 標題：有軸名時顯示軸名，無則只顯示 Slave#
        public string Title => string.IsNullOrEmpty(AxisName)
            ? $"Slave #{SlaveIndex}"
            : $"{AxisName}";

        // 介面顯示用的 Hex 字串 (e.g. "0x6041")
        [ObservableProperty]
        private string _statusHex = "---";

        // 狀態描述 (e.g. "Ready", "Fault")
        [ObservableProperty]
        private string _statusDesc = "Unknown";

        // DI 燈號 (0~15)
        public ObservableCollection<DiBitModel> Inputs { get; } = new ObservableCollection<DiBitModel>();

        public ServoIoCard(int index)
        {
            SlaveIndex = index;
            // 初始化 16 個 DI 燈號
            for (int i = 0; i < 16; i++)
            {
                Inputs.Add(new DiBitModel { Index = i, IsActive = false });
            }
        }

        public void Update(string diRaw, string statusRaw)
        {
            // 防呆：如果是空的 (例如 Slave 1)，直接跳過
            if (string.IsNullOrEmpty(diRaw) || string.IsNullOrEmpty(statusRaw)) return;

            // 1. 解析 Status Word
            try
            {
                uint st = ParseAuto(statusRaw);

                // 更新介面顯示 (統一轉成 Hex 格式，大寫，補零)
                StatusHex = $"0x{st:X4}";

                // 狀態位元判斷 (CiA 402 State Machine)
                // Bit 3 = Fault
                // Bit 2 = Operation Enabled
                // Bit 1 = Switched On
                // Bit 0 = Ready To Switch On
                if ((st & 0x8) != 0) StatusDesc = "FAULT";
                else if ((st & 0x4) != 0) StatusDesc = "Running";
                else if ((st & 0x2) != 0) StatusDesc = "Ready";
                else if ((st & 0x1) != 0) StatusDesc = "SwitchedOn";
                else StatusDesc = "Disable";
            }
            catch { StatusDesc = "ERR"; }

            // 2. 解析 DI Inputs
            try
            {
                uint di = ParseAuto(diRaw);
                for (int i = 0; i < 16; i++)
                {
                    // 檢查第 i 個 bit 是否為 1
                    bool isOn = ((di >> i) & 1) == 1;
                    if (Inputs[i].IsActive != isOn)
                    {
                        Inputs[i].IsActive = isOn;
                    }
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"IO DI parse error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ★★★ [核心] 智慧解析函式：支援 10進位 與 16進位 ★★★
        private uint ParseAuto(string val)
        {
            val = val.Trim();
            if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                // Hex 格式 (移除 0x 後解析)
                return Convert.ToUInt32(val.Substring(2), 16);
            }
            else
            {
                // 十進位格式
                return uint.TryParse(val, out uint result) ? result : 0;
            }
        }
    }

    // 單個燈號
    public partial class DiBitModel : ObservableObject
    {
        public int Index { get; set; }

        [ObservableProperty]
        private bool _isActive;
    }

    // 主 VM
    public partial class IoMonitorViewModel : ObservableObject
    {
        public ObservableCollection<ServoIoCard> Cards { get; } = new ObservableCollection<ServoIoCard>();

        // [2026-02-24] 軸映射：Slave Index → 軸名（僅顯示已映射的 Slave 卡片）
        private Dictionary<int, string> _axisSlaveMap = new();

        // [2026-02-24] 從 AxisMapping 更新過濾條件
        public void UpdateAxisMapping(IEnumerable<AxisMapItem> axisMaps)
        {
            _axisSlaveMap.Clear();

            if (axisMaps != null)
            {
                foreach (var map in axisMaps)
                {
                    if (map.SelectedSlave != null &&
                        !string.IsNullOrEmpty(map.SelectedSlave.Name) &&
                        map.SelectedSlave.Name != "--- None ---")
                    {
                        _axisSlaveMap[map.SelectedSlave.Index] = map.AxisName;
                    }
                }
            }

            // 移除不在映射中的舊卡片
            if (_axisSlaveMap.Count > 0)
            {
                for (int i = Cards.Count - 1; i >= 0; i--)
                {
                    if (!_axisSlaveMap.ContainsKey(Cards[i].SlaveIndex))
                    {
                        Cards.RemoveAt(i);
                    }
                }
            }

            // 更新現有卡片的軸名（AxisName 變更會自動通知 Title 更新）
            foreach (var card in Cards)
            {
                card.AxisName = _axisSlaveMap.TryGetValue(card.SlaveIndex, out var name) ? name : "";
            }
        }

        public void UpdateData(MachineStatusData data)
        {
            if (data?.Servo_IO == null) return;

            foreach (var kvp in data.Servo_IO)
            {
                if (!int.TryParse(kvp.Key, out int slaveIdx)) continue;

                var raw = kvp.Value;
                // 如果該 Slave 沒有數據 (例如您的 Slave 1 是空的 {})，就跳過
                if (raw == null || (string.IsNullOrEmpty(raw.DI) && string.IsNullOrEmpty(raw.Status)))
                    continue;

                // [2026-02-24] 有軸映射時，只顯示已映射的 Slave
                if (_axisSlaveMap.Count > 0 && !_axisSlaveMap.ContainsKey(slaveIdx))
                    continue;

                // 找找看有沒有這張卡，沒有就新增
                var card = Cards.FirstOrDefault(c => c.SlaveIndex == slaveIdx);
                if (card == null)
                {
                    var axisName = _axisSlaveMap.TryGetValue(slaveIdx, out var name) ? name : "";
                    card = new ServoIoCard(slaveIdx) { AxisName = axisName };
                    InsertCardSorted(card);
                }

                card.Update(raw.DI, raw.Status);
            }
        }

        private void InsertCardSorted(ServoIoCard newCard)
        {
            for (int i = 0; i < Cards.Count; i++)
            {
                if (Cards[i].SlaveIndex > newCard.SlaveIndex)
                {
                    Cards.Insert(i, newCard);
                    return;
                }
            }
            Cards.Add(newCard);
        }
    }
}