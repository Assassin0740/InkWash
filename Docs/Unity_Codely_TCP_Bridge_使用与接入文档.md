# Unity Codely TCP Bridge 使用与接入文档

> 配套文件：`docs/unity_bridge_client/unity_bridge.py`（可直接拷到新项目的便携版客户端）
> 本文基于本仓库实际版本：`cn.tuanjie.codely.bridge@1.0.81` + `test_tcp.py`
> 编写日期：2026-09-13

---

## 1. 这东西是什么

`test_tcp.py` 是一个**纯 Python 命令行客户端**，用来从外部（终端 / CI / AI Agent）远程驱动
**正在运行的 Unity 编辑器**：执行 C# 片段、读 Console 日志、抓 Hierarchy、截图、进入/退出 Play
模式等。

它连的不是 Unity 官方服务，而是一个第三方 UPM 包 `cn.tuanjie.codely.bridge`
（团结 Codely Bridge），该包在 Unity 启动时拉起一个本地 TCP 服务（`NativeTcpBridge.dll`），
对外暴露一套 JSON 命令协议。

```
┌────────────────────┐        TCP 127.0.0.1:<动态端口>       ┌──────────────────────────────┐
│  test_tcp.py       │  ──8字节长度头 + UTF-8 JSON 请求──▶   │  Unity Editor (运行中)        │
│  (Python 3, 零依赖) │                                      │  cn.tuanjie.codely.bridge    │
│                    │  ◀──8字节长度头 + UTF-8 JSON 响应──   │   └ NativeTcpBridge.dll      │
└────────────────────┘                                      │      └ 命令分发 → 各 Tool    │
                                                            │         (manage_scene / …)  │
                                                            └──────────────────────────────┘
```

关键点：**Unity 必须处于打开状态**（编辑器在前台/后台都行，但进程必须活着，且包未被手动停止）。
脚本本身不启动 Unity，也不包含任何 Unity 端代码。

---

## 2. 需要下载/安装什么

### 2.1 Python 侧：**什么都不用装**

`test_tcp.py` 只用到标准库：`os / sys / json / socket / struct / re / time`。

- 不需要 `pip install` 任何包（没有 requests、没有 websocket-client、没有 protobuf）
- 要求 **Python 3.7+**（用到了 `sys.stdout.reconfigure`；3.6 及以下会走到 `except` 兜底，
  只是中文输出可能乱码，功能不受影响）
- 建议 Python 3.9+ 以获得稳定的 UTF-8 输出

验证：`python -c "import socket,struct,json; print('ok')"` 能过就够了。

### 2.2 Unity 侧：安装 `cn.tuanjie.codely.bridge` 包

这是**唯一的外部依赖**。本仓库 `Packages/manifest.json` 里的声明：

```json
"dependencies": {
  "cn.tuanjie.codely.bridge": "1.0.81"
}
```

它的来源（`Packages/packages-lock.json`）：

```json
"source": "registry",
"url": "https://packages.unity.cn"
```

> ⚠️ **注意**：`packages.unity.cn` 是 **Unity 中国版 / 团结引擎的默认主 Registry**。
> 如果你的另一个项目用的是**国际版 Unity**（主源是 `packages.unity.com`），直接写这一行会
> **解析失败**。解决办法见下面三种安装方式。

该包自带的传递依赖（通常无需手动加）：

| 依赖 | 版本 | 说明 |
|---|---|---|
| `com.unity.ext.nunit` | 1.0.6 | 测试框架，桥内部用 |
| `com.unity.ugui` | 1.0.0 | uGUI |
| `com.unity.inputsystem` | — | **可选**，有则启用 `CODELY_INPUT_SYSTEM`（模拟输入相关命令） |

> Newtonsoft.Json（`Codely.Newtonsoft.Json.dll`）和 Roslyn（`Codely.Roslyn.dll`）已经**内置在包的
> `Plugins/` 目录**里，不需要项目额外引入 `com.unity.nuget.newtonsoft-json`。
> （本仓库 manifest 里那个 `com.unity.nuget.newtonsoft-json: 3.2.1` 是项目自身热更代码用的。）

**Unity 版本要求**：包声明 `unity: 2019.4`，README 建议 2021.3+。本仓库实测 Unity 2022.3 正常。

---

## 3. 三种安装方式（挑一个）

### 方式 A：中国版 / 团结引擎（推荐，最简单）

直接编辑 `Packages/manifest.json`，在 `dependencies` 里加一行，回 Unity 自动导入：

```json
"cn.tuanjie.codely.bridge": "1.0.81"
```

### 方式 B：国际版 Unity，或需要离线/固定版本（推荐，最稳）

把包**整个目录**复制成**内嵌包（embedded package）**：

1. 从已装好的项目里拿到包目录：
   - 本仓库：`D:\SaveCatCat_Library\PackageCache\cn.tuanjie.codely.bridge@1.0.81`
   - （`Library` 目录在本仓库是软链到 D 盘的；一般路径是 `<项目>/Library/PackageCache/cn.tuanjie.codely.bridge@1.0.81`）
2. 复制到新项目：`新项目/Packages/cn.tuanjie.codely.bridge/`
   - 目录里必须有 `package.json`，UPM 会自动识别为内嵌包
   - **保留完整的 `Plugins/` 目录**（`NativeTcpBridge.dll` 等原生库在各平台子目录里）
   - `Editor/`、`Plugins/` 下的 `.meta` 文件不要删（GUID 引用）
3. 回到 Unity，等待编译。Console 出现
   `Codely-Bridge: Native Codely Bridge running on port xxxxx` 即成功。

> 内嵌包不写进 `manifest.json` 的 dependencies，而是直接存在于 `Packages/` 下。
> 优点：不依赖 registry、可随 git 提交、版本锁死。缺点：升级要手动换目录。

### 方式 C：自建 scopedRegistry

如果你有私有 registry 镜像了 `packages.unity.cn`，在 `manifest.json` 加：

```json
"scopedRegistries": [
  {
    "name": "unity-cn",
    "url": "https://packages.unity.cn",
    "scopes": ["cn.tuanjie"]
  }
]
```

再加 dependencies。需要访问中国区网络。

---

## 4. 端口是怎么找到的（最容易踩坑的地方）

桥用的端口是**启动时动态分配**的（不是固定 25916；本项目实测日志里是 `51009`），
所以客户端必须先"发现"端口。`test_tcp.py` 的策略是**三级候选 + TCP 探活**：

| 优先级 | 来源 | 说明 |
|---|---|---|
| 1 | `.com-unity-codely.json` 里的 `unity_port` | 心跳文件。**注意：1.0.80 起路径改成了 `<项目根>/Temp/.com-unity-codely.json`** |
| 2 | Editor.log 正则扫描 | 匹配 `NativeTcpBridge started on port (\d+)` / `Codely-Bridge ... port (\d+)`，读最后 16MB，**最新匹配优先** |
| 3 | 循环重试 + TCP connect 探活 | 每 0.5s 一轮，直到 `timeout`（默认 10s）。域名/程序集重载后同一端口可能重开，所以每轮会清空 "已试过" 集合 |

探活方式是 `socket.create_connection(("127.0.0.1", port), timeout=0.75)`，连不上就换下一个候选。

> **本项目当前的坑**：仓库根目录的 `.com-unity-codely.json` 是**旧位置的历史残留**
> （`unity_port: -1`、`reason: package_updating`、`last_heartbeat: 2026-09-04`），
> 而 `test_tcp.py` 恰好只读这个旧路径。所以本项目实际是靠 **Editor.log 兜底**发现端口的。
> 便携版 `unity_bridge.py` 已修正为**同时检查 `Temp/` 与根目录**。

心跳文件字段含义：

```json
{
  "unity_port": 51009,          // -1 = 桥已停止
  "stream_port": 50833,         // 另一个流通道端口（1.0.81 起 native 不再写入）
  "project_path": "…/Assets",
  "reloading": false,           // 是否正在域重载
  "reason": "package_updating", // 停止原因：manually_stopped / unity_quit / package_updating / native_stopped …
  "seq": 6351,                  // 心跳序号
  "last_heartbeat": "2026-09-04T11:24:48.119Z"
}
```

各平台 Editor.log 默认路径（便携版已内置，也可用环境变量 `UNITY_EDITOR_LOG` 覆盖）：

| 平台 | 路径 |
|---|---|
| Windows | `%LOCALAPPDATA%\Unity\Editor\Editor.log` |
| macOS | `~/Library/Logs/Unity/Editor.log` |
| Linux | `~/.config/unity3d/Editor.log`（部分发行版 `~/.local/share/unity3d/Editor.log`） |

> ⚠️ 同时开多个 Unity 项目时，Editor.log 只有一份（最后一个启动的会覆盖），
> 此时端口发现可能拿到**别的项目的端口**。解决办法：显式设置
> `UNITY_BRIDGE_PORT=<正确端口>`，或先跑 `doctor` 看实际连上的是哪个。

---

## 5. 通信协议（想自己写客户端就看这节）

一次连接的生命周期：

1. **TCP 连上后，服务端先发一行 banner**，客户端必须读到 `\n` 为止并丢弃
   （`test_tcp.py` 里那段 `while True: ch = s.recv(1) …` 就是干这个的，不能省）
2. **请求帧**：`8 字节大端无符号长度` + `UTF-8 JSON`
3. **响应帧**：`8 字节大端无符号长度` + `UTF-8 JSON`，单帧上限 **64 MiB**
   （对应桥里的 `MaxFrameBytes = 64UL * 1024 * 1024`）
4. 一问一答后 `with socket…` 自动关闭连接（也可以复用，但脚本是每次新建）

请求：

```json
{ "type": "execute_csharp_script", "params": { "script": "Debug.Log(1);" } }
```

响应（外层由桥统一包装 `Response.Success/Error`）：

```json
{
  "success": true,
  "message": "Command executed successfully",
  "data": {  }          // 各 Tool 自己的结果，可能是多层嵌套
}
```

失败时：

```json
{ "success": false, "message": "…", "code": "…", "error": "…", "data": { "command": "…", "stackTrace": "…" } }
```

`struct` 用法：
- 发：`struct.pack(">Q", len(payload_bytes))`
- 收：`struct.unpack(">Q", _recv_exact(s, 8))[0]`
- 必须自己实现 `_recv_exact`（`recv` 不保证一次收满）

---

## 6. 支持的命令（type 清单，来自 UnityTcpBridge.cs 的 switch）

| type | 常用 `params.action` / 参数 | 用途 |
|---|---|---|
| `manage_scene` | `get_hierarchy` / `create` / `load` / `save` | 场景层级、存取 |
| `manage_editor` | `play` / `pause` / `stop` / `get_state` / `request_compile` / `refresh` | 编辑器状态、播放控制、编译 |
| `manage_gameobject` | — | 增删改 GameObject / 组件 |
| `manage_asset` | — | 资产导入、查找、修改 |
| `manage_script` | — | 脚本文件层面操作 |
| `manage_shader` | — | Shader 相关 |
| `manage_package` | — | UPM 包管理 |
| `manage_bake` | — | 烘焙（光照/NavMesh） |
| `read_console` | `count`（条数）/ `action: clear` / `get` | 读或清 Console 日志 |
| `execute_menu_item` | 菜单路径 | 触发 Unity 菜单项 |
| `execute_csharp_script` | `script`（必填）、`script_path`、`execution_mode`(play/editor)、`capture_logs`、`imports`… | **最常用**：执行 C# 片段并返回日志 |
| `exec_editor_script` / `exec_runtime_script` | — | 脚本文件方式执行 |
| `manage_screenshot` | `capture` / `capture_game_view` / `capture_scene_view` / `capture_main_camera` / `start_game_view_recording` … + `savePath` | 截图 / 录屏 |
| `manage_gameview` | — | GameView 尺寸/分辨率 |
| `manage_input` | — | 模拟输入（需 InputSystem 才有全部能力） |
| `manage_dialog` | — | 处理 Unity 弹窗（会阻塞的对话框） |
| `manage_job` | — | 后台异步任务（在 pump 工作线程跑） |
| `manage_window_bridge` | — | 编辑器窗口/面板级操作（下拉、拖拽、ObjectSelector…） |
| `execute_custom_tool` / `get_custom_tools` | — | 项目内注册的自定义工具 |
| `manage_workflow` | — | **已移除**，会返回改用 `manage_editor.request_compile` 的提示 |

> 写保护（WriteGuard）：Play/Pause 模式下部分写命令会被桥直接拒绝，返回
> `"Command blocked by write guard"`。这是设计行为，不是 bug。

---

## 7. 命令行用法（以 `test_tcp.py` 为例，便携版命令完全一致）

```bash
# 1) 体检：打印选中的端口、心跳文件路径、日志里发现的端口
python test_tcp.py doctor

# 2) 只打印端口号（适合 shell 里 $(...) 取值）
python test_tcp.py port

# 3) 刷新资源数据库（等导入+编译结束，最长 30s）
python test_tcp.py refresh

# 4) 读 Console 最近 20 条（可选传条数）
python test_tcp.py read_console 50

# 5) 清空 Console
python test_tcp.py clear_console

# 6) 执行 C#（必须有 return，脚本会被当表达式体执行）
python test_tcp.py execute_csharp 'return UnityEngine.Application.unityVersion;'

# 7) 场景层级
python test_tcp.py scene_info

# 8) 截图（路径会转成绝对路径后传给 Unity）
python test_tcp.py screenshot screenshots/a.png

# 9) 打印 UI 树（默认根 Canvas，深度 3）
python test_tcp.py dump_tree Canvas 4

# 10) 按名字找物体并列出组件（大小写不敏感）
python test_tcp.py find_object MainUIManager

# 11) 播放控制
python test_tcp.py play
python test_tcp.py pause
python test_tcp.py stop

# 12) 直接发原始 JSON（兜底：所有命令都能这么调）
python test_tcp.py json '{"type":"manage_editor","params":{"action":"get_state"}}'
```

`doctor` 输出示例（当前 Unity 未启动时的样子）：

```json
{
  "status": "无法连接 Unity Codely Bridge (reason: package_updating)。已检查配置文件和 Editor.log 中的端口: [51009]",
  "selected_port": null,
  "config_path": "E:\\Programs\\Project\\SaveCatCat\\.com-unity-codely.json",
  "configured_port": -1,
  "config_reason": "package_updating",
  "editor_log": "C:\\Users\\Administrator\\AppData\\Local\\Unity\\Editor\\Editor.log",
  "log_ports": [51009]
}
```

看到 `selected_port` 为数字即表示连通；为 `null` 说明 Unity 没开或桥没起来。

---

## 8. 迁移到另一个项目：Step by Step

1. **装 Unity 包**（见第 3 节，方式 A 或 B）。
2. **把客户端脚本拷过去**
   - 推荐 `docs/unity_bridge_client/unity_bridge.py` → 放到新项目根目录
   - 或直接用旧版 `test_tcp.py`（能用，但只认 Windows 日志路径 + 旧心跳路径）
3. **配置路径**（可选，给了默认值）
   - `UNITY_PROJECT_ROOT`：脚本会自动向上找含 `Assets/`+`ProjectSettings/` 的目录，一般不用设
   - `UNITY_EDITOR_LOG`：多 Unity 实例 / 非默认安装时覆盖
   - `UNITY_BRIDGE_PORT`：直接钉死端口，跳过所有发现逻辑（最省心）
   - `UNITY_BRIDGE_HOST`：默认 `127.0.0.1`
4. **打开 Unity，等编译完成**，Console 出现 `Native Codely Bridge running on port xxxxx`。
   - 也可以在菜单里确认：`Tools > Codely Bridge > Control Window / Status Window`
5. **验证**：`python unity_bridge.py doctor` → `selected_port` 有值；
   再 `python unity_bridge.py port`、`python unity_bridge.py scene_info`。
6. **gitignore 建议**
   - `Temp/` Unity 已忽略；心跳文件不用管
   - 如果用方式 B（内嵌包），`Packages/cn.tuanjie.codely.bridge/` 建议提交进 git
7. **作为库使用**

```python
from unity_bridge import send_tcp_command, get_unity_port

port = get_unity_port()                       # 或 int(os.environ["UNITY_BRIDGE_PORT"])
res  = send_tcp_command(
    {"type": "execute_csharp_script",
     "params": {"script": "return UnityEditor.AssetDatabase.GetAllAssetPaths().Length;"}},
    port=port, timeout=30,
)
print(res["data"])
```

---

## 9. 排错表

| 现象 | 原因 | 处理 |
|---|---|---|
| `无法连接 Unity Codely Bridge (reason: …)` | Unity 没开 / 桥被停 / 端口不对 | 先开 Unity；`Tools > Codely Bridge > Control Window` 点 Start；再 `doctor` |
| `unity_port: -1` + `reason: package_updating` | 正在导包或域重载，桥临时停了 | 等 Unity 空闲后重试（脚本本身会轮询 10s） |
| `reason: manually_stopped` | 有人在 Control Window 里手动 Stop 过 | 重新 Start（`SessionState` 里的 ManualStop 标记会被清掉） |
| 连上了但命令返回 `Unknown or unsupported command type` | `type` 拼错或该版本不支持 | 对照第 6 节表；版本号不同命令集会有差异 |
| `Command blocked by write guard` | 处于 Play/Pause 模式下的写操作 | 先 `stop` 退出 Play 模式 |
| 拿到的是**另一个项目**的端口 | 多 Unity 实例共用一份 Editor.log | 设 `UNITY_BRIDGE_PORT` 钉死 |
| 中文乱码 | 终端非 UTF-8 | `chcp 65001`（Windows）；脚本已尝试 `stdout.reconfigure` |
| 拷贝包后 Unity 报缺 `Cn.Tuanjie.Codely.Editor` / `Codely.Common` | 只拷了 `Editor/Bridge`，漏了 `Editor/Common`、`Editor/Tauri` | 整个包目录一起拷 |
| 报 `Codely.Roslyn.dll` / `Codely.Newtonsoft.Json.dll` 找不到 | 漏拷 `Plugins/` 根目录的两个 DLL | 补齐 `Plugins/*.dll` |
| 原生库加载失败 / ABI 不匹配 | 混用了不同版本的包目录和 DLL | 保证 `Editor/` 与 `Plugins/` 同一版本，整体替换 |

---

## 10. 便携版相对原 `test_tcp.py` 的改动

| 项 | 原 `test_tcp.py` | `unity_bridge.py` |
|---|---|---|
| 心跳文件路径 | 只读脚本同目录（旧位置） | 依次尝试 `<root>/Temp/` → `<root>/` → 脚本目录，取第一个有有效端口的 |
| 项目根判定 | 无（`__file__` 目录） | 向上查找 `Assets` + `ProjectSettings` |
| Editor.log | 仅 Windows (`LOCALAPPDATA`) | Windows / macOS / Linux + `UNITY_EDITOR_LOG` 覆盖 |
| 端口覆盖 | 无 | `UNITY_BRIDGE_PORT` / `UNITY_BRIDGE_HOST` |
| 命令集 | 相同 | 相同（去掉 dump_tree/find_object 里的 emoji，避免非 UTF-8 终端乱码） |
| 依赖 | 无 | 无 |

如果你只想最小改动，也可以直接改原 `test_tcp.py` 的这两行：

```python
CONFIG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".com-unity-codely.json")
EDITOR_LOG_PATH = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Unity", "Editor", "Editor.log")
```
