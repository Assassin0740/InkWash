# launch_unity_batch.py — 批处理模式导入（自动跳过模态框），完成后自然退出
import subprocess, time, os

exe = r"E:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe"
proj = r"E:\Programs\Project\InkWash"
log = r"E:\Programs\Project\InkWash\Temp\editor_batch_relaunch.log"

flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
p = subprocess.Popen([exe, "-batchmode", "-quit", "-projectPath", proj, "-logFile", log],
                     creationflags=flags, stdin=subprocess.DEVNULL,
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, close_fds=True)
print("batch launched pid", p.pid, flush=True)

t0 = time.time()
while time.time() - t0 < 6 * 3600:
    time.sleep(20)
    if p.poll() is not None:
        print("batch exited rc=", p.returncode, "after", int(time.time() - t0), "s", flush=True)
        break
print("wrapper end", flush=True)
