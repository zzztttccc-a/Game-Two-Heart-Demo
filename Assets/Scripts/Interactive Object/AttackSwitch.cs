using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可被近战/伤害命中触发的机关：
/// - 实现 IHitResponder.Hit，接入 DamageEnemies -> HitTaker 的命中流程。
/// - 命中后对绑定对象执行位置/旋转修改，并按规则启用/禁用其子对象。
/// - 支持一次性触发或来回切换（Toggle）。
/// 使用方法：
/// 1) 将本脚本挂到一个拥有 Collider2D 的物体（机关碰撞体）上；该物体不要放在被 DamageEnemies 忽略的层（如 Player 等）。
/// 2) 确保玩家攻击的 DamageEnemies 攻击碰撞体能与该机关的 Collider2D 发生触发；
/// 3) 在 Inspector 中配置 BoundTarget 列表、位置/旋转修改方式与子对象启停规则。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AttackSwitch : MonoBehaviour, IHitResponder
{
    [Header("触发设置")]
    [Tooltip("是否每次攻击在 开/关 两个状态间切换；关闭时仅第一次命中执行，后续忽略。")]
    public bool toggleOnEachHit = true;

    [Tooltip("仅响应近战（Nail）类型的攻击；关闭则任何 HitInstance 都响应。")]
    public bool onlyNailAttack = false;

    [Tooltip("命中后的执行延迟秒数（可为0）")]
    public float delaySeconds = 0f;

    [Tooltip("命中防连击冷却（秒），避免一次攻击判定多次触发")]
    public float cooldown = 0.1f;

    [Header("绑定目标与动作")]
    public List<BoundTarget> targets = new List<BoundTarget>();

    [Header("动画设置")]
    [Tooltip("是否使用平滑动画过渡到目标位置/旋转")] public bool smoothAnimation = true;
    [Tooltip("动画时长（秒）")] public float animationDuration = 0.25f;
    [Tooltip("动画曲线（0-1）")] public AnimationCurve animationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // 运行时状态
    private bool isOn = false; // 当前是否处于“已触发”态（用于toggle回滚）
    private float lastHitTime = -999f;

    public void Hit(HitInstance damageInstance)
    {
        // 过滤攻击类型（可选）
        if (onlyNailAttack && damageInstance.AttackType != AttackTypes.Nail)
        {
            return;
        }

        // 冷却
        if (Time.time - lastHitTime < cooldown)
        {
            return;
        }
        lastHitTime = Time.time;

        if (delaySeconds > 0f)
        {
            StartCoroutine(DelayedExecute());
        }
        else
        {
            Execute();
        }
    }

    private System.Collections.IEnumerator DelayedExecute()
    {
        yield return new WaitForSeconds(delaySeconds);
        Execute();
    }

    private void Execute()
    {
        if (toggleOnEachHit)
        {
            if (!isOn)
            {
                ApplyForward();
                isOn = true;
            }
            else
            {
                ApplyReverse();
                isOn = false;
            }
        }
        else
        {
            if (!isOn)
            {
                ApplyForward();
                isOn = true;
            }
            // 非切换模式：后续命中不再执行
        }
    }

    /// <summary>
    /// 外部触发一次性开关（无需攻击命中），例如按钮或机关逻辑调用。
    /// - 若 toggleOnEachHit=false：仅在首次调用时生效，之后不再执行。
    /// - 若 toggleOnEachHit=true：每次调用在开/关之间来回切换。
    /// </summary>
    public void TriggerOnce()
    {
        // 绕过 onlyNailAttack 与冷却，直接执行（可选加延迟）
        if (delaySeconds > 0f)
        {
            StartCoroutine(DelayedExecute());
        }
        else
        {
            Execute();
        }
    }

    private void ApplyForward()
    {
        foreach (var t in targets)
        {
            if (t == null || t.target == null) continue;
            // 缓存原始状态（仅第一次）
            if (!t.cached)
            {
                t.CacheOriginal();
            }

            // 平滑/即时应用位置与旋转
            ApplyTransformWithAnimation(t, forward: true);

            // 子对象启停
            ApplyChildToggle(t, forward: true);
        }
    }

    private void ApplyReverse()
    {
        foreach (var t in targets)
        {
            if (t == null || t.target == null) continue;
            if (!t.cached) continue;

            // 平滑/即时回滚位置与旋转
            ApplyTransformWithAnimation(t, forward: false);
        
            // 子对象回滚
            ApplyChildToggle(t, forward: false);
        }
    }

    private void ApplyChildToggle(BoundTarget t, bool forward)
    {
        if (t.childToggleMode == ChildToggleMode.None) return;
        var childCount = t.target.childCount;
        if (!t.cached)
        {
            t.CacheOriginalChildren(childCount);
        }

        switch (t.childToggleMode)
        {
            case ChildToggleMode.EnableAll:
                for (int i = 0; i < childCount; i++)
                {
                    if (forward)
                        t.target.GetChild(i).gameObject.SetActive(true);
                    else
                        t.target.GetChild(i).gameObject.SetActive(t.originalChildrenActive[i]);
                }
                break;

            case ChildToggleMode.DisableAll:
                for (int i = 0; i < childCount; i++)
                {
                    if (forward)
                        t.target.GetChild(i).gameObject.SetActive(false);
                    else
                        t.target.GetChild(i).gameObject.SetActive(t.originalChildrenActive[i]);
                }
                break;

            case ChildToggleMode.EnableByNames:
                for (int i = 0; i < childCount; i++)
                {
                    var child = t.target.GetChild(i).gameObject;
                    bool match = Array.Exists(t.childNames ?? Array.Empty<string>(), name => child.name == name);
                    if (forward)
                        child.SetActive(match);
                    else
                        child.SetActive(t.originalChildrenActive[i]);
                }
                break;

            case ChildToggleMode.DisableByNames:
                for (int i = 0; i < childCount; i++)
                {
                    var child = t.target.GetChild(i).gameObject;
                    bool match = Array.Exists(t.childNames ?? Array.Empty<string>(), name => child.name == name);
                    if (forward)
                        child.SetActive(!match);
                    else
                        child.SetActive(t.originalChildrenActive[i]);
                }
                break;

            case ChildToggleMode.EnableByIndices:
                for (int i = 0; i < childCount; i++)
                {
                    bool match = Array.Exists(t.childIndices ?? Array.Empty<int>(), idx => idx == i);
                    if (forward)
                        t.target.GetChild(i).gameObject.SetActive(match);
                    else
                        t.target.GetChild(i).gameObject.SetActive(t.originalChildrenActive[i]);
                }
                break;

            case ChildToggleMode.DisableByIndices:
                for (int i = 0; i < childCount; i++)
                {
                    bool match = Array.Exists(t.childIndices ?? Array.Empty<int>(), idx => idx == i);
                    if (forward)
                        t.target.GetChild(i).gameObject.SetActive(!match);
                    else
                        t.target.GetChild(i).gameObject.SetActive(t.originalChildrenActive[i]);
                }
                break;
        }
    }

    [Serializable]
    public class BoundTarget
    {
        [Tooltip("要被修改的目标（Transform）")] public Transform target;
        [Tooltip("空间：Self=本地坐标/旋转；World=世界坐标/旋转")] public Space space = Space.Self;

        [Header("位置设置")]
        public PositionMode positionMode = PositionMode.None;
        [Tooltip("当位置模式为SetAbsolute时使用")] public Vector3 targetPosition;
        [Tooltip("当位置模式为AddOffset时使用")] public Vector3 positionOffset;

        [Header("旋转设置（Z轴）")]
        public RotationMode rotationMode = RotationMode.None;
        [Tooltip("当旋转模式为SetZ时使用")] public float targetRotationZ;
        [Tooltip("当旋转模式为AddDeltaZ时使用")] public float rotationDeltaZ;

        [Header("子对象启停")]
        public ChildToggleMode childToggleMode = ChildToggleMode.None;
        [Tooltip("用于按名称启停的列表")] public string[] childNames;
        [Tooltip("用于按索引启停的列表")] public int[] childIndices;

        // 运行时缓存
        [NonSerialized] public bool cached;
        [NonSerialized] public Vector3 originalLocalPosition;
        [NonSerialized] public Vector3 originalWorldPosition;
        [NonSerialized] public float originalRotationZ;
        [NonSerialized] public bool[] originalChildrenActive;
        [NonSerialized] public Coroutine animCoroutine;

        public void CacheOriginal()
        {
            cached = true;
            originalLocalPosition = target.localPosition;
            originalWorldPosition = target.position;
            var euler = (space == Space.Self) ? target.localEulerAngles : target.eulerAngles;
            originalRotationZ = euler.z;
        }

        public void CacheOriginalChildren(int childCount)
        {
            if (originalChildrenActive != null && originalChildrenActive.Length == childCount) return;
            originalChildrenActive = new bool[childCount];
            for (int i = 0; i < childCount; i++)
            {
                originalChildrenActive[i] = target.GetChild(i).gameObject.activeSelf;
            }
        }
    }

    public enum PositionMode { None, SetAbsolute, AddOffset }
    public enum RotationMode { None, SetZ, AddDeltaZ }
    public enum ChildToggleMode { None, EnableAll, DisableAll, EnableByNames, DisableByNames, EnableByIndices, DisableByIndices }

    private void ApplyTransformWithAnimation(BoundTarget t, bool forward)
    {
        bool doPos = t.positionMode != PositionMode.None;
        bool doRot = t.rotationMode != RotationMode.None;

        if (!doPos && !doRot) return;

        // 计算起点
        Vector3 startPos = (t.space == Space.Self) ? t.target.localPosition : t.target.position;
        float startRotZ = ((t.space == Space.Self) ? t.target.localEulerAngles : t.target.eulerAngles).z;

        // 计算终点（前进或回滚）
        Vector3 endPos = startPos;
        float endRotZ = startRotZ;

        if (forward)
        {
            // 位置目标
            if (doPos)
            {
                if (t.positionMode == PositionMode.SetAbsolute)
                {
                    endPos = t.targetPosition;
                }
                else if (t.positionMode == PositionMode.AddOffset)
                {
                    endPos = startPos + t.positionOffset;
                }
            }
            // 旋转目标
            if (doRot)
            {
                if (t.rotationMode == RotationMode.SetZ)
                {
                    endRotZ = t.targetRotationZ;
                }
                else if (t.rotationMode == RotationMode.AddDeltaZ)
                {
                    endRotZ = startRotZ + t.rotationDeltaZ;
                }
            }
        }
        else
        {
            // 回滚到缓存的原始位置与旋转
            if (doPos)
            {
                endPos = (t.space == Space.Self) ? t.originalLocalPosition : t.originalWorldPosition;
            }
            if (doRot)
            {
                endRotZ = t.originalRotationZ;
            }
        }

        // 停止之前的动画协程
        if (t.animCoroutine != null)
        {
            StopCoroutine(t.animCoroutine);
            t.animCoroutine = null;
        }

        if (!smoothAnimation || animationDuration <= 0f)
        {
            // 即时应用
            if (doPos)
            {
                if (t.space == Space.Self) t.target.localPosition = endPos; else t.target.position = endPos;
            }
            if (doRot)
            {
                var euler = (t.space == Space.Self) ? t.target.localEulerAngles : t.target.eulerAngles;
                euler.z = endRotZ;
                if (t.space == Space.Self) t.target.localEulerAngles = euler; else t.target.eulerAngles = euler;
            }
            return;
        }

        // 平滑动画协程（同时处理位置与旋转）
        t.animCoroutine = StartCoroutine(AnimateTransformRoutine(t, startPos, endPos, startRotZ, endRotZ));
    }

    private System.Collections.IEnumerator AnimateTransformRoutine(BoundTarget t, Vector3 startPos, Vector3 endPos, float startRotZ, float endRotZ)
    {
        float elapsed = 0f;
        float duration = Mathf.Max(0.0001f, animationDuration);
        while (elapsed < duration)
        {
            float k = Mathf.Clamp01(elapsed / duration);
            float curveK = (animationCurve != null) ? animationCurve.Evaluate(k) : k;

            // 插值位置
            Vector3 pos = Vector3.Lerp(startPos, endPos, curveK);
            if (t.space == Space.Self) t.target.localPosition = pos; else t.target.position = pos;

            // 插值旋转Z
            float rotZ = Mathf.LerpAngle(startRotZ, endRotZ, curveK);
            var euler = (t.space == Space.Self) ? t.target.localEulerAngles : t.target.eulerAngles;
            euler.z = rotZ;
            if (t.space == Space.Self) t.target.localEulerAngles = euler; else t.target.eulerAngles = euler;

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 结束时确保到达目标
        if (t.space == Space.Self)
        {
            t.target.localPosition = endPos;
            var e = t.target.localEulerAngles; e.z = endRotZ; t.target.localEulerAngles = e;
        }
        else
        {
            t.target.position = endPos;
            var e = t.target.eulerAngles; e.z = endRotZ; t.target.eulerAngles = e;
        }

        t.animCoroutine = null;
    }
}