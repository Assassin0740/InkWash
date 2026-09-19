using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using InkWash.Roguelike;

namespace InkWash.UI
{
    /// <summary>
    /// 升级三选一（UGUI 版，第三十四轮）。
    ///
    /// ★ 为什么从 IMGUI 迁到 UGUI（用户实测反馈「你的 UI 不是 UGUI」）：
    ///   IMGUI 是即时模式——每帧重建控件、吃 OnGUI 消耗、缩放/布局/点击判定都自成一套，
    ///   与 RunPresentation 的 UGUI 主菜单/结算/HUD 割裂。选卡是玩家停留最久的界面，
    ///   必须同一套渲染与交互管线。迁移后光标解锁（Reward 状态）直接可用鼠标点卡。
    ///
    /// ★ 保留 <see cref="InjectChoice"/>：这是这个面板能被自动验收的**唯一入口**
    ///   （第三十轮 S5 修复时已确立为一等公民接口，不是测试特权路径）。
    ///
    /// 暂停（Time.timeScale = 0）由 <see cref="RunManager"/> 负责，面板自己不碰 timeScale ——
    /// "谁改 timeScale"必须只有一个地方，否则顿帧（HitStop 也改）与暂停会互相覆盖。
    /// 键盘 1/2/3 走 <see cref="Update"/>：timeScale=0 时 Update 照常跑（deltaTime=0 而已），
    /// 暂停中照样能选。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkillChoicePanel : MonoBehaviour
    {
        [Tooltip("同时给几个选项（三选一 = 3）")]
        public int optionCount = 3;

        [Tooltip("键盘选择键，按顺序对应第 1/2/3 张卡")]
        public KeyCode[] keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };

        [Header("风格")]
        public Color paperColor = new Color(0.90f, 0.87f, 0.80f);
        public Color inkColor = new Color(0.09f, 0.10f, 0.13f);

        [Header("诊断（只读）")]
        [SerializeField] private int _showCount;
        [SerializeField] private int _chosenIndex = -1;
        [SerializeField] private string _chosenName = "（未选）";
        [SerializeField] private int _injectedChoiceCount;

        public int ShowCount => _showCount;
        public int ChosenIndex => _chosenIndex;
        public string ChosenName => _chosenName;
        public int InjectedChoiceCount => _injectedChoiceCount;
        public bool IsShowing => _showing;
        public IReadOnlyList<SkillData> Options => _options;

        private readonly List<SkillData> _options = new List<SkillData>();
        private bool _showing;
        private Action<SkillData> _onChosen;
        private SkillInventory _inventory;

        // ---- UGUI 结构（惰性构建：首次 Show 前建好）----
        private Canvas _canvas;
        private GameObject _panel;
        private Text _title;
        private Text _debug;
        private readonly List<CardView> _cards = new List<CardView>();

        private class CardView
        {
            public GameObject root;
            public Image strip;
            public Text title;
            public Text sub;
            public Text body;
            public Text cap;
            public Button pick;
        }

        private void Awake()
        {
            if (optionCount < 1) optionCount = 3;
        }

        private void Start()
        {
            // 用来自查"当前几层"（候选卡要显示"再叠一层变成多少"）
            _inventory = GetComponent<SkillInventory>();
            if (_inventory == null) _inventory = FindObjectOfType<SkillInventory>();
        }

        // ==================================================================
        //  UGUI 构建
        // ==================================================================

        private static Font UiFont
        {
            get { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        }

        /// <summary>独立 Canvas：sortingOrder 150 压过 RunPresentation(100) 的 HUD——选卡是模态界面。</summary>
        private void EnsureCanvas()
        {
            if (_canvas != null) return;

            var go = new GameObject("SkillChoiceCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 150;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            DontDestroyOnLoad(go);

            // 压暗背景（墨色幕布）：raycastTarget=true 顺便挡掉背后 HUD 的误点
            _panel = new GameObject("RewardPanel", typeof(Image));
            var rt = (RectTransform)_panel.transform;
            rt.SetParent(go.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.62f);
            _panel.SetActive(false);

            _title = NewText(_panel.transform, "Title", "领悟 · 择一", 30, paperColor, TextAnchor.MiddleCenter);
            Stretch((RectTransform)_title.transform, 0.25f, 0.75f, 0.14f, 0.20f);

            _debug = NewText(_panel.transform, "DebugLine", "", 13, new Color(0.88f, 0.86f, 0.80f, 0.85f), TextAnchor.UpperLeft);
            Stretch((RectTransform)_debug.transform, 0.01f, 0.99f, 0.955f, 0.995f);

            // 三张卡槽（位置固定，Show 时按数量填充/隐藏）
            const float gap = 22f;
            float cardW = 276f, cardH = 236f;
            for (int i = 0; i < Mathf.Max(1, optionCount); i++)
            {
                float x0 = 0.5f + (i - (optionCount - 1) * 0.5f) * (cardW + gap) / 1920f;
                _cards.Add(BuildCard(_panel.transform, i, x0 - cardW / 1920f / 2f, cardW / 1920f, cardH / 1080f));
            }
        }

        private CardView BuildCard(Transform parent, int index, float xAnchor, float wAnchor, float hAnchor)
        {
            var v = new CardView();

            var go = new GameObject("Card" + index, typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(xAnchor, 0.5f - hAnchor / 2f);
            rt.anchorMax = new Vector2(xAnchor + wAnchor, 0.5f + hAnchor / 2f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.97f);
            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.18f);
            colors.pressedColor = new Color(0f, 0f, 0f, 0.18f);
            btn.colors = colors;
            v.root = go;

            // 稀有度色条（卡片顶部）
            var stripGo = new GameObject("Strip", typeof(Image));
            var stripRt = (RectTransform)stripGo.transform;
            stripRt.SetParent(rt, false);
            stripRt.anchorMin = new Vector2(0f, 1f); stripRt.anchorMax = new Vector2(1f, 1f);
            stripRt.pivot = new Vector2(0.5f, 1f);
            stripRt.sizeDelta = new Vector2(0f, 6f);
            stripRt.anchoredPosition = Vector2.zero;
            v.strip = stripGo.GetComponent<Image>();
            v.strip.raycastTarget = false;

            v.title = NewText(rt, "Name", "", 21, inkColor, TextAnchor.MiddleCenter);
            Stretch((RectTransform)v.title.transform, 0.03f, 0.97f, 0.865f, 0.94f);

            v.sub = NewText(rt, "Sub", "", 13, new Color(0.35f, 0.35f, 0.35f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)v.sub.transform, 0.03f, 0.97f, 0.795f, 0.855f);

            v.body = NewText(rt, "Body", "", 14, inkColor, TextAnchor.UpperLeft);
            var bodyRt = (RectTransform)v.body.transform;
            Stretch(bodyRt, 0.06f, 0.94f, 0.30f, 0.78f);
            v.body.horizontalOverflow = HorizontalWrapMode.Wrap;
            v.body.verticalOverflow = VerticalWrapMode.Truncate;
            v.body.rectTransform.sizeDelta = new Vector2(0f, 0f);

            v.cap = NewText(rt, "Cap", "", 13, new Color(0.35f, 0.35f, 0.35f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)v.cap.transform, 0.03f, 0.97f, 0.135f, 0.195f);

            var pickGo = new GameObject("Pick", typeof(Image), typeof(Button));
            var pickRt = (RectTransform)pickGo.transform;
            pickRt.SetParent(rt, false);
            pickRt.anchorMin = new Vector2(0.11f, 0.04f); pickRt.anchorMax = new Vector2(0.89f, 0.115f);
            pickRt.offsetMin = Vector2.zero; pickRt.offsetMax = Vector2.zero;
            pickGo.GetComponent<Image>().color = inkColor;
            var pb = pickGo.GetComponent<Button>();
            var pc = pb.colors;
            pc.highlightedColor = new Color(1f, 1f, 1f, 0.22f);
            pc.pressedColor = new Color(0f, 0f, 0f, 0.30f);
            pb.colors = pc;
            v.pick = pb;

            var pickLabel = NewText(pickRt, "Label", "", 14, new Color(0.94f, 0.92f, 0.86f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)pickLabel.transform, 0f, 1f, 0f, 1f);

            int idx = index;
            btn.onClick.AddListener(() => { if (_showing) Commit(idx); });
            pb.onClick.AddListener(() => { if (_showing) Commit(idx); });
            return v;
        }

        private static Text NewText(Transform parent, string name, string content, int size,
            Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = UiFont;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt, float x0, float x1, float y0, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ==================================================================
        //  流程
        // ==================================================================

        public void Show(List<SkillData> options, Action<SkillData> onChosen)
        {
            EnsureCanvas();

            _options.Clear();
            if (options != null)
                for (int i = 0; i < options.Count && i < optionCount; i++) _options.Add(options[i]);

            _onChosen = onChosen;
            _showing = _options.Count > 0;
            _chosenIndex = -1;
            _chosenName = "（未选）";
            if (_showing) _showCount++;

            RefreshCards();
            if (_panel != null) _panel.SetActive(_showing);
        }

        public void Hide()
        {
            _showing = false;
            _options.Clear();
            _onChosen = null;
            if (_panel != null) _panel.SetActive(false);
        }

        /// <summary>
        /// 注入一次选择（自动化验收用）。返回 false = 面板没开 / 下标越界。
        /// 这是这个面板能被自动验收的**唯一入口**：验收脚本不可能去点 UGUI 按钮，
        /// 所以"可编程选择"必须是一等公民接口，而不是测试时临时加的特权路径。
        /// </summary>
        public bool InjectChoice(int index)
        {
            if (!_showing) return false;
            if (index < 0 || index >= _options.Count) return false;
            _injectedChoiceCount++;
            Commit(index);
            return true;
        }

        private void Commit(int index)
        {
            var picked = _options[index];
            _chosenIndex = index;
            _chosenName = picked != null ? picked.displayName : "（空）";

            var cb = _onChosen;
            Hide();
            if (cb != null) cb(picked);
        }

        private void Update()
        {
            if (!_showing) return;
            for (int i = 0; i < _options.Count && i < keys.Length; i++)
                if (Input.GetKeyDown(keys[i])) { Commit(i); return; }
        }

        /// <summary>Show 时填充三张卡的内容（数量不足的槽隐藏）。</summary>
        private void RefreshCards()
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var v = _cards[i];
                bool used = i < _options.Count && _options[i] != null;
                v.root.SetActive(used);
                if (!used) continue;

                var s = _options[i];
                int stacks = _inventory != null ? _inventory.StacksOf(s) : 0;

                if (v.strip != null)
                {
                    var rc = s.RarityColor; rc.a = 1f;
                    v.strip.color = rc;
                }
                if (v.title != null) v.title.text = s.displayName;
                if (v.sub != null)
                    v.sub.text = s.RarityName + "　" + (stacks > 0 ? "已有 " + stacks + " 层" : "未获得");
                if (v.body != null) v.body.text = s.BuildDescription(stacks);
                if (v.cap != null)
                    v.cap.text = stacks >= s.maxStacks ? "已满级" : "第 " + (stacks + 1) + " 层";
                if (v.pick != null)
                {
                    var lbl = v.pick.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = "选它（" + (i + 1) + "）";
                }
            }

            if (_debug != null)
            {
                var lv = _inventory != null ? _inventory.GetComponent<LevelSystem>() : null;
                string line = lv != null ? lv.Describe() : "";
                string owned = _inventory != null ? _inventory.Describe() : "";
                _debug.text = line + (line.Length > 0 && owned.Length > 0 ? "　｜　" : "") + owned;
            }
        }
    }
}
