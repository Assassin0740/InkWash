using System.Collections;
using InkWash.Player;
using InkWash.Roguelike;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InkWash.UI
{
    /// <summary>
    /// UGUI 总装（第三十一轮）—— 运行时构建 Canvas 与全部面板，替代退役的 IMGUI。
    ///
    /// 为什么运行时建而不是手摆 prefab：本项目场景由脚本跨机复原（跨机克隆复原清单），
    /// 手工摆的 UI 层级是"场景里看不见的一份文档"，克隆一丢就没了；
    /// 代码建 UI **自身就是复原脚本**，场景里只挂一个空组件。
    /// （后续如需美术在编辑器里改样式，可再加"烘焙成 prefab"的菜单项，结构不变。）
    ///
    /// 一并解决的三件用户实测问题：
    /// ① "无法点击左键的开始游戏 / 选卡时鼠标动不了" —— 光标改为**按游戏状态驱动**：
    ///    MainMenu/Reward/GameOver/Victory 解锁可见，Playing 锁定隐藏；
    /// ② "血量 UI 也没有看见" —— 新增 HUD（血条 + 房间进度）；
    /// ③ "显示身陨时角色没有死亡倒地" —— 订阅 PlayerHealth.Died 做程序化倒地，
    ///    复位（ResetHealth → Revived）时自动扶起。
    /// </summary>
    [DisallowMultipleComponent]
    public class RunPresentation : MonoBehaviour
    {
        public static RunPresentation Instance { get; private set; }

        private RunManager _run;
        private PlayerHealth _health;

        private GameObject _menuPanel;
        private GameObject _resultPanel;
        private GameObject _hud;
        private Image _hpFill;
        private Text _hpText;
        private Text _roomText;
        private Text _resultTitle;
        private Text _resultDetail;

        // ---- 死亡倒地 ----
        private PlayerController _ctl;
        private Animator _anim;
        private Quaternion _standingRot;
        private Coroutine _fallRoutine;
        private bool _falling;

        private static Font UiFont
        {
            get { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            EnsureEventSystem();
            BuildCanvas();

            _run = RunManager.Instance;
            if (_run == null) _run = FindObjectOfType<RunManager>();
            if (_health == null)
                _health = _run != null && _run.playerHealth != null
                    ? _run.playerHealth
                    : FindObjectOfType<PlayerHealth>();

            if (_run != null) _run.StateChanged += OnStateChanged;
            if (_health != null)
            {
                _health.Died += OnPlayerDied;
                _health.Revived += OnPlayerRevived;
                _ctl = _health.controller;
                if (_ctl != null) _anim = _ctl.GetComponentInChildren<Animator>();
            }

            ApplyState(_run != null ? _run.State : RunState.MainMenu);
        }

        private void OnDestroy()
        {
            if (_run != null) _run.StateChanged -= OnStateChanged;
            if (_health != null) { _health.Died -= OnPlayerDied; _health.Revived -= OnPlayerRevived; }
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_hud == null || !_hud.activeSelf) return;
            if (_health != null)
            {
                float r = _health.HealthRatio;
                if (_hpFill != null) _hpFill.fillAmount = r;
                if (_hpText != null)
                    _hpText.text = Mathf.CeilToInt(_health.Health) + " / " + Mathf.CeilToInt(_health.EffectiveMaxHealth);
            }
            if (_roomText != null && _run != null)
                _roomText.text = "房间 " + Mathf.Min(_run.RoomIndex + 1, _run.roomsToClear) + " / " + _run.roomsToClear;
        }

        // ==================================================================
        //  状态 → 面板可见性 + 光标
        // ==================================================================

        private void OnStateChanged(RunState prev, RunState next) { ApplyState(next); }

        private void ApplyState(RunState s)
        {
            if (_menuPanel != null) _menuPanel.SetActive(s == RunState.MainMenu);
            if (_resultPanel != null) _resultPanel.SetActive(s == RunState.GameOver || s == RunState.Victory);
            if (_hud != null) _hud.SetActive(s == RunState.Playing || s == RunState.Reward);
            if (_resultTitle != null && _run != null)
            {
                _resultTitle.text = s == RunState.Victory ? "通　关" : "身　殒";
                _resultDetail.text = s == RunState.Victory
                    ? "清空 " + _run.RoomIndex + " 间房　升级 " + _run.RewardCount + " 次"
                    : "抵达第 " + (_run.RoomIndex + 1) + " 间房　升级 " + _run.RewardCount + " 次";
            }

            // 光标：只有战斗中才锁定隐藏；其余状态（含 Reward 选卡）解锁可见
            bool locked = s == RunState.Playing;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ==================================================================
        //  死亡倒地 / 复原
        // ==================================================================

        private void OnPlayerDied()
        {
            if (_ctl == null || _falling) return;
            _falling = true;
            if (_fallRoutine != null) StopCoroutine(_fallRoutine);
            _fallRoutine = StartCoroutine(FallRoutine());
        }

        /// <summary>程序化倒地：控住角色模型绕脚部枢轴倒向前方，0.6 s。用 unscaled 时间——结算已冻结 timeScale。</summary>
        private IEnumerator FallRoutine()
        {
            if (_ctl == null) yield break;
            _ctl.enabled = false;
            if (_anim != null) _anim.enabled = false;

            Transform t = _ctl.transform;
            _standingRot = t.rotation;

            Vector3 facing = t.forward; facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            facing.Normalize();
            // 身体 up 轴（脊柱）放倒到朝向方向 ⇒ 人躺在地上、头朝原来的面向
            Quaternion target = Quaternion.FromToRotation(Vector3.up, facing) * _standingRot;

            const float dur = 0.6f;
            float t01 = 0f;
            while (t01 < 1f)
            {
                t01 = Mathf.Min(1f, t01 + Time.unscaledDeltaTime / dur);
                float e = 1f - (1f - t01) * (1f - t01);        // ease-out：先快后慢，像失去力气
                t.rotation = Quaternion.Slerp(_standingRot, target, e);
                yield return null;
            }
            _fallRoutine = null;
        }

        private void OnPlayerRevived()
        {
            if (_fallRoutine != null) { StopCoroutine(_fallRoutine); _fallRoutine = null; }
            if (_ctl == null) return;
            _ctl.transform.rotation = _standingRot;
            if (_anim != null) _anim.enabled = true;
            _ctl.enabled = true;
            _falling = false;
        }

        // ==================================================================
        //  UGUI 构建（运行时）
        // ==================================================================

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        private void BuildCanvas()
        {
            var go = new GameObject("RunUICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            DontDestroyOnLoad(go);

            BuildMainMenu(go.transform);
            BuildResult(go.transform);
            BuildHud(go.transform);
        }

        // ---- 主菜单 ----
        private void BuildMainMenu(Transform parent)
        {
            _menuPanel = NewPanel(parent, "MainMenu", new Color(0.08f, 0.07f, 0.06f, 0.55f));

            var title = NewText(_menuPanel.transform, "Title", "墨刃 · InkWash", 64,
                new Color(0.12f, 0.10f, 0.08f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)title.transform, 0.20f, 0.80f, 0.30f, 0.46f);

            var sub = NewText(_menuPanel.transform, "Sub", "三维水墨动作 Roguelike", 22,
                new Color(0.25f, 0.22f, 0.18f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)sub.transform, 0.20f, 0.80f, 0.46f, 0.52f);

            var btn = NewButton(_menuPanel.transform, "StartBtn", "开 始 一 局", 30,
                new Color(0.13f, 0.11f, 0.09f, 0.92f), new Color(0.95f, 0.93f, 0.88f));
            Stretch(((RectTransform)btn.transform), 0.40f, 0.60f, 0.60f, 0.70f);
            btn.onClick.AddListener(OnStartClicked);
        }

        private void OnStartClicked()
        {
            if (_run != null) _run.StartRun();
        }

        // ---- 结算 ----
        private void BuildResult(Transform parent)
        {
            _resultPanel = NewPanel(parent, "Result", new Color(0.05f, 0.04f, 0.04f, 0.65f));

            _resultTitle = NewText(_resultPanel.transform, "Title", "身　殒", 56,
                new Color(0.93f, 0.91f, 0.86f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)_resultTitle.transform, 0.25f, 0.75f, 0.34f, 0.48f);

            _resultDetail = NewText(_resultPanel.transform, "Detail", "", 20,
                new Color(0.80f, 0.77f, 0.70f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)_resultDetail.transform, 0.25f, 0.75f, 0.50f, 0.56f);

            var btn = NewButton(_resultPanel.transform, "RetryBtn", "再 来 一 局", 26,
                new Color(0.90f, 0.88f, 0.82f, 0.95f), new Color(0.15f, 0.13f, 0.11f));
            Stretch((RectTransform)btn.transform, 0.42f, 0.58f, 0.62f, 0.72f);
            btn.onClick.AddListener(OnStartClicked);
        }

        // ---- HUD：血条 + 房间 ----
        private void BuildHud(Transform parent)
        {
            _hud = new GameObject("HUD", typeof(RectTransform));
            var rt = (RectTransform)_hud.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            // 血条（左下）：底 + 填充 + 数字
            var bgGo = new GameObject("HpBg", typeof(Image));
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.SetParent(_hud.transform, false);
            bgRt.anchorMin = new Vector2(0.03f, 0.05f); bgRt.anchorMax = new Vector2(0.26f, 0.095f);
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            bgGo.GetComponent<Image>().color = new Color(0.10f, 0.09f, 0.08f, 0.75f);

            var fillGo = new GameObject("HpFill", typeof(Image));
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.SetParent(bgRt, false);
            fillRt.anchorMin = new Vector2(0.012f, 0.12f); fillRt.anchorMax = new Vector2(0.988f, 0.88f);
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            _hpFill = fillGo.GetComponent<Image>();
            _hpFill.color = new Color(0.16f, 0.14f, 0.13f);
            _hpFill.type = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.fillAmount = 1f;

            _hpText = NewText(bgRt, "HpText", "", 15, new Color(0.95f, 0.93f, 0.88f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)_hpText.transform, 0f, 1f, -0.2f, 1.2f);

            // 房间进度（左上）
            _roomText = NewText(_hud.transform, "RoomText", "", 20,
                new Color(0.15f, 0.13f, 0.11f, 0.9f), TextAnchor.UpperLeft);
            Stretch((RectTransform)_roomText.transform, 0.03f, 0.35f, 0.93f, 0.98f);
        }

        // ==================================================================
        //  UGUI 小工厂
        // ==================================================================

        private static GameObject NewPanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            go.SetActive(false);
            return go;
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
            return t;
        }

        private static Button NewButton(Transform parent, string name, string label, int size,
            Color bg, Color fg)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            go.GetComponent<Image>().color = bg;

            var txt = NewText(rt, "Label", label, size, fg, TextAnchor.MiddleCenter);
            Stretch((RectTransform)txt.transform, 0f, 1f, 0f, 1f);

            var b = go.GetComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.25f);
            colors.pressedColor = new Color(0f, 0f, 0f, 0.25f);
            b.colors = colors;
            return b;
        }

        private static void Stretch(RectTransform rt, float x0, float x1, float y0, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0);
            rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
