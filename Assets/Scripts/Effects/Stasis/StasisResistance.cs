using UnityEngine;

/// <summary>
/// 为目标配置在定身术下的抗性：
/// - Freeze：完全冻结。
/// - Slow：仅减速（动画减速、刚体阻尼增强），slowFactor 控制减速强度。
/// - Immune：免疫（不受影响）。
/// 将该组件添加到 Boss / 精英等对象上即可实现差异化效果。
/// </summary>
public class StasisResistance : MonoBehaviour
{
    [SerializeField]
    private StasisReaction reaction = StasisReaction.Slow;

    [Range(0.05f, 1f)]
    [SerializeField]
    private float slowFactor = 0.2f;

    public StasisReaction Reaction => reaction;
    public float SlowFactor => slowFactor;
}

public enum StasisReaction
{
    Freeze,
    Slow,
    Immune
}