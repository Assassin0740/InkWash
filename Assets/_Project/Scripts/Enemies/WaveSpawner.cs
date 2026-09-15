using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace InkWash.Enemies
{
    [Serializable]
    public class EnemySpawnEntry
    {
        public GameObject prefab;
        public int count = 3;
    }

    [Serializable]
    public class Wave
    {
        public string label = "波次";
        [Tooltip("这一波开打前的等待（给玩家喘息/让上一波尸体消失）")]
        public float delayBefore = 1.5f;
        public EnemySpawnEntry[] entries = new EnemySpawnEntry[0];
    }

    /// <summary>
    /// 波次生成器：按配置刷怪 → 等这一波清空 → 刷下一波 → 全部清空后抛事件。
    ///
    /// 两个刻意的设计：
    /// 1) **用事件统计存活数，不每帧扫场景**（见 <see cref="EnemyBase.DiedEvent"/>）。
    /// 2) **生成点必须过 `NavMesh.SamplePosition`**：直接按坐标放下会在导航网格之外，
    ///    表现是"敌人站着不动、什么都不做"，而且控制台**一条报错都没有**（NavMeshAgent
    ///    只是把 `isOnNavMesh` 置 false，不抛异常）。这类静默失败一定要在源头挡掉。
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
        [Header("生成点（留空则自动在场地内取样）")]
        public Transform[] spawnPoints;

        [Header("自动取样参数（spawnPoints 为空时生效）")]
        public Vector3 spawnCenter = Vector3.zero;
        public float spawnRadius = 9f;
        [Tooltip("离玩家至少这么远才出生（别贴脸刷怪）")]
        public float minDistanceToPlayer = 5f;
        [Tooltip("黄金角取点：最多试几个候选（要过「在导航网格上 + 有路到玩家」两道校验）")]
        public int spawnAttempts = 24;

        /// <summary>黄金角 137.5°：连续取点会自然铺开成一圈，不会聚在同一个方向。</summary>
        private const float GoldenAngleDeg = 137.5f;

        [Header("波次")]
        public Wave[] waves;
        public bool autoStart = true;
        [Tooltip("同一波内两只怪的出生间隔")]
        public float spawnInterval = 0.4f;

        [Header("诊断（只读）")]
        [SerializeField] private int _currentWave = -1;
        [SerializeField] private int _waveStartedCount;
        [SerializeField] private int _waveClearedCount;
        [SerializeField] private int _spawnedCount;
        [SerializeField] private int _spawnFailedCount;
        [SerializeField] private float _waveTimer;
        [SerializeField] private bool _allCleared;

        public int CurrentWave => _currentWave;
        public int WaveStartedCount => _waveStartedCount;
        public int WaveClearedCount => _waveClearedCount;
        public int SpawnedCount => _spawnedCount;
        public int SpawnFailedCount => _spawnFailedCount;
        public bool AllCleared => _allCleared;
        public int AliveEnemies => _alive.Count;
        public float WaveTimer => _waveTimer;
        public int WaveCount => waves != null ? waves.Length : 0;

        public event Action<int> WaveStarted;
        public event Action<int> WaveCleared;
        public event Action AllWavesCleared;

        private readonly List<EnemyBase> _alive = new List<EnemyBase>();
        private int _spawnCursor;
        private int _pointCursor;
        private readonly List<GameObject> _pending = new List<GameObject>();
        private bool _running;

        public void ResetDiagnostics()
        {
            _currentWave = -1; _waveStartedCount = 0; _waveClearedCount = 0;
            _spawnedCount = 0; _spawnFailedCount = 0; _waveTimer = 0f; _allCleared = false;
        }

        /// <summary>
        /// 把波次状态机整个复位（验收脚本每轮开始前调用）。
        ///
        /// 为什么必须有这个：`--runtime` 跑完**不会自动退出 Play**，所以第二个脚本很可能
        /// 落在**同一个还活着的 Play 会话**里 —— 那时 `_running=true`、`_allCleared=true`，
        /// `Begin()` 直接 return，于是"一只怪都没刷出来"却一条报错也没有。
        /// 这类静默失败浪费的是整轮调试，必须在源头挡住。
        /// </summary>
        public void ResetForTest()
        {
            _running = false;
            _allCleared = false;
            _currentWave = -1;
            _waveTimer = 0f;
            _spawnTimer = 0f;
            _pending.Clear();
            _alive.Clear();
            _spawnCursor = 0;
            _pointCursor = 0;
            _spawnedCount = 0;
            _spawnFailedCount = 0;
            _waveStartedCount = 0;
            _waveClearedCount = 0;
        }

        private void Start()
        {
            if (autoStart) Begin();
        }

        /// <summary>开始跑波次（也可以由房间控制器在开门后调用）。</summary>
        public void Begin()
        {
            if (_running) return;
            _running = true;
            _currentWave = -1;
            _waveTimer = 0f;
            NextWave();
        }

        private void NextWave()
        {
            _currentWave++;
            if (waves == null || _currentWave >= waves.Length)
            {
                _allCleared = true;
                SafeInvoke(AllWavesCleared);
                return;
            }
            _waveTimer = waves[_currentWave].delayBefore;
            _waveStartedCount++;
            SafeInvoke(WaveStarted, _currentWave);
        }

        private void Update()
        {
            if (!_running || _allCleared) return;
            if (_currentWave < 0 || waves == null || _currentWave >= waves.Length) return;

            // 阶段 A：等延迟
            if (_waveTimer > 0f)
            {
                _waveTimer -= Time.deltaTime;
                if (_waveTimer > 0f) return;
                BuildPending();
            }

            // 阶段 B：逐个出生
            if (_pending.Count > 0)
            {
                _spawnTimer -= Time.deltaTime;
                if (_spawnTimer <= 0f)
                {
                    _spawnTimer = Mathf.Max(0.02f, spawnInterval);
                    SpawnOne(_pending[_pending.Count - 1]);
                    _pending.RemoveAt(_pending.Count - 1);
                }
                return;
            }

            // 阶段 C：这一波清空了 → 下一波
            if (_alive.Count == 0)
            {
                _waveClearedCount++;
                SafeInvoke(WaveCleared, _currentWave);
                NextWave();
            }
        }

        private float _spawnTimer;

        private void BuildPending()
        {
            var w = waves[_currentWave];
            if (w.entries != null)
                foreach (var e in w.entries)
                {
                    if (e == null || e.prefab == null) continue;
                    for (int i = 0; i < Mathf.Max(0, e.count); i++) _pending.Add(e.prefab);
                }
            _spawnTimer = 0f;
        }

        private void SpawnOne(GameObject prefab)
        {
            Vector3 pos = PickSpawnPosition();

            // 朝向玩家出生。**这不是妆点，是必需**：早期版本用 Quaternion.identity，
            // 于是"站在场地另一头、正好背对玩家"的敌人永远进不了视野锥（sightAngleDeg 只有 220°），
            // 表现为"一只怪都不动"，而控制台干干净净 —— 没有任何东西提示你是朝向的问题。
            // 波次怪本来就该是"冲着你来"的。
            Vector3 look = Combat.PlayerRef.Exists ? Combat.PlayerRef.Position - pos : Vector3.forward;
            look.y = 0f;
            Quaternion rot = look.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(look.normalized, Vector3.up)
                : Quaternion.identity;

            var go = Instantiate(prefab, pos, rot);
            go.name = prefab.name + "_" + _spawnedCount;
            var enemy = go.GetComponent<EnemyBase>();
            if (enemy == null) enemy = go.GetComponentInChildren<EnemyBase>();
            if (enemy != null)
            {
                enemy.DiedEvent += OnEnemyDied;
                _alive.Add(enemy);
            }
            _spawnedCount++;
        }

        private void OnEnemyDied(EnemyBase e)
        {
            _alive.Remove(e);
        }

        private Vector3 PickSpawnPosition()
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                var t = spawnPoints[_pointCursor % spawnPoints.Length];
                _pointCursor++;
                Vector3 p = t != null ? t.position : spawnCenter;
                p.y = spawnCenter.y;
                NavMeshHit ph;
                if (NavMesh.SamplePosition(p, out ph, 2.5f, NavMesh.AllAreas))
                    return ph.position + Vector3.up * 0.05f;
                _spawnFailedCount++;
                Debug.LogWarning("[WaveSpawner] 预设出生点不在导航网格上: " + p);
                return p;
            }

            Vector3 playerPos = Combat.PlayerRef.Exists ? Combat.PlayerRef.Position : spawnCenter;

            // 绕**房间中心**取点，而不是绕玩家画圈。
            //
            // 这里踩过一个代价很大的坑：早期写法是 `player + dir * spawnRadius`，即把圆**以玩家为圆心**。
            // 玩家一旦不在房间正中（本关玩家就在 z=-3），圆上就会有点落到**石门外面**；
            // 敌人被 `SamplePosition` 拉上导航网格后隔着石门看不见玩家（石门挡住 Linecast），
            // 于是整波敌人在原地站着，控制台一条报错都没有。
            // 「出生点属于地图」—— 所以圆心必须是地图，玩家只用来做"别贴脸"的约束。
            for (int i = 0; i < spawnAttempts; i++)
            {
                float ang = (_spawnCursor++ * GoldenAngleDeg) * Mathf.Deg2Rad;
                Vector3 want = spawnCenter + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * spawnRadius;
                want.y = spawnCenter.y;

                // 离玩家太近就往外推（仍以玩家为参照，但只推"距离"，不改"归属"）
                Vector3 flat = want - playerPos;
                flat.y = 0f;
                if (flat.sqrMagnitude > 1e-4f && flat.magnitude < minDistanceToPlayer)
                    want = playerPos + flat.normalized * minDistanceToPlayer;
                want.y = spawnCenter.y;

                NavMeshHit h;
                // 采样半径收窄到 2 m：给 6 m 的宽容度会把"墙外的点"也拉到墙上，
                // 得到"在导航网格上但过不来"的假通过。
                if (!NavMesh.SamplePosition(want, out h, 2.0f, NavMesh.AllAreas)) continue;
                Vector3 pos = h.position;

                // 真正要验的不是"在导航网格上"，而是"**能不能走到玩家**"。
                // 隔着一堵墙的另一侧同样在导航网格上，但敌人永远过不来（贴着墙原地打转）。
                if (Combat.PlayerRef.Exists && !HasPathTo(pos, playerPos)) continue;

                return pos + Vector3.up * 0.05f;
            }

            _spawnFailedCount++;
            Debug.LogWarning("[WaveSpawner] 找不到「有路且离玩家够远」的出生点，回退到房间中心: " + spawnCenter);
            return spawnCenter + Vector3.up * 0.05f;
        }

        /// <summary>从 from 到 to 是否有一条**完整**的导航路径（PathComplete 才算通）。</summary>
        private static bool HasPathTo(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path)) return false;
            return path.status == NavMeshPathStatus.PathComplete;
        }

        private void SafeInvoke(Action<int> evt, int arg)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action<int>)d).Invoke(arg); }
                catch (Exception e) { Debug.LogError("[WaveSpawner] 订阅者抛异常（已隔离）：" + e); }
            }
        }

        private void SafeInvoke(Action evt)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action)d).Invoke(); }
                catch (Exception e) { Debug.LogError("[WaveSpawner] 订阅者抛异常（已隔离）：" + e); }
            }
        }
    }
}
