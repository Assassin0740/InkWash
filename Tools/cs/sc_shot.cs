using System;
using System.IO;
using UnityEngine;

// sc_shot：把**主相机当前看到的画面**存成 PNG（桥的 screenshot 接口被禁用了）。
// 用来在"用户看游戏画面"的复核流程里，给我自己留一份可回看的证据。
var N = 1;                       // 连拍几张（看波形有没有在动）
var dir = "D:/Unity Project/InkWash/Tools/screenshots/review";
Directory.CreateDirectory(dir);

Camera cam = Camera.main;
if (cam == null)
{
    foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>())
        if (c != null && c.enabled) { cam = c; break; }
}
if (cam == null) return "SC_SHOT_NO_CAMERA";

int w = 1120, h = 630;
var rt = new RenderTexture(w, h, 24);
string msg = "SC_SHOT " + cam.name;
for (int i = 0; i < N; i++)
{
    cam.targetTexture = rt;
    cam.Render();
    RenderTexture.active = rt;
    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
    tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
    tex.Apply();
    File.WriteAllBytes(dir + "/rv_" + i + ".png", tex.EncodeToPNG());
    UnityEngine.Object.Destroy(tex);
    RenderTexture.active = null;
    // （--runtime 禁止 Thread.Sleep；要看波形变化就隔几秒多调一次本脚本）
}
cam.targetTexture = null;
UnityEngine.Object.Destroy(rt);
return msg + " → " + N + " 张已存到 " + dir;
