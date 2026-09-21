# launch_unity_clean.py — 清除沙箱代理环境变量后启动 Unity（GUI 模式），并驻留防杀
import subprocess, time, os

exe = r"E:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe"
proj = r"E:\Programs\Project\InkWash"

env = dict(os.environ)
for k in ["HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy", "ALL_PROXY", "all_proxy"]:
    env.pop(k, None)
env["NO_PROXY"] = "*"

flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
p = subprocess.Popen([exe, "-projectPath", proj],
                     creationflags=flags, env=env,
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     close_fds=True)
print("gui launched pid", p.pid, flush=True)

t0 = time.time()
while time.time() - t0 < 6 * 3600:
    time.sleep(30)
    if p.poll() is not None:
        print("unity exited rc=", p.returncode, flush=True)
        break
print("keepalive end", flush=True)
