using System;
using System.Collections.Generic;
using UnityEngine;
using InkWash.Roguelike;

namespace InkWash.UI
{
    /// <summary>
    /// 升级三选一。用 IMDbGUI 而不是 uGUI Canvas，理由与 <see cref="InkWash.UI.InkStylePanel"/> 相同：
    ///   1. 本工程没有 uGUI 场景资产，为三张卡引入一整套 Canvas/EventSystem 是重资产；
    ///   2. 关键：**验收脚本要能驱动它**。uGUI 的按钮点击需要模拟 EventSystem 射线，
    ///      在自动化环境里非常脆；IMGUI 只要暴露一个 <see cref="InjectChoice"/> 就完事了。
    ///
    /// 暂停（Time.timeScale = 0）由 <see cref="RunManager"/> 负责，面板自己不碰 timeScale ——
    /// "谁改 timeScale"必须只有一个地方，否则顿帧（HitStop 也改）与暂停会互相覆盖。
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

        private GUIStyle _cardTitle, _cardSub, _cardBody, _hint, _hud;
        private bool _stylesReady;

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

        private void BuildStyles()
        {
            _cardTitle = new GUIStyle(GUI.skin.label)
            { fontSize = 21, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _cardTitle.normal.textColor = inkColor;

            _cardSub = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            _cardSub.normal.textColor = new Color(0.35f, 0.35f, 0.35f);

            _cardBody = new GUIStyle(GUI.skin.label)
            { fontSize = 14, alignment = TextAnchor.UpperLeft, wordWrap = true };
            _cardBody.normal.textColor = inkColor;

            _hint = new GUIStyle(GUI.skin.label)
            { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            _hint.normal.textColor = paperColor;

            _hud = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperLeft };
            _hud.normal.textColor = new Color(0.88f, 0.86f, 0.80f);
            _stylesReady = true;
        }

        public void Show(List<SkillData> options, Action<SkillData> onChosen)
        {
            _options.Clear();
            if (options != null)
                for (int i = 0; i < options.Count && i < optionCount; i++) _options.Add(options[i]);

            _onChosen = onChosen;
            _showing = _options.Count > 0;
            _chosenIndex = -1;
            _chosenName = "（未选）";
            if (_showing) _showCount++;
        }

        public void Hide()
        {
            _showing = false;
            _options.Clear();
            _onChosen = null;
        }

        /// <summary>
        /// 注入一次选择（自动化验收用）。返回 false = 面板没开 / 下标越界。
        ///
        /// 这是这个面板能被自动验收的**唯一入口**：验收脚本不可能去点 IMGUI 按钮，
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

        private void OnGUI()
        {
            if (!_showing) return;
            if (!_stylesReady) BuildStyles();

            // 压暗背景：让三张卡跳出来（墨色幕布）
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            var head = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            head.normal.textColor = paperColor;
            GUI.Label(new Rect(0f, Screen.height * 0.14f, Screen.width, 40f), "领悟 · 择一", head);

            const float gap = 22f;
            float cardW = Mathf.Min(276f, (Screen.width - gap * (optionCount + 1)) / optionCount);
            float cardH = 236f;
            float totalW = cardW * _options.Count + gap * (_options.Count - 1);
            float x0 = (Screen.width - totalW) * 0.5f;
            float y0 = Screen.height * 0.5f - cardH * 0.5f;

            for (int i = 0; i < _options.Count; i++)
            {
                var s = _options[i];
                if (s == null) continue;
                var rect = new Rect(x0 + i * (cardW + gap), y0, cardW, cardH);

                // 卡片底：宣纸色
                var bg = new Color(paperColor.r, paperColor.g, paperColor.b, 0.97f);
                var old = GUI.color;
                GUI.color = bg;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = old;

                // 稀有度色条
                var strip = new Color(s.RarityColor.r, s.RarityColor.g, s.RarityColor.b, 1f);
                var oldC = GUI.color;
                GUI.color = strip;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 5f), Texture2D.whiteTexture);
                GUI.color = oldC;

                GUI.Label(new Rect(rect.x + 8f, rect.y + 14f, rect.width - 16f, 30f), s.displayName, _cardTitle);

                int stacks = _inventory != null ? _inventory.StacksOf(s) : 0;
                string sub = s.RarityName + "　" + (stacks > 0 ? "已有 " + stacks + " 层" : "未获得");
                GUI.Label(new Rect(rect.x + 8f, rect.y + 46f, rect.width - 16f, 20f), sub, _cardSub);

                GUI.Label(new Rect(rect.x + 16f, rect.y + 76f, rect.width - 32f, 110f),
                          s.BuildDescription(stacks), _cardBody);

                string cap = stacks >= s.maxStacks ? "已满级" : "第 " + (stacks + 1) + " 层";
                GUI.Label(new Rect(rect.x + 8f, rect.y + cardH - 46f, rect.width - 16f, 20f), cap, _cardSub);

                if (GUI.Button(new Rect(rect.x + 30f, rect.y + cardH - 30f, rect.width - 60f, 26f), "选它（" + (i + 1) + "）"))
                    Commit(i);
            }

            GUI.Label(new Rect(0f, y0 + cardH + 22f, Screen.width, 26f),
                      "按 1 / 2 / 3 或点按钮选择", _hint);

            DrawHud();
        }

        /// <summary>左上角常驻小字：让录屏里也能看到"当前几级、拿了什么"。</summary>
        private void DrawHud()
        {
            var rm = RunManager.Instance;
            var lv = _inventory != null ? _inventory.GetComponent<LevelSystem>() : null;
            string line = lv != null ? lv.Describe() : "";
            string owned = _inventory != null ? _inventory.Describe() : "";
            if (line.Length > 0) GUI.Label(new Rect(14f, Screen.height - 46f, 640f, 20f), line, _hud);
            if (owned.Length > 0) GUI.Label(new Rect(14f, Screen.height - 26f, 900f, 20f), owned, _hud);
            if (rm != null) GUI.Label(new Rect(14f, 12f, 640f, 20f), rm.Describe(), _hud);
        }
    }
}
