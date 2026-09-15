using System;
using UnityEngine;

namespace InkWash.Enemies
{
    /// <summary>
    /// 房间控制器：监听波次生成器，**清空所有波次后开门**（把挡路的石门降下去 + 关掉它的碰撞）。
    ///
    /// 为什么把"开门"单列成组件、不塞进 <see cref="WaveSpawner"/>：
    /// 波次是"战斗节奏"，开门是"关卡进度"，两者变化的原因完全不同
    /// （前者跟着数值/难度调，后者跟着关卡结构调）。捆在一起会让后面加
    /// 「回廊/宝库/BOSS 房」时到处改同一个类。
    /// </summary>
    public class RoomController : MonoBehaviour
    {
        public WaveSpawner spawner;

        [Header("门（清空后下沉开启）")]
        public Transform[] gates;
        public float gateOpenDepth = 3.2f;
        public float gateOpenDuration = 1.0f;

        [Header("诊断（只读）")]
        [SerializeField] private bool _isCleared;
        [SerializeField] private float _clearTime = -1f;
        [SerializeField] private float _gateProgress;
        [SerializeField] private int _openedGateCount;

        public bool IsCleared => _isCleared;
        public float ClearTime => _clearTime;
        public float GateProgress => _gateProgress;
        public int OpenedGateCount => _openedGateCount;

        /// <summary>清空时抛一次（Sprint 5 的 Roguelike 流程会订阅它推进房间）。</summary>
        public event Action Cleared;

        private Vector3[] _gateStart;
        private float _openTimer = -1f;

        private void Awake()
        {
            if (spawner == null) spawner = FindObjectOfType<WaveSpawner>();
            if (gates != null)
            {
                _gateStart = new Vector3[gates.Length];
                for (int i = 0; i < gates.Length; i++)
                    if (gates[i] != null) _gateStart[i] = gates[i].localPosition;
            }
        }

        private void OnEnable()
        {
            if (spawner != null) spawner.AllWavesCleared += OnAllCleared;
        }

        private void OnDisable()
        {
            if (spawner != null) spawner.AllWavesCleared -= OnAllCleared;
        }

        // 订阅晚于 Start 的情况（例如房间控制器后启用）兜一下
        private void Start()
        {
            if (spawner != null) spawner.AllWavesCleared -= OnAllCleared;   // 去重
            if (spawner != null) spawner.AllWavesCleared += OnAllCleared;
        }

        private void OnAllCleared()
        {
            if (_isCleared) return;
            _isCleared = true;
            _clearTime = Time.time;
            _openTimer = 0f;

            // 碰撞立刻关掉：不能让玩家"看着门开了但撞上去"
            if (gates != null)
                foreach (var g in gates)
                {
                    if (g == null) continue;
                    foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                }

            Debug.Log("[RoomController] 房间清空 —— 开门（" + (gates != null ? gates.Length : 0) + " 扇）");
            try { Cleared?.Invoke(); }
            catch (Exception e) { Debug.LogError("[RoomController] Cleared 订阅者抛异常（已隔离）：" + e); }
        }

        /// <summary>
        /// 把房间复位到「未清空」状态（验收脚本每轮开始前调用，避免上一轮的开门状态串味）。
        /// 注意要**同时**复位门的 localPosition 与 Collider.enabled —— 只复位状态而门还沉在地下，
        /// 下一轮就会拍到「门是开着的但 IsCleared=false」这种自相矛盾的画面。
        /// </summary>
        public void ResetForTest()
        {
            _isCleared = false;
            _clearTime = -1f;
            _openTimer = -1f;
            _gateProgress = 0f;
            _openedGateCount = 0;

            if (gates == null || _gateStart == null) return;
            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i] == null) continue;
                gates[i].localPosition = _gateStart[i];
                foreach (var c in gates[i].GetComponentsInChildren<Collider>(true)) c.enabled = true;
            }
        }

        private void Update()
        {
            if (_openTimer < 0f || gates == null || _gateStart == null) return;

            _openTimer += Time.deltaTime;
            float k = gateOpenDuration <= 0f ? 1f : Mathf.Clamp01(_openTimer / gateOpenDuration);
            // 缓出：门是"沉下去"的，不是"匀速掉下去"
            float e = 1f - Mathf.Pow(1f - k, 3f);
            _gateProgress = e;
            int opened = 0;
            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i] == null) continue;
                opened++;
                var p = _gateStart[i];
                p.y -= gateOpenDepth * e;
                gates[i].localPosition = p;
            }
            _openedGateCount = opened;

            if (k >= 1f) _openTimer = -1f;
        }
    }
}
