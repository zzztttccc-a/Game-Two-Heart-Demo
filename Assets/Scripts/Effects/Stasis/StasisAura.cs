using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StasisAura（定身术范围触发器）：
/// - 以自身为圆心，对指定 LayerMask 下的对象施加“定身/减速”效果，持续 t 秒；
/// - 非侵入式：不依赖敌人具体脚本，只冻结刚体与动画；
/// - Boss 抗性：若目标带有 StasisResistance 组件，可配置为免疫或仅减速。
/// 使用方法：
/// 1) 将本组件挂到一个空物体或效果预制体上，设置 radius / duration / targetLayers。
/// 2) 勾选 autoTriggerOnEnable 以便在启用时立即触发一次；
/// 3) 可在脚本或动画事件中调用 TriggerStasis() 主动施放。
/// </summary>
public class StasisAura : MonoBehaviour
{
    [Header("Stasis 范围与时长")]
    [Tooltip("以玩家/施放者为圆心的半径。单位：世界坐标。")]
    public float radius = 8f;

    [Tooltip("定身/减速的持续时间（秒）。")]
    public float duration = 2f;

    [Header("目标筛选")]
    [Tooltip("需要作用的 Layer。建议包含 Enemies / Interactive Object 等图层。")]
    public LayerMask targetLayers;

    [Tooltip("启用时自动触发一次定身效果。适合直接作为一次性的法术预制体使用。")]
    public bool autoTriggerOnEnable = true;

    [Header("Boss 抗性（无 StasisResistance 组件时的兜底配置）")]
    [Tooltip("用于标签兜底判断 Boss（可选）。如果目标的 tag 在此列表中，则默认使用减速而非完全冻结。")]
    public List<string> bossTagsFallback = new List<string> { "Boss" };

    [Range(0.05f, 1f)]
    [Tooltip("Boss 兜底减速倍率（0~1），仅用于没有 StasisResistance 组件时的默认减速。")]
    public float bossSlowFactorFallback = 0.2f;

    private readonly HashSet<GameObject> _affectedOnce = new HashSet<GameObject>();

    [Header("高级冻结选项（应用到目标的 StasisTarget）")]
    [Tooltip("使用 Animator.enabled=false 进行硬暂停，避免触发切换导致的一帧动画闪烁。")]
    public bool useHardAnimatorPause = true;

    [Tooltip("在冻结期间锁定物体的世界位置/旋转与本地缩放，防止脚本直接改 Transform 导致位移。")]
    public bool lockTransformDuringFreeze = true;

    [Tooltip("在冻结期间暂停 PlayMakerFSM（通过反射方式），用于脚本驱动位移/AI 的对象。开启可能影响受击逻辑，谨慎使用。")]
    public bool pausePlayMakerFSM = false;

    [Tooltip("冻结期间锁定所有子 Transform 的局部位姿，实现完整层级锁定。")]
    public bool lockChildrenTransformsDuringFreeze = true;

    [Tooltip("冻结期间冻结所有子刚体（Rigidbody2D），而不仅仅是根刚体。")]
    public bool freezeAllRigidbodiesInChildren = true;

    [Header("Debug")]
    [Tooltip("将调试日志传递到被作用的 StasisTarget 上，以便定位重复施法问题。")]
    public bool enableDebugLogging = false;

    private void OnEnable()
    {
        if (autoTriggerOnEnable)
        {
            TriggerStasis();
        }
    }

    /// <summary>
    /// 主动触发定身效果。会对范围内符合 LayerMask 的对象施加 StasisTarget。
    /// </summary>
    public void TriggerStasis()
    {
        try
        {
            var hits = Physics2D.OverlapCircleAll(transform.position, radius, targetLayers);
            _affectedOnce.Clear();

            foreach (var hit in hits)
            {
                if (hit == null) continue;

                // 尽量作用于对象的根节点，避免对子节点重复处理。
                var target = hit.transform.root != null ? hit.transform.root.gameObject : hit.gameObject;
                if (target == null) continue;
                if (_affectedOnce.Contains(target)) continue;
                _affectedOnce.Add(target);

                // 检查抗性组件
                var resistance = target.GetComponent<StasisResistance>();

                bool freezeFully = true;
                float slowFactor = 1f;

                if (resistance != null)
                {
                    switch (resistance.Reaction)
                    {
                        case StasisReaction.Immune:
                            // 免疫：跳过
                            continue;
                        case StasisReaction.Slow:
                            freezeFully = false;
                            slowFactor = Mathf.Clamp(resistance.SlowFactor, 0.05f, 1f);
                            break;
                        case StasisReaction.Freeze:
                            freezeFully = true;
                            break;
                    }
                }
                else
                {
                    // 无抗性组件时的兜底：若标签命中 Boss 列表，则使用减速，否则完全冻结。
                    if (bossTagsFallback != null && bossTagsFallback.Count > 0 && bossTagsFallback.Contains(target.tag))
                    {
                        freezeFully = false;
                        slowFactor = Mathf.Clamp(bossSlowFactorFallback, 0.05f, 1f);
                    }
                }

                var stasis = target.GetComponent<StasisTarget>();
                if (stasis == null)
                {
                    stasis = target.AddComponent<StasisTarget>();
                }

                // 将高级冻结选项传递给目标组件
                stasis.UseHardAnimatorPause = useHardAnimatorPause;
                stasis.LockTransformDuringFreeze = lockTransformDuringFreeze;
                stasis.PausePlayMakerFSM = pausePlayMakerFSM;
                stasis.LockChildrenTransformsDuringFreeze = lockChildrenTransformsDuringFreeze;
                stasis.FreezeAllRigidbodiesInChildren = freezeAllRigidbodiesInChildren;
                stasis.EnableDebugLogging = enableDebugLogging;

                stasis.Apply(duration, freezeFully, slowFactor);
            }
        }
        catch (System.Exception ex)
        {
            if (enableDebugLogging)
            {
                Debug.LogError($"[StasisAura] TriggerStasis exception: {ex}");
            }
        }
        // 不再执行自动回收或销毁，交由外部（FSM/脚本）自行管理对象生命周期
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}