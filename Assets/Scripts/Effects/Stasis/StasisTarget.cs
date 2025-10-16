using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单体定身/减速状态管理组件：按需附加到目标对象上。
/// - Freeze：完全定身（动画暂停、刚体冻结、速度清零）。
/// - Slow：仅减速（动画速度乘 slowFactor，刚体增加阻尼）。
/// 说明：
/// - 仅对 Animator / Rigidbody2D /（可选）tk2dSpriteAnimator 做通用冻结，
///   不直接停用伤害组件与碰撞体，确保受击仍然有效。
/// - 若目标自身销毁或禁用，本组件会在 OnDisable 时尝试恢复已更改的状态。
/// </summary>
[DefaultExecutionOrder(10000)]
public class StasisTarget : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("输出定身/恢复流程的详细日志以便定位问题（仅在 Editor/Development Build 下使用）。")]
    public bool EnableDebugLogging = false;

    // Animator 暂停与恢复
    private List<Animator> _animators = new List<Animator>();
    private List<float> _animatorOriginalSpeeds = new List<float>();
    private List<bool> _animatorOriginalEnabled = new List<bool>();
    private List<bool> _animatorOriginalApplyRootMotion = new List<bool>();

    // tk2dSpriteAnimator（通过反射或 Behaviour.enabled 进行暂停）
    private List<Behaviour> _tk2dAnimators = new List<Behaviour>();
    private List<bool> _tk2dOriginalEnabled = new List<bool>();

    // PlayMakerFSM（可选暂停，用于脚本驱动的位移/AI）。通过类型名反射避免硬依赖
    private List<Behaviour> _playmakerFSMs = new List<Behaviour>();
    private List<bool> _playmakerOriginalEnabled = new List<bool>();

    // Rigidbody2D 冻结与恢复（仅根节点）
    private Rigidbody2D _rb;
    private Vector2 _rbOriginalVelocity;
    private float _rbOriginalAngularVelocity;
    private RigidbodyConstraints2D _rbOriginalConstraints;
    private float _rbOriginalDrag;
    private float _rbOriginalAngularDrag;

    // 子刚体（可选）
    private List<Rigidbody2D> _childRBs = new List<Rigidbody2D>();
    private List<Vector2> _childRBOriginalVelocity = new List<Vector2>();
    private List<float> _childRBOriginalAngularVelocity = new List<float>();
    private List<RigidbodyConstraints2D> _childRBOriginalConstraints = new List<RigidbodyConstraints2D>();
    private List<float> _childRBOriginalDrag = new List<float>();
    private List<float> _childRBOriginalAngularDrag = new List<float>();

    private bool _applied;
    private bool _freezeFully;
    private bool _slowActive;
    private float _slowFactor = 1f;
    private float _endTime;

    // 首次命中加成：在定身开始时置为可用，命中一次后消费；定身结束后重置为不可用
    private bool _firstHitBonusAvailable;
    public bool FirstHitBonusAvailable => _firstHitBonusAvailable;
    public bool HasActiveStasis => _applied;

    private int _applyCount = 0;

    // 减速时附加的阻尼增强值（经验值，可在此微调整体减速感）
    private const float SlowExtraDrag = 15f;
    private const float SlowExtraAngularDrag = 15f;

    [Header("高级冻结选项")]
    [Tooltip("使用 Animator.enabled=false 进行硬暂停，避免触发切换导致的一帧动画闪烁。")]
    public bool UseHardAnimatorPause = true;

    [Tooltip("在冻结期间锁定物体的世界位置/旋转与本地缩放，防止脚本直接改 Transform 导致位移。")]
    public bool LockTransformDuringFreeze = true;

    [Tooltip("在冻结期间暂停 PlayMakerFSM（通过反射方式），用于脚本驱动位移/AI 的对象。开启可能影响受击逻辑，谨慎使用。")]
    public bool PausePlayMakerFSM = false;

    [Tooltip("冻结期间锁定所有子 Transform 的局部位姿（localPos/localRot/localScale），实现完整层级锁定。")]
    public bool LockChildrenTransformsDuringFreeze = true;

    [Tooltip("冻结期间冻结所有子刚体（Rigidbody2D），而不仅仅是根刚体。")]
    public bool FreezeAllRigidbodiesInChildren = true;

    // 变换锁定缓存
    private Vector3 _freezeWorldPos;
    private Quaternion _freezeWorldRot;
    private Vector3 _freezeLocalScale;

    // 子层级锁定缓存
    private List<Transform> _lockedTransforms = new List<Transform>();
    private List<Vector3> _lockedLocalPositions = new List<Vector3>();
    private List<Quaternion> _lockedLocalRotations = new List<Quaternion>();
    private List<Vector3> _lockedLocalScales = new List<Vector3>();

    /// <summary>
    /// 施加定身/减速效果。可重复调用以刷新持续时间或叠加覆盖（使用更晚的结束时间）。
    /// </summary>
    /// <param name="duration">持续时间（秒）</param>
    /// <param name="freezeFully">true 完全冻结；false 仅减速</param>
    /// <param name="slowFactor">减速倍率（0~1），仅在 freezeFully=false 时使用</param>
    public void Apply(float duration, bool freezeFully, float slowFactor)
    {
        if (EnableDebugLogging)
        {
            Debug.Log($"[StasisTarget] Apply called on '{name}': duration={duration}, freezeFully={freezeFully}, slowFactor={slowFactor}, _applied={_applied}, _endTime(old)={_endTime}");
        }
        _freezeFully = freezeFully;
        _slowFactor = Mathf.Clamp(slowFactor, 0.05f, 1f);
        _endTime = Mathf.Max(_endTime, Time.time + Mathf.Max(0.01f, duration));

        if (!_applied)
        {
            CacheComponents();
            DoApply();
            // 定身首次命中加成在真正应用时启用
            _firstHitBonusAvailable = true;
            _applied = true;
            _applyCount++;
            if (EnableDebugLogging)
            {
                Debug.Log($"[StasisTarget] Applied (#{_applyCount}) on '{name}'. _endTime(new)={_endTime}");
            }
        }
        else
        {
            // 已在定身中，只更新模式与结束时间
            if (_freezeFully)
            {
                // 切换到完全冻结模式
                SwitchToFreeze();
            }
            else
            {
                // 切换到减速模式
                SwitchToSlow();
            }
            if (EnableDebugLogging)
            {
                Debug.Log($"[StasisTarget] Refreshed on '{name}'. _endTime(new)={_endTime}");
            }
        }
    }

    private void Update()
    {
        if (_applied && Time.time >= _endTime)
        {
            if (EnableDebugLogging)
            {
                Debug.Log($"[StasisTarget] Duration ended on '{name}', restoring.");
            }
            Restore();
        }
    }

    private void OnDisable()
    {
        if (_applied)
        {
            if (EnableDebugLogging)
            {
                Debug.Log($"[StasisTarget] OnDisable on '{name}', restoring.");
            }
            Restore();
        }
    }

    private void CacheComponents()
    {
        _animators.Clear();
        _animatorOriginalSpeeds.Clear();
        _animatorOriginalEnabled.Clear();
        // 重要：之前未清空 _animatorOriginalApplyRootMotion，导致多次施放后数据累积、索引错位
        _animatorOriginalApplyRootMotion.Clear();
        GetComponentsInChildren(true, _animators);
        for (int i = 0; i < _animators.Count; i++)
        {
            var anim = _animators[i];
            _animatorOriginalSpeeds.Add(anim.speed);
            _animatorOriginalEnabled.Add(anim.enabled);
            _animatorOriginalApplyRootMotion.Add(anim.applyRootMotion);
        }

        _tk2dAnimators.Clear();
        _tk2dOriginalEnabled.Clear();
        // 通过遍历 Behaviour，筛选名字为 tk2dSpriteAnimator 的组件，避免对 tk2d 产生编译依赖
        // 重要：之前未清空 PlayMaker 相关列表，导致多次施放后列表不断增长，恢复/切换时出现错位
        _playmakerFSMs.Clear();
        _playmakerOriginalEnabled.Clear();
        var behaviours = GetComponentsInChildren<Behaviour>(true);
        foreach (var b in behaviours)
        {
            if (b != null && b.GetType().Name == "tk2dSpriteAnimator")
            {
                _tk2dAnimators.Add(b);
                _tk2dOriginalEnabled.Add(b.enabled);
            }
            // 收集 PlayMakerFSM（可选暂停）
            if (b != null && b.GetType().Name == "PlayMakerFSM")
            {
                _playmakerFSMs.Add(b);
                _playmakerOriginalEnabled.Add(b.enabled);
            }
        }

        _rb = GetComponent<Rigidbody2D>();
        if (_rb != null)
        {
            _rbOriginalVelocity = _rb.velocity;
            _rbOriginalAngularVelocity = _rb.angularVelocity;
            _rbOriginalConstraints = _rb.constraints;
            _rbOriginalDrag = _rb.drag;
            _rbOriginalAngularDrag = _rb.angularDrag;
        }

        // 收集子刚体（可选）
        _childRBs.Clear();
        _childRBOriginalVelocity.Clear();
        _childRBOriginalAngularVelocity.Clear();
        _childRBOriginalConstraints.Clear();
        _childRBOriginalDrag.Clear();
        _childRBOriginalAngularDrag.Clear();
        if (FreezeAllRigidbodiesInChildren)
        {
            var allRBs = GetComponentsInChildren<Rigidbody2D>(true);
            foreach (var crb in allRBs)
            {
                if (crb == _rb) continue; // 根刚体已单独记录
                _childRBs.Add(crb);
                _childRBOriginalVelocity.Add(crb.velocity);
                _childRBOriginalAngularVelocity.Add(crb.angularVelocity);
                _childRBOriginalConstraints.Add(crb.constraints);
                _childRBOriginalDrag.Add(crb.drag);
                _childRBOriginalAngularDrag.Add(crb.angularDrag);
            }
        }
    }

    private void DoApply()
    {
        if (_freezeFully)
        {
            SwitchToFreeze();
        }
        else
        {
            SwitchToSlow();
        }
    }

    /// <summary>
    /// 若当前处于定身/减速状态且首次命中加成可用，则消费并返回 true。
    /// </summary>
    public bool TryConsumeFirstHitBonus()
    {
        if (_applied && _firstHitBonusAvailable)
        {
            _firstHitBonusAvailable = false;
            return true;
        }
        return false;
    }

    private void SwitchToFreeze()
    {
        _slowActive = false;
        if (EnableDebugLogging)
        {
            Debug.Log($"[StasisTarget] SwitchToFreeze on '{name}'");
        }

        // Animator 硬暂停或减速为 0
        for (int i = 0; i < _animators.Count; i++)
        {
            var animator = _animators[i];
            if (animator != null)
            {
                if (UseHardAnimatorPause)
                {
                    animator.enabled = false;
                }
                else
                {
                    animator.speed = 0f;
                }
                // 禁用根运动，避免 Animator 驱动位移
                animator.applyRootMotion = false;
            }
        }

        // tk2d 动画禁用（可视上等同于暂停）
        for (int i = 0; i < _tk2dAnimators.Count; i++)
        {
            var b = _tk2dAnimators[i];
            if (b != null)
            {
                b.enabled = false;
            }
        }

        // PlayMakerFSM 暂停（可选）
        if (PausePlayMakerFSM)
        {
            for (int i = 0; i < _playmakerFSMs.Count; i++)
            {
                var fsm = _playmakerFSMs[i];
                if (fsm != null)
                {
                    fsm.enabled = false;
                }
            }
        }

        // 刚体冻结与速度清零
        if (_rb != null)
        {
            _rb.velocity = Vector2.zero;
            _rb.angularVelocity = 0f;
            _rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        // 冻结所有子刚体
        if (FreezeAllRigidbodiesInChildren)
        {
            for (int i = 0; i < _childRBs.Count; i++)
            {
                var crb = _childRBs[i];
                if (crb == null) continue;
                crb.velocity = Vector2.zero;
                crb.angularVelocity = 0f;
                crb.constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }

        // 锁定变换数据，防止脚本在冻结期间修改位置/旋转/缩放
        if (LockTransformDuringFreeze)
        {
            _freezeWorldPos = transform.position;
            _freezeWorldRot = transform.rotation;
            _freezeLocalScale = transform.localScale;

            if (LockChildrenTransformsDuringFreeze)
            {
                _lockedTransforms.Clear();
                _lockedLocalPositions.Clear();
                _lockedLocalRotations.Clear();
                _lockedLocalScales.Clear();

                var allTransforms = GetComponentsInChildren<Transform>(true);
                foreach (var t in allTransforms)
                {
                    _lockedTransforms.Add(t);
                    _lockedLocalPositions.Add(t.localPosition);
                    _lockedLocalRotations.Add(t.localRotation);
                    _lockedLocalScales.Add(t.localScale);
                }
            }
        }
    }

    private void SwitchToSlow()
    {
        _slowActive = true;
        if (EnableDebugLogging)
        {
            Debug.Log($"[StasisTarget] SwitchToSlow on '{name}', slowFactor={_slowFactor}");
        }

        // Animator 减速
        for (int i = 0; i < _animators.Count; i++)
        {
            var animator = _animators[i];
            if (animator != null)
            {
                animator.speed = _animatorOriginalSpeeds[i] * _slowFactor;
                if (!animator.enabled) animator.enabled = true; // 确保减速时启用
                // 防御：索引越界时保持现有的 applyRootMotion
                if (i < _animatorOriginalApplyRootMotion.Count)
                {
                    animator.applyRootMotion = _animatorOriginalApplyRootMotion[i];
                }
            }
        }

        // tk2d 不做细粒度减速，保持原本动画速度（Boss表现近似不完全停顿）
        for (int i = 0; i < _tk2dAnimators.Count; i++)
        {
            var b = _tk2dAnimators[i];
            if (b != null)
            {
                b.enabled = _tk2dOriginalEnabled[i];
            }
        }

        // 刚体增加阻尼（显著降低移动与旋转）
        if (_rb != null)
        {
            _rb.constraints = _rbOriginalConstraints; // 保持原约束，不直接冻结
            _rb.drag = _rbOriginalDrag + SlowExtraDrag;
            _rb.angularDrag = _rbOriginalAngularDrag + SlowExtraAngularDrag;
        }
    }

    private void Restore()
    {
        if (EnableDebugLogging)
        {
            Debug.Log($"[StasisTarget] Restore on '{name}'");
        }
        // 恢复 Animator 速度
        for (int i = 0; i < _animators.Count; i++)
        {
            var animator = _animators[i];
            if (animator != null)
            {
                animator.speed = _animatorOriginalSpeeds[i];
                animator.enabled = _animatorOriginalEnabled[i];
                if (i < _animatorOriginalApplyRootMotion.Count)
                {
                    animator.applyRootMotion = _animatorOriginalApplyRootMotion[i];
                }
            }
        }

        // 恢复 tk2d 使能状态
        for (int i = 0; i < _tk2dAnimators.Count; i++)
        {
            var b = _tk2dAnimators[i];
            if (b != null)
            {
                b.enabled = _tk2dOriginalEnabled[i];
            }
        }

        // 恢复 PlayMakerFSM 使能状态
        for (int i = 0; i < _playmakerFSMs.Count; i++)
        {
            var fsm = _playmakerFSMs[i];
            if (fsm != null)
            {
                fsm.enabled = _playmakerOriginalEnabled[i];
            }
        }

        // 恢复刚体状态
        if (_rb != null)
        {
            _rb.constraints = _rbOriginalConstraints;
            if (_freezeFully)
            {
                // 完全冻结结束后，恢复到原速度（提供更自然的继续运动），也可选择保持为 0
                _rb.velocity = _rbOriginalVelocity;
                _rb.angularVelocity = _rbOriginalAngularVelocity;
            }
            else if (_slowActive)
            {
                _rb.drag = _rbOriginalDrag;
                _rb.angularDrag = _rbOriginalAngularDrag;
            }
        }

        // 恢复子刚体
        if (FreezeAllRigidbodiesInChildren)
        {
            for (int i = 0; i < _childRBs.Count; i++)
            {
                var crb = _childRBs[i];
                if (crb == null) continue;
                crb.constraints = _childRBOriginalConstraints[i];
                crb.drag = _childRBOriginalDrag[i];
                crb.angularDrag = _childRBOriginalAngularDrag[i];
                // 若为完全冻结，恢复原速度；否则保持当前减速后的速度
                if (_freezeFully)
                {
                    crb.velocity = _childRBOriginalVelocity[i];
                    crb.angularVelocity = _childRBOriginalAngularVelocity[i];
                }
            }
        }

        _applied = false;
        _slowActive = false;
        _freezeFully = false;
        _endTime = 0f;
        _firstHitBonusAvailable = false;
        if (EnableDebugLogging)
        {
            Debug.Log($"[StasisTarget] Restore completed on '{name}'.");
        }

        // 用后可选择移除组件，保持干净（如不希望保留可改为 Destroy(this)）
        // 这里保留组件以便在同一对象上重复施加效果（刷新结束时间）。
    }

    private void LateUpdate()
    {
        // 在冻结期间强制锁定变换，避免脚本直接修改 Transform 导致位移或旋转
        if (_applied && _freezeFully && LockTransformDuringFreeze)
        {
            transform.SetPositionAndRotation(_freezeWorldPos, _freezeWorldRot);
            transform.localScale = _freezeLocalScale;

            if (LockChildrenTransformsDuringFreeze)
            {
                for (int i = 0; i < _lockedTransforms.Count; i++)
                {
                    var t = _lockedTransforms[i];
                    if (t == null) continue; // 可能在冻结期间被销毁
                    t.localPosition = _lockedLocalPositions[i];
                    t.localRotation = _lockedLocalRotations[i];
                    t.localScale = _lockedLocalScales[i];
                }
            }
        }
    }

    private void OnAnimatorMove()
    {
        // 防止 Animator root motion 在冻结期间修改 Transform
        if (_applied && _freezeFully && LockTransformDuringFreeze)
        {
            transform.SetPositionAndRotation(_freezeWorldPos, _freezeWorldRot);
            transform.localScale = _freezeLocalScale;
        }
    }
}