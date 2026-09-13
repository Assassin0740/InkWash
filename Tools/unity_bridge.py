"""Portable command-line / importable client for the Unity Codely TCP Bridge.

Drop this file into the root of any Unity project that has the
`cn.tuanjie.codely.bridge` package installed, then:

    python unity_bridge.py doctor
    python unity_bridge.py scene_info
    python unity_bridge.py execute_csharp 'Debug.Log("hi");'

Dependencies: NONE (Python 3.7+ standard library only).

Port discovery order (first hit wins):
  1. $UNITY_BRIDGE_PORT                       (explicit override)
  2. <ProjectRoot>/Temp/.com-unity-codely.json   (bridge >= 1.0.80 heartbeat file)
  3. <ProjectRoot>/.com-unity-codely.json        (legacy location)
  4. Unity Editor.log scan                       (regex, newest match first)

Every candidate is TCP-probed before it is used, because a stale entry in the
heartbeat file is common (unity_port: -1 after a domain reload / package update).
"""

import os
import sys
import json
import socket
import struct
import re
import time

if sys.stdout.encoding != 'utf-8':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

# --------------------------------------------------------------------------
# Paths
# --------------------------------------------------------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# Walk up a few levels so the script also works from <root>/tools, <root>/docs, etc.
def _guess_project_root():
    d = SCRIPT_DIR
    for _ in range(4):
        if os.path.isdir(os.path.join(d, "Assets")) and os.path.isdir(os.path.join(d, "ProjectSettings")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    return SCRIPT_DIR

PROJECT_ROOT = os.environ.get("UNITY_PROJECT_ROOT") or _guess_project_root()

# Bridge >= 1.0.80 writes the heartbeat here; older versions used the project root.
CONFIG_CANDIDATES = (
    os.path.join(PROJECT_ROOT, "Temp", ".com-unity-codely.json"),
    os.path.join(PROJECT_ROOT, ".com-unity-codely.json"),
    os.path.join(SCRIPT_DIR, ".com-unity-codely.json"),
)

MAX_RESPONSE_BYTES = 64 * 1024 * 1024  # must stay <= bridge MaxFrameBytes (64 MiB)


def _default_editor_log():
    if sys.platform == "win32":
        return os.path.join(os.environ.get("LOCALAPPDATA", ""), "Unity", "Editor", "Editor.log")
    if sys.platform == "darwin":
        return os.path.expanduser("~/Library/Logs/Unity/Editor.log")
    # Linux: location varies by distro / install, list both.
    return os.path.expanduser("~/.config/unity3d/Editor.log")


EDITOR_LOG_CANDIDATES = tuple(
    p for p in (
        os.environ.get("UNITY_EDITOR_LOG"),
        _default_editor_log(),
        os.path.expanduser("~/.local/share/unity3d/Editor.log"),
    ) if p
)


# --------------------------------------------------------------------------
# Port discovery
# --------------------------------------------------------------------------

def _valid_port(value):
    return isinstance(value, int) and 1 <= value <= 65535


def _read_json(path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def _read_config():
    """Return (data, path). Prefers the newest heartbeat file that has a port."""
    fallback = ({}, None)
    for path in CONFIG_CANDIDATES:
        data = _read_json(path)
        if not data:
            continue
        if _valid_port(data.get("unity_port")):
            return data, path
        fallback = (data, path)
    return fallback


def _ports_from_editor_log():
    """Return newest Codely Bridge ports first (log is scanned newest-line-first)."""
    ports = []
    for log_path in EDITOR_LOG_CANDIDATES:
        try:
            with open(log_path, "rb") as f:
                f.seek(0, os.SEEK_END)
                size = f.tell()
                # Keep enough history to survive import/compile log storms.
                f.seek(max(0, size - 16 * 1024 * 1024))
                text = f.read().decode("utf-8", errors="ignore")
        except OSError:
            continue

        patterns = (
            r"NativeTcpBridge(?: started| running)? on port (\d{1,5})",
            r"Codely-Bridge.*?port (\d{1,5})",
        )
        matches = []
        for pattern in patterns:
            matches.extend(int(p) for p in re.findall(pattern, text, flags=re.IGNORECASE))
        ports.extend(p for p in reversed(matches) if _valid_port(p))
    return list(dict.fromkeys(ports))


def _port_is_open(port, timeout=0.75):
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=timeout):
            return True
    except OSError:
        return False


def get_unity_port(timeout=10):
    env_port = os.environ.get("UNITY_BRIDGE_PORT")
    if env_port and env_port.isdigit() and _valid_port(int(env_port)):
        return int(env_port)

    deadline = time.monotonic() + timeout
    reason = "心跳文件不可用"
    discovered = set()
    while time.monotonic() < deadline:
        data, _ = _read_config()
        reason = data.get("reason", reason)
        candidates = [data.get("unity_port"), *_ports_from_editor_log()]
        tried = set()
        for port in candidates:
            if not _valid_port(port) or port in tried:
                continue
            tried.add(port)
            discovered.add(port)
            if _port_is_open(port):
                return port
        time.sleep(0.5)
        # The same native port may reopen after a domain reload: retry it.
    raise ConnectionError(
        f"无法连接 Unity Codely Bridge (reason: {reason})。"
        f"已检查心跳文件和 Editor.log 中的端口: {sorted(discovered) or '无'}"
    )


# --------------------------------------------------------------------------
# Wire protocol
# --------------------------------------------------------------------------

def _recv_exact(sock, size):
    chunks = []
    received = 0
    while received < size:
        chunk = sock.recv(size - received)
        if not chunk:
            raise ConnectionError(f"连接提前关闭：期望 {size} 字节，实际收到 {received} 字节")
        chunks.append(chunk)
        received += len(chunk)
    return b"".join(chunks)


def send_tcp_command(payload_dict, host=None, port=None, timeout=15):
    """Send one command and return the decoded response dict.

    Wire format (both directions): 8-byte big-endian length prefix + UTF-8 JSON.
    """
    host = host or os.environ.get("UNITY_BRIDGE_HOST", "127.0.0.1")
    if port is None:
        port = get_unity_port(timeout=5)

    payload_bytes = json.dumps(payload_dict, ensure_ascii=False).encode("utf-8")
    header = struct.pack(">Q", len(payload_bytes))

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(timeout)
        s.connect((host, port))

        # The server sends a one-line banner on connect; read it before framing.
        banner = bytearray()
        while True:
            ch = s.recv(1)
            if not ch or ch == b'\n':
                break
            banner.extend(ch)

        s.sendall(header + payload_bytes)

        resp_len = struct.unpack(">Q", _recv_exact(s, 8))[0]
        if resp_len > MAX_RESPONSE_BYTES:
            raise ValueError(f"Unity 响应过大：{resp_len} 字节（上限 {MAX_RESPONSE_BYTES}）")

        resp_data = _recv_exact(s, resp_len).decode("utf-8", errors="replace")
        try:
            return json.loads(resp_data)
        except ValueError as exc:
            return {"success": False, "error": f"Unity 返回了无效 JSON: {exc}", "raw_response": resp_data}


def print_result(res):
    print(json.dumps(res, indent=2, ensure_ascii=False))


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------

def main():
    if len(sys.argv) < 2:
        print(__doc__)
        print("用法: python unity_bridge.py <doctor|port|refresh|read_console|clear_console|"
              "execute_csharp|scene_info|screenshot|dump_tree|find_object|play|pause|stop|json>")
        return

    cmd = sys.argv[1].lower()

    if cmd == "doctor":
        config, config_path = _read_config()
        log_ports = _ports_from_editor_log()
        try:
            selected = get_unity_port(timeout=3)
            status = "connected"
        except Exception as exc:
            selected = None
            status = str(exc)
        print_result({
            "status": status,
            "selected_port": selected,
            "project_root": PROJECT_ROOT,
            "heartbeat_file": config_path,
            "configured_port": config.get("unity_port"),
            "config_reason": config.get("reason"),
            "last_heartbeat": config.get("last_heartbeat"),
            "editor_logs": list(EDITOR_LOG_CANDIDATES),
            "log_ports": log_ports,
        })
        return

    if cmd == "port":
        print(get_unity_port())
        return

    if cmd == "refresh":
        res = send_tcp_command({"type": "execute_csharp_script", "params": {
            "script": "UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate "
                      "| UnityEditor.ImportAssetOptions.ForceSynchronousImport); return \"ASSET_DATABASE_REFRESHED\";"
        }}, timeout=30)
    elif cmd == "clear_console":
        res = send_tcp_command({"type": "read_console", "params": {"action": "clear"}})
    elif cmd == "scene_info":
        res = send_tcp_command({"type": "manage_scene", "params": {"action": "get_hierarchy"}})
    elif cmd == "read_console":
        count = int(sys.argv[2]) if len(sys.argv) > 2 else 20
        res = send_tcp_command({"type": "read_console", "params": {"count": count}})
    elif cmd == "screenshot":
        out_path = sys.argv[2] if len(sys.argv) > 2 else "screenshot.png"
        res = send_tcp_command({"type": "manage_screenshot", "params": {
            "action": "capture", "savePath": os.path.abspath(out_path)}})
    elif cmd == "execute_csharp":
        code = sys.argv[2] if len(sys.argv) > 2 else 'Debug.Log("Hello Unity!");'
        res = send_tcp_command({"type": "execute_csharp_script", "params": {"script": code}})
    elif cmd == "dump_tree":
        root_name = sys.argv[2] if len(sys.argv) > 2 else "Canvas"
        depth = int(sys.argv[3]) if len(sys.argv) > 3 else 3
        csharp_dump = """
var sb = new System.Text.StringBuilder();
void Dump(Transform t, string indent, int d, int maxD) {
    if (t == null || d > maxD) return;
    string active = t.gameObject.activeInHierarchy ? "[on]" : "[off]";
    var comps = new System.Collections.Generic.List<string>();
    foreach (var c in t.GetComponents<Component>()) {
        if (c == null) { comps.Add("Missing"); continue; }
        string cn = c.GetType().Name;
        if (cn == "Transform" || cn == "RectTransform") continue;
        comps.Add(cn);
    }
    string compStr = comps.Count > 0 ? " [" + string.Join(", ", comps) + "]" : "";
    var rt = t as RectTransform;
    string rectStr = rt != null ? " (pos: " + rt.anchoredPosition + ", size: " + rt.rect.width.ToString("F0") + "x" + rt.rect.height.ToString("F0") + ")" : "";
    sb.AppendLine(indent + "|-- " + active + " " + t.name + compStr + rectStr);
    for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), indent + "|   ", d + 1, maxD);
}
var target = GameObject.Find(\"""" + root_name + """\");
if (target != null) Dump(target.transform, "", 0, """ + str(depth) + """);
else {
    var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
    foreach (var r in roots) Dump(r.transform, "", 0, """ + str(depth) + """);
}
Debug.Log(sb.ToString());
"""
        res = send_tcp_command({"type": "execute_csharp_script", "params": {"script": csharp_dump}})
        logs = res.get("data", {}).get("data", {}).get("logs")
        if logs:
            for log in logs:
                print(log)
            return
    elif cmd == "find_object":
        target_name = sys.argv[2] if len(sys.argv) > 2 else "MainUIManager"
        csharp_find = """
var gos = Object.FindObjectsOfType<GameObject>(true);
var sb = new System.Text.StringBuilder();
sb.AppendLine("=== 查找结果 '""" + target_name + """' ===");
foreach (var go in gos) {
    if (go.name.IndexOf(\"""" + target_name + """\", System.StringComparison.OrdinalIgnoreCase) >= 0) {
        sb.AppendLine("目标: " + go.name + " (Active: " + go.activeInHierarchy + ")");
        sb.AppendLine("   路径: " + GetPath(go.transform));
        var comps = go.GetComponents<Component>();
        sb.AppendLine("   组件数: " + comps.Length);
        foreach (var c in comps) {
            if (c == null) sb.AppendLine("     - Missing Component!");
            else sb.AppendLine("     - " + c.GetType().FullName);
        }
    }
}
string GetPath(Transform t) {
    if (t.parent == null) return t.name;
    return GetPath(t.parent) + "/" + t.name;
}
Debug.Log(sb.ToString());
"""
        res = send_tcp_command({"type": "execute_csharp_script", "params": {"script": csharp_find}})
        logs = res.get("data", {}).get("data", {}).get("logs")
        if logs:
            for log in logs:
                print(log)
            return
    elif cmd == "play":
        res = send_tcp_command({"type": "manage_editor", "params": {"action": "play"}})
    elif cmd == "pause":
        res = send_tcp_command({"type": "manage_editor", "params": {"action": "pause"}})
    elif cmd == "stop":
        res = send_tcp_command({"type": "manage_editor", "params": {"action": "stop"}})
    elif cmd == "json":
        res = send_tcp_command(json.loads(sys.argv[2]))
    else:
        print(f"未知快捷命令: {cmd}，尝试作为原始 JSON 解析...")
        try:
            res = send_tcp_command(json.loads(cmd))
        except Exception as e:
            print(f"JSON 解析失败: {e}")
            return

    print_result(res)


if __name__ == "__main__":
    main()
