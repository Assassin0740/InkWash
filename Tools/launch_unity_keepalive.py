# launch_unity_keepalive.py — 启动 Unity 并驻留，防止沙箱任务结束时子进程被杀
import subprocess, time, os

exe = r"E:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe"
proj = r"E:\Programs\Project\InkWash"

flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
p = subprocess.Popen([exe, "-projectPath", proj], creationflags=flags,
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     close_fds=True)
print("launched pid", p.pid, flush=True)

# 驻留：每 30s 检查一次 Unity 是否还活着；最多驻留 6 小时
t0 = time.time()
while time.time() - t0 < 6 * 3600:
    time.sleep(30)
    if p.poll() is not None:
        print("unity exited rc=", p.returncode, flush=True)
        break
print("keepalive end", flush=True)
