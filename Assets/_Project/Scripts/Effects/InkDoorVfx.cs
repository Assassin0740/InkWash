using UnityEngine;

namespace InkWash.Effects
{
    /// <summary>
    /// 门开启墨涡（第三十八轮 B3）：清空房间、门体下沉的瞬间，
    /// 每个门洞脚下绽一大圈墨 + 留一片墨渍 —— 「路开了」的水墨式宣告。
    ///
    /// 为什么挂在 RunManager 身上（代码装配，不进 prefab）：它订阅的是
    /// RunManager.DoorOpened；房间推进时门 Transform 会随 RoomController 换房间
    /// 复用，读 gates 的时机放在事件回调里，永远拿的是"当前这间房"的门。
    /// </summary>
    [DisallowMultipleComponent]
    public class InkDoorVfx : MonoBehaviour
    {
        [Tooltip("门涡比普通落地墨花大多少倍")]
        public float vortexScale = 2.2f;
        [Tooltip("门下墨渍强度")]
        public float stainStrength = 2.0f;

        private InkWash.Roguelike.RunManager _run;

        private void Start()
        {
            _run = GetComponent<InkWash.Roguelike.RunManager>();
            if (_run == null) _run = GetComponentInParent<InkWash.Roguelike.RunManager>();
            if (_run != null) _run.DoorOpened += OnDoorOpened;
        }

        private void OnDestroy()
        {
            if (_run != null) _run.DoorOpened -= OnDoorOpened;
        }

        private void OnDoorOpened()
        {
            var room = _run != null ? _run.room : null;
            if (room == null || room.gates == null) return;
            foreach (var gate in room.gates)
            {
                if (gate == null) continue;
                var p = gate.position;
                // 门洞中心脚下绽墨（InkHitVfx 内部自己找地面，不抛异常）
                InkHitVfx.SpawnGround(new Vector3(p.x, p.y, p.z), vortexScale);
                // 留一片更浓的墨渍——玩家走过去时脚下正踩着"门开过"的痕迹
                InkStain.Spawn(new Vector3(p.x, p.y, p.z), stainStrength, 6f);
                InkWash.Audio.AudioManager.Play("Swing_Heavy", 0.7f);
            }
        }
    }
}
