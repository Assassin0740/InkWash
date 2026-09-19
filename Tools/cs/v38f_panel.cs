// v38f_panel.cs —— 编辑器态：InkStyle 实例 _stage=4 / visible=false，保存场景
using UnityEngine;
using UnityEditor;

var panel = Object.FindObjectOfType<InkWash.UI.InkStylePanel>(true);
if (panel == null) return "FAIL no InkStylePanel in scene";
var so = new SerializedObject(panel);
var st = so.FindProperty("_stage");
var vis = so.FindProperty("visible");
if (st == null || vis == null) return "FAIL props missing";
st.intValue = 4;
vis.boolValue = false;
so.ApplyModifiedProperties();
EditorUtility.SetDirty(panel);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
return "panel @ " + panel.gameObject.name + " stage=" + st.intValue + " visible=" + vis.boolValue + " saved=" + saved;
