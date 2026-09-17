// m7_build：M7 交付 —— 打包 Windows 64 位可执行（主场景 + 演示场）。
using System.IO;
using UnityEditor;
using UnityEngine;

string outDir = Path.GetFullPath("Build");
Directory.CreateDirectory(outDir);
string exe = Path.Combine(outDir, "InkWash.exe");

var opts = new BuildPlayerOptions
{
    scenes = new[] { "Assets/_Project/Scenes/Main.unity", "Assets/_Project/Scenes/Showcase.unity" },
    locationPathName = exe,
    target = BuildTarget.StandaloneWindows64,
    options = BuildOptions.None
};

Debug.Log("[BUILD] 开始打包 -> " + exe);
var rep = BuildPipeline.BuildPlayer(opts);
var s = rep.summary;

Debug.Log("[BUILD] 结果=" + s.result
          + "  产物大小=" + (s.totalSize / (1024f * 1024f)).ToString("F1") + " MB"
          + "  错误=" + s.totalErrors + "  警告=" + s.totalWarnings
          + "  耗时=" + s.totalTime.TotalSeconds.ToString("F0") + "s");

if (File.Exists(exe))
    Debug.Log("[BUILD] 可执行已生成: " + exe + "  " + (new FileInfo(exe).Length / (1024f * 1024f)).ToString("F1") + " MB");
else
    Debug.LogWarning("[BUILD] 未找到可执行文件");
