using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Parry范围探测器：用于判断当前角色的“can parry”触发器范围内，是否存在可进行下攻击（下劈）的交互目标。
/// 使用方式：将本脚本挂在带有2D触发器碰撞体（isTrigger=true）的Parry范围物体上（通常是角色的子物体）。
/// 可通过图层或标签来过滤有效目标。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ParryRangeDetector : MonoBehaviour
{
    [Header("目标过滤（任意一种命中即视为有效）")]
    [Tooltip("可交互对象的图层（支持多选；留空表示不按图层过滤）")]
    public LayerMask targetLayers = 0;

    [Tooltip("以层名字列表过滤（例如：Enemy, Interactable）。填写后同样参与命中判定，任意命中即有效。")]
    public string[] targetLayerNames;

    [Tooltip("可交互对象的标签（留空表示不按标签过滤）")]
    public string[] targetTags;

    private readonly HashSet<Collider2D> _targets = new HashSet<Collider2D>();
    private readonly HashSet<int> _targetLayerIndexSet = new HashSet<int>();

    /// <summary>
    /// 当前范围内是否存在可进行下攻击的交互对象
    /// </summary>
    public bool HasTarget => _targets.Count > 0;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnEnable()
    {
        RefreshLayerNameCache();
    }

    private void OnValidate()
    {
        RefreshLayerNameCache();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsValidTarget(other))
        {
            _targets.Add(other);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (_targets.Contains(other))
        {
            _targets.Remove(other);
        }
    }

    private bool IsValidTarget(Collider2D col)
    {
        if (col == null || col.gameObject == null) return false;

        // 图层命中
        if (targetLayers != 0)
        {
            if ((targetLayers.value & (1 << col.gameObject.layer)) != 0)
            {
                return true;
            }
        }

        // 名字列表命中（将名字转换为layer索引缓存后判定）
        if (_targetLayerIndexSet.Count > 0)
        {
            if (_targetLayerIndexSet.Contains(col.gameObject.layer))
            {
                return true;
            }
        }

        // 标签命中
        if (targetTags != null && targetTags.Length > 0)
        {
            var tag = col.gameObject.tag;
            for (int i = 0; i < targetTags.Length; i++)
            {
                if (!string.IsNullOrEmpty(targetTags[i]) && tag == targetTags[i])
                {
                    return true;
                }
            }
        }

        // 若未设置任何过滤条件，则不认为它是有效目标
        return false;
    }

    private void RefreshLayerNameCache()
    {
        _targetLayerIndexSet.Clear();
        if (targetLayerNames == null || targetLayerNames.Length == 0) return;
        for (int i = 0; i < targetLayerNames.Length; i++)
        {
            var name = targetLayerNames[i];
            if (string.IsNullOrEmpty(name)) continue;
            int idx = LayerMask.NameToLayer(name);
            if (idx >= 0)
            {
                _targetLayerIndexSet.Add(idx);
            }
            else
            {
                // 名字无法解析为有效Layer时忽略
            }
        }
    }
}