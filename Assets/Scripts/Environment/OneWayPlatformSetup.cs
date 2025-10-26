using UnityEngine;

/// <summary>
/// 给平台添加/配置 PlatformEffector2D，实现单向平台（从下方穿过，上方可站立）。
/// 使用方法：将该脚本挂到平台 GameObject 上（带 Collider2D 或 Tilemap+CompositeCollider2D）。
/// 注意：Collider2D/CompositeCollider2D 需要勾选 Used By Effector。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class OneWayPlatformSetup : MonoBehaviour
{
    [Header("Platform Effector 设置")]
    [SerializeField, Tooltip("启用单向平台功能")] private bool useOneWay = true;
    [SerializeField, Tooltip("有效表面弧度（建议 180），平台上表面为有效面")] private float surfaceArc = 180f;
    [SerializeField, Tooltip("将所有子碰撞器作为一个整体处理")] private bool useOneWayGrouping = true;

    [Header("辅助设置（可选）")]
    [SerializeField, Tooltip("在编辑器变更时自动应用设置")] private bool autoApplyOnValidate = true;

    private void Reset()
    {
        ApplySettings();
    }

    private void OnValidate()
    {
        if (autoApplyOnValidate)
        {
            ApplySettings();
        }
    }

    /// <summary>
    /// 应用单向平台设置：
    /// - Collider2D/CompositeCollider2D 的 Used By Effector = true
    /// - 添加或获取 PlatformEffector2D 并应用属性
    /// </summary>
    public void ApplySettings()
    {
        // 支持 Tilemap 上的 CompositeCollider2D
        var compCol = GetComponent<CompositeCollider2D>();
        if (compCol != null)
        {
            compCol.usedByEffector = true;
        }

        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.usedByEffector = true;
        }

        var eff = GetComponent<PlatformEffector2D>();
        if (eff == null)
        {
            eff = gameObject.AddComponent<PlatformEffector2D>();
        }
        eff.useOneWay = useOneWay;
        eff.surfaceArc = surfaceArc;
        eff.useOneWayGrouping = useOneWayGrouping;

        // 额外提示：如果你希望此平台参与 HeroController 的落地事件，
        // 请在 Inspector 手动将 Tag 设置为 "HeroWalkable"（需在 Tags and Layers 中先定义）。
        // 这里不自动设置 Tag，避免工程未定义标签时报错。
    }
}