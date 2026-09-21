# launch_unity.py — 重启 InkWash 编辑器（完全分离，随父进程退出存活）
import subprocess, sys, os

exe = r"E:\Program Files\Unity\Hub\Editor\2022.3.62f2c1\Editor\Unity.exe"
proj = r"E:\Programs\Project\InkWash"
flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
r = subprocess.Popen([exe, "-projectPath", proj], creationflags=flags,
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     close_fds=True)
print("launched pid", r.pid)
