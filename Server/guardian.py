import subprocess
import time
import sys
import os
import signal

# 設定工作目錄與 Server 腳本
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SERVER_SCRIPT = os.path.join(BASE_DIR, "server.py")

# 定義特殊的退出代碼，代表「請求重啟」
RESTART_EXIT_CODE = 42

server_process = None

def signal_handler(sig, frame):
    """ 處理系統關閉訊號 (如 sudo systemctl stop cnc) """
    global server_process
    print(f"[GUARDIAN] Received signal {sig}. Shutting down server...")
    if server_process:
        server_process.terminate() # 優雅關閉子進程
        try:
            server_process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            server_process.kill() # 強制關閉
    sys.exit(0)

# 註冊訊號監聽
signal.signal(signal.SIGTERM, signal_handler)
signal.signal(signal.SIGINT, signal_handler)

def main():
    global server_process
    print("[GUARDIAN] Starting CNC Server Supervisor...")
    
    while True:
        print(f"[GUARDIAN] Launching server: {SERVER_SCRIPT}")
        
        # 啟動 server.py
        # 使用 sys.executable 確保用同一個 Python 解譯器執行
        server_process = subprocess.Popen([sys.executable, SERVER_SCRIPT])
        
        # 阻塞在此，直到 server.py 結束
        exit_code = server_process.wait()
        
        print(f"[GUARDIAN] Server exited with code: {exit_code}")
        
        # 判斷退出原因
        if exit_code == RESTART_EXIT_CODE:
            print("[GUARDIAN] Restart requested via API. Rebooting in 2 seconds...")
            time.sleep(2) # 緩衝時間，確保資源釋放
            continue # 重跑迴圈 -> 重啟
            
        elif exit_code == 0:
            print("[GUARDIAN] Server shut down normally. Exiting guardian.")
            break # 正常結束 -> 守護者也下班
            
        else:
            # 異常崩潰 (Exit Code 1 等)
            print(f"[GUARDIAN] Server crashed! Restarting in 5 seconds...")
            time.sleep(5)
            continue

if __name__ == "__main__":
    main()
