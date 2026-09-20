using System.Collections;
using InkWash.Combat;
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
        private Image _hitVignette;     // 受击墨溅闪屏
        private float _hitFlash;

        // ---- 以撒式过门：提示 + 过场淡入 ----
        private Text _doorPrompt;       // "石门已开 —— 穿过门洞继续"
        private Image _fadeImage;       // 过门瞬间的水墨黑场
        private Coroutine _fadeRoutine;

        // ---- 主动技能冷却（墨爆 R）----
        private PlayerActiveSkill _skill;
        private Text _skillText;

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
            _skill = FindObjectOfType<PlayerActiveSkill>();   // 墨爆冷却读数（第三十四轮）

            if (_run != null)
            {
                _run.StateChanged += OnStateChanged;
                _run.DoorOpened += OnDoorOpened;     // 以撒式过门（第三十三轮）
                _run.DoorCrossed += OnDoorCrossed;
            }
            if (_health != null)
            {
                _health.Died += OnPlayerDied;
                _health.Revived += OnPlayerRevived;
                _health.Damaged += OnPlayerDamaged;   // 受击 → 墨溅闪屏（第三十二轮）
                _ctl = _health.controller;
                if (_ctl != null) _anim = _ctl.GetComponentInChildren<Animator>();
            }

            ApplyState(_run != null ? _run.State : RunState.MainMenu);

            // ★ BGM（第三十七轮）：中国风曲循环——主菜单就开始放，音量在 AudioManager 里压低
            InkWash.Audio.AudioManager.PlayBgm();
        }

        private void OnDestroy()
        {
            if (_run != null)
            {
                _run.StateChanged -= OnStateChanged;
                _run.DoorOpened -= OnDoorOpened;
                _run.DoorCrossed -= OnDoorCrossed;
            }
            if (_health != null)
            {
                _health.Died -= OnPlayerDied;
                _health.Revived -= OnPlayerRevived;
                _health.Damaged -= OnPlayerDamaged;
            }
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_hud == null || !_hud.activeSelf) return;
            if (_health != null)
            {
                float r = _health.HealthRatio;
                if (_hpFill != null)
                {
                    _hpFill.fillAmount = r;
                    // ★ 濒危脉动（第三十七轮）：血量 < 30% 时朱砂色明暗脉动，
                    //   用 unscaled 时间（选卡/结算暂停时脉动不冻结）
                    _hpFill.color = r < 0.3f
                        ? Color.Lerp(new Color(0.52f, 0.15f, 0.11f), new Color(0.80f, 0.12f, 0.08f),
                            0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f))
                        : new Color(0.52f, 0.15f, 0.11f);
                }
                if (_hpText != null)
                    _hpText.text = Mathf.CeilToInt(_health.Health) + " / " + Mathf.CeilToInt(_health.EffectiveMaxHealth);
            }
            if (_roomText != null && _run != null)
                _roomText.text = "房间 " + Mathf.Min(_run.RoomIndex + 1, _run.roomsToClear) + " / " + _run.roomsToClear;

            // 主动技能冷却读数（墨爆 R）：就绪高亮，冷却中灰显倒计时
            if (_skillText != null)
            {
                if (_skill == null) _skill = FindObjectOfType<PlayerActiveSkill>();
                if (_skill != null)
                {
                    _skillText.text = _skill.IsReady
                        ? "墨爆 [R]  就绪"
                        : "墨爆 [R]  " + _skill.CooldownLeft.ToString("F1") + "s";
                    _skillText.color = _skill.IsReady
                        ? new Color(0.95f, 0.93f, 0.88f, 0.95f)
                        : new Color(0.95f, 0.93f, 0.88f, 0.35f);
                }
                else _skillText.text = "";
            }

            // 受击闪屏衰减：unscaled 时间 —— 顿帧/暂停期间也要正常退掉
            if (_hitFlash > 0f)
            {
                _hitFlash = Mathf.Max(0f, _hitFlash - Time.unscaledDeltaTime * 3.2f);
                if (_hitVignette != null)
                    _hitVignette.color = new Color(0.32f, 0.06f, 0.05f, _hitFlash * 0.38f);
            }
        }

        /// <summary>
        /// 受击墨溅闪屏（第三十二轮）：画面四缘涌上一层淡墨红再退掉。
        /// 为什么不做在 PlayerHitFeedback 里：它挂在 Player prefab 上、生命周期跟局走，
        /// 而 Canvas 在这里（DontDestroyOnLoad）；且 HUD 的构建/可见性本来就归这个类管。
        /// </summary>
        public void FlashDamage() { _hitFlash = 1f; }

        private void OnPlayerDamaged(DamageInfo info) { FlashDamage(); }

        // ==================================================================
        //  以撒式过门：提示 / 水墨转场
        // ==================================================================

        private void OnDoorOpened()
        {
            if (_doorPrompt != null) _doorPrompt.gameObject.SetActive(true);
        }

        /// <summary>过门黑场当前 alpha（验收读数口，不走运行时逻辑）。</summary>
        public float DoorFadeAlphaForTest => _fadeImage != null ? _fadeImage.color.a : 0f;

        private void OnDoorCrossed()
        {
            if (_doorPrompt != null) _doorPrompt.gameObject.SetActive(false);
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(DoorFadeRoutine());
        }

        /// <summary>穿门转场：墨色涌入盖住传送瞬间 → 玩家已在下一间房 → 墨色退去。全程 unscaled。</summary>
        private IEnumerator DoorFadeRoutine()
        {
            if (_fadeImage == null) yield break;
            float t = 0f;
            while (t < 0.16f)   // 涌入
            {
                t += Time.unscaledDeltaTime;
                _fadeImage.color = new Color(0.06f, 0.05f, 0.05f, Mathf.Clamp01(t / 0.16f) * 0.92f);
                yield return null;
            }
            t = 0f;
            while (t < 0.55f)   // 退去
            {
                t += Time.unscaledDeltaTime;
                _fadeImage.color = new Color(0.06f, 0.05f, 0.05f, (1f - Mathf.Clamp01(t / 0.55f)) * 0.92f);
                yield return null;
            }
            _fadeImage.color = new Color(0.06f, 0.05f, 0.05f, 0f);
            _fadeRoutine = null;
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
            // 过门提示只在战斗界面有意义：死亡/通关时若还挂着就收掉
            if (_doorPrompt != null && (s == RunState.GameOver || s == RunState.Victory))
                _doorPrompt.gameObject.SetActive(false);
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

        /// <summary>
        /// 死亡表现（第三十七轮重构）：
        /// ★ **死亡动画优先**——Player.controller 已接 Death.anim（Dead 触发器），
        ///   有真实倒地动画就不该拧 transform：31 轮的"disable Animator + 绕脚部
        ///   枢轴转 90°"会把骨骼停在死前姿势整体放倒，观感就是用户截图里的
        ///   "碎在地上"。
        /// ★ 程序化倒地只作为**无死亡动画时的兜底**（controller 没有 Dead 参数时）。
        /// 用 unscaled 时间——结算已冻结 timeScale。
        /// </summary>
        private IEnumerator FallRoutine()
        {
            if (_ctl == null) yield break;
            _ctl.enabled = false;

            Transform t = _ctl.transform;

            // ---- 路线 A：死亡动画（Player.controller 有 Dead 触发器且 Animator 可用）----
            if (_anim != null && _anim.runtimeAnimatorController != null && HasTrigger(_anim, "Dead"))
            {
                // ★ 身陨暂停（timeScale=0）会把 Animator 一起冻住 —— 死亡动画切
                //   UnscaledTime 模式，暂停中照播；复活时恢复 Normal。
                _anim.enabled = true;
                _anim.updateMode = AnimatorUpdateMode.UnscaledTime;
                _anim.SetTrigger(HashDeadAnim);

                // ★ v45 实测故障：致死那一击会**同时**挂上 Hit 触发器（受击表现），
                //   AnyState->Hit 的过渡一旦先启动，后来才设的 Dead 就再也挤不进去
                //   （AnyState 转移在过渡进行中默认不打断），实测死后停在 Hit 状态、
                //   死亡动画根本没播。这里双管齐下：清掉 Hit 触发器 + 用 CrossFade
                //   直接切 Death 状态（CrossFade 无视条件与进行中的过渡）。
                _anim.ResetTrigger(HashHitAnim);
                _anim.CrossFade(HashDeathState, 0.12f, 0, 0f);
                // 等动画把人放倒（Death.anim 约 1.2 s）；期间不碰 transform
                float guard = 0f;
                while (guard < 2.5f) { guard += Time.unscaledDeltaTime; yield return null; }
                _fallRoutine = null;
                yield break;
            }

            // ---- 路线 B：程序化倒地兜底 ----
            if (_anim != null) _anim.enabled = false;
            _usedProceduralFall = true;
            _standingRot = t.rotation;

            Vector3 facing = t.forward; facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            facing.Normalize();
            // 身体 up 轴（脊柱）放倒到朝向方向 ⇒ 人躺在地上、头朝原来的面向。
            // ★ 第三十七轮：枢轴在脚部，转 90° 后身体平面贴地——但骨骼若停在
            //   T-pose/跑姿，四肢会插进地面。抬高半个身位兜底，视觉上"瘫倒"
            //   而不是"切进地里"。
            Quaternion target = Quaternion.FromToRotation(Vector3.up, facing) * _standingRot;
            Vector3 pos0 = t.position;
            Vector3 pos1 = pos0 + Vector3.up * 0.25f;

            const float dur = 0.6f;
            float t01 = 0f;
            while (t01 < 1f)
            {
                t01 = Mathf.Min(1f, t01 + Time.unscaledDeltaTime / dur);
                float e = 1f - (1f - t01) * (1f - t01);        // ease-out：先快后慢，像失去力气
                t.rotation = Quaternion.Slerp(_standingRot, target, e);
                t.position = Vector3.Lerp(pos0, pos1, e);
                yield return null;
            }
            _fallRoutine = null;
        }

        private static readonly int HashDeadAnim = Animator.StringToHash("Dead");
        private static readonly int HashHitAnim = Animator.StringToHash("Hit");
        private static readonly int HashDeathState = Animator.StringToHash("Death");

        /// <summary>controller 是否真的存在该 Trigger 参数（不存在 SetTrigger 会刷警告）。</summary>
        private static bool HasTrigger(Animator a, string name)
        {
            if (a.runtimeAnimatorController == null) return false;
            foreach (var p in a.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == name) return true;
            return false;
        }

        private void OnPlayerRevived()
        {
            if (_fallRoutine != null) { StopCoroutine(_fallRoutine); _fallRoutine = null; }
            if (_ctl == null) return;
            // ★ 只有程序化倒地（路线 B）才动过 transform —— 动画路线的复活
            //   不需要也不应该拧回旋转（_standingRot 可能是陈旧值）。
            if (_usedProceduralFall)
            {
                _ctl.transform.rotation = _standingRot;
                _usedProceduralFall = false;
            }
            if (_anim != null)
            {
                _anim.updateMode = AnimatorUpdateMode.Normal;   // 死亡动画用的 UnscaledTime 恢复回来
                _anim.enabled = true;
            }
            _ctl.enabled = true;
            _falling = false;
        }

        private bool _usedProceduralFall;

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
            InkWash.Audio.AudioManager.Play("UI_Click", 0.9f);
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

            // 血条（左下）：宣纸底 + 朱砂填充 + 墨描边（第三十七轮美化）
            //   ★ 之前是两块纯色矩形，用户评"很粗糙"。水墨 HUD 的语言：
            //   纸（底）— 朱砂（血）— 墨（框），条形用**程序化笔触 sprite**
            //   （SDF 圆角条 + 噪声扰边 + 左端笔锋收尖），不再是完美矩形。
            var bgGo = new GameObject("HpBg", typeof(Image));
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.SetParent(_hud.transform, false);
            bgRt.anchorMin = new Vector2(0.03f, 0.05f); bgRt.anchorMax = new Vector2(0.26f, 0.10f);
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = BrushBarSprite;
            bgImg.color = new Color(0.955f, 0.945f, 0.915f, 0.92f);   // 宣纸

            var outline = bgGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.10f, 0.09f, 0.08f, 0.95f);  // 墨框
            outline.effectDistance = new Vector2(2f, -2f);

            var fillGo = new GameObject("HpFill", typeof(Image));
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.SetParent(bgRt, false);
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = new Vector2(6f, 5f); fillRt.offsetMax = new Vector2(-6f, -5f);
            _hpFill = fillGo.GetComponent<Image>();
            _hpFill.sprite = BrushBarSprite;
            _hpFill.color = new Color(0.52f, 0.15f, 0.11f);           // 朱砂（血）
            _hpFill.type = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.fillAmount = 1f;
            _hpFill.raycastTarget = false;
            bgImg.raycastTarget = false;

            _hpText = NewText(bgRt, "HpText", "", 15, new Color(0.98f, 0.96f, 0.90f), TextAnchor.MiddleCenter);
            Stretch((RectTransform)_hpText.transform, 0f, 1f, -0.2f, 1.2f);

            // 房间进度（左上）
            _roomText = NewText(_hud.transform, "RoomText", "", 20,
                new Color(0.15f, 0.13f, 0.11f, 0.9f), TextAnchor.UpperLeft);
            Stretch((RectTransform)_roomText.transform, 0.03f, 0.35f, 0.93f, 0.98f);

            // 受击墨溅闪屏（全屏、alpha 0 待命、不挡射线 —— 挡了会吞主菜单/选卡的点击）
            var vinGo = new GameObject("HitVignette", typeof(Image));
            var vinRt = (RectTransform)vinGo.transform;
            vinRt.SetParent(_hud.transform, false);
            vinRt.anchorMin = Vector2.zero; vinRt.anchorMax = Vector2.one;
            vinRt.offsetMin = Vector2.zero; vinRt.offsetMax = Vector2.zero;
            _hitVignette = vinGo.GetComponent<Image>();
            _hitVignette.color = new Color(0.32f, 0.06f, 0.05f, 0f);
            _hitVignette.raycastTarget = false;

            // 以撒式过门提示（顶部居中）：清房开门时出现，穿门后收起
            _doorPrompt = NewText(_hud.transform, "DoorPrompt", "石门已开 —— 穿过门洞继续", 26,
                new Color(0.92f, 0.89f, 0.80f, 0.95f), TextAnchor.UpperCenter);
            Stretch((RectTransform)_doorPrompt.transform, 0.2f, 0.8f, 0.80f, 0.90f);
            _doorPrompt.gameObject.SetActive(false);

            // 过门过场黑场（全屏、盖在一切之上、不挡射线）：门洞穿出的水墨转场
            var fadeGo = new GameObject("DoorFade", typeof(Image));
            var fadeRt = (RectTransform)fadeGo.transform;
            fadeRt.SetParent(_hud.transform, false);
            fadeRt.anchorMin = Vector2.zero; fadeRt.anchorMax = Vector2.one;
            fadeRt.offsetMin = Vector2.zero; fadeRt.offsetMax = Vector2.zero;
            _fadeImage = fadeGo.GetComponent<Image>();
            _fadeImage.color = new Color(0.06f, 0.05f, 0.05f, 0f);
            _fadeImage.raycastTarget = false;

            // 主动技能冷却（血条正上方）：就绪高亮 / 冷却灰显倒计时
            _skillText = NewText(_hud.transform, "SkillText", "", 15,
                new Color(0.95f, 0.93f, 0.88f, 0.9f), TextAnchor.MiddleLeft);
            Stretch((RectTransform)_skillText.transform, 0.03f, 0.26f, 0.098f, 0.128f);
        }

        // ==================================================================
        //  UGUI 小工厂
        // ==================================================================

        private static Sprite _brushBar;

        /// <summary>
        /// 程序化笔触条 sprite（256×48，一次生成静态缓存）：
        /// 圆角条 SDF + 上下边缘值噪声扰动 + 左端笔锋收尖 + 右端顿笔。
        /// 给血条用——水墨 HUD 里不该有完美矩形，"毛笔画出来的条"才有纸感。
        /// </summary>
        private static Sprite BrushBarSprite
        {
            get
            {
                if (_brushBar != null) return _brushBar;
                const int w = 256, h = 48;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var rng = new System.Random(20260919);
                float nT = 0f, nB = 0f;
                var top = new float[w]; var bot = new float[w];
                for (int x = 0; x < w; x++)
                {
                    nT = Mathf.Lerp(nT, (float)rng.NextDouble(), 0.18f);   // 平滑随机：毛糙但不锯齿
                    nB = Mathf.Lerp(nB, (float)rng.NextDouble(), 0.18f);
                    top[x] = h * 0.5f - 7f - nT * 7f;
                    bot[x] = h * 0.5f + 7f + nB * 7f;
                }
                var px = new Color32[w * h];
                for (int x = 0; x < w; x++)
                {
                    float taper = Mathf.SmoothStep(0f, 1f, x / (w * 0.14f));  // 左端起笔收尖
                    for (int y = 0; y < h; y++)
                    {
                        float inside = (y > top[x] && y < bot[x]) ? 1f : 0f;
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(inside * taper * 255f));
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                _brushBar = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
                _brushBar.name = "BrushBar";
                return _brushBar;
            }
        }

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
