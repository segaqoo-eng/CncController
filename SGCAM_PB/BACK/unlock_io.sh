#!/bin/bash

# 1. 等待 5 秒，確保 EtherCAT Master 已經啟動且模組進入 OP 狀態
sleep 5

# 2. 顯示訊息 (可在終端機看到)
echo "=== [Unlock Script] Unlocking Delta IO (Slave 13) ==="

# 3. 寫入 0xFF 到 0x2001 (Output Enable)
# 分別解鎖 Port 1~4
ethercat download -p 13 0x2001 0x01 0xFF --type uint8
ethercat download -p 13 0x2001 0x02 0xFF --type uint8
ethercat download -p 13 0x2001 0x03 0xFF --type uint8
ethercat download -p 13 0x2001 0x04 0xFF --type uint8

echo "=== [Unlock Script] Done! IO should be active now. ==="
