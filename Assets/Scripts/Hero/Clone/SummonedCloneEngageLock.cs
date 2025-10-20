using System;
using UnityEngine;
using HutongGames.PlayMaker;
using Tooltip = UnityEngine.TooltipAttribute;

/// <summary>
/// 分身（SummonedClone）锁定脚本：当 Alert Range New 检测到“Enemies”层目标时，
/// - 将分身的横向移动速度提升为 1.5 倍；
/// - 持续朝目标方向移动，避免进入 Idle/Turn（通过在 LateUpdate 覆盖速度）直到进入攻击；
/// - 当进入攻击状态或目标离开范围时，解除锁定并恢复正常。
///
/// 设计说明：
/// - 不依赖 Physics2D 扫描；通过 AlertRange + PlayMaker FSM 的 target 变量来锁定目标；
/// - 保留 SummonedCloneCompanionAI 的其他功能（如传送），但通过 LateUpdate 覆写速度来“压制”Idle/Turn；
/// - 进入攻击触发距离时，向 FSM 发送攻击事件（DO_ATTACK），并停止速度覆盖以让攻击接管。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class SummonedCloneEngageLock : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("可选：分身的 AI（用于共存与参数参考），留空也可工作。")] private SummonedCloneCompanionAI companionAI;
    [SerializeField, Tooltip("用于检测 Enemies 的 AlertRange 组件（必须为 isTrigger 的碰撞体）。如果为空会在子物体中自动查找。")] private AlertRange alertRange;
    [SerializeField, Tooltip("分身的刚体（自动获取）。")] private Rigidbody2D body;

    [Header("Lock 行为")]
    [SerializeField, Tooltip("锁定期间的基础移动速度（在此基础上乘以倍数）。")]
    private float baseLockMoveSpeed = 7.5f;

    [SerializeField, Tooltip("锁定时的速度倍数。")] private float speedMultiplier = 1.5f;
    [SerializeField, Tooltip("在锁定期间于 LateUpdate 覆写刚体速度，避免进入 Idle/Turn。")] private bool overrideVelocityInLateUpdate = true;
    [SerializeField, Tooltip("翻转 X 缩放以面向移动方向。")] private bool faceMoveDirection = true;

    [Header("AlertRange 设置")]
    [SerializeField, Tooltip("脚本启动时自动将 AlertRange 切换为 detectEnemies=true。")] private bool forceDetectEnemies = true;

    [Header("PlayMaker 集成（目标与攻击）")] 
    [SerializeField, Tooltip("分身根对象上的 FSM 名称（例如 SummonedCloneV2 为 'Mantis'）。")] private string rootFsmName = "Mantis";
    [SerializeField, Tooltip("FSM 中保存当前锁定目标的 GameObject 变量名（例如 'target'）。")] private string targetVariableName = "target";
    [SerializeField, Tooltip("FSM 中表示“正在攻击”的布尔变量名（例如 'isAttacking'）。")] private string attackBoolVarName = "isAttacking";
    [SerializeField, Tooltip("进入攻击触发距离时要发送的事件名（例如 'DO_ATTACK'）。")] private string attackEventName = "DO_ATTACK";
    [SerializeField, Tooltip("触发攻击的水平距离阈值（与 DO_ATTACK 事件配合）。")] private float attackTriggerDistance = 2.5f;
    [SerializeField, Tooltip("当 FSM 指示正在攻击时，自动解除锁定并停止速度覆盖。")]
    private bool releaseOnAttack = true;

    [Header("Detection Gate (Can See Enemies)")]
    [SerializeField, Tooltip("锁定条件参考 FSM 的“can see enemies”布尔变量。为真时触发/维持锁定。")]
    private bool requireCanSeeEnemies = true;
    [SerializeField, Tooltip("FSM 中表示“能看见敌人”的布尔变量名（例如 'can see enemies'）。")] private string canSeeEnemiesVarName = "can see enemies";

    private PlayMakerFSM rootFsm;
    private FsmGameObject fsmTargetGo;
    private FsmBool fsmAttackBool;
    private FsmBool fsmCanSeeEnemies;

    // 运行时
    private bool isLocked;
    private int lastFacing = 1; // 1=右,-1=左
    private float wantedSpeedX;

    private void Awake()
    {
        if (!body) body = GetComponent<Rigidbody2D>();
        if (!companionAI) companionAI = GetComponent<SummonedCloneCompanionAI>();
        if (!alertRange)
        {
            alertRange = AlertRange.Find(gameObject, childName: null) ?? GetComponentInChildren<AlertRange>();
        }
        if (alertRange && forceDetectEnemies)
        {
            alertRange.SetDetectEnemies(true);
        }
    }

    private void Start()
    {
        CacheFsm();
    }

    private void OnEnable()
    {
        isLocked = false;
        wantedSpeedX = 0f;
    }

    private void Update()
    {
        bool targetDetectedByRange = alertRange != null && alertRange.IsHeroInRange; // 名称沿用 AlertRange，detectEnemies=true 时代表敌人进入
        bool hasTargetInFsm = fsmTargetGo != null && fsmTargetGo.Value != null;
        bool canSeeEnemies = fsmCanSeeEnemies != null && fsmCanSeeEnemies.Value;
        bool targetDetected = requireCanSeeEnemies ? canSeeEnemies : (canSeeEnemies || targetDetectedByRange || hasTargetInFsm);

        if (!isLocked)
        {
            if (targetDetected)
            {
                isLocked = true;
            }
            return;
        }

        // 锁定中：根据 FSM 的目标推进
        GameObject target = GetCurrentTarget();
        if (target == null)
        {
            // 目标丢失：若范围也无敌人，则解除锁定
            if (!targetDetectedByRange)
            {
                DisengageLock();
                return;
            }
            // 范围有敌人但 FSM 未赋值，保持锁定但不覆写速度
            wantedSpeedX = 0f;
            return;
        }

        if (requireCanSeeEnemies && !canSeeEnemies)
        {
            DisengageLock();
            return;
        }

        float dx = target.transform.position.x - transform.position.x;
        float absDx = Mathf.Abs(dx);
        int facing = dx >= 0f ? 1 : -1;
        lastFacing = facing;

        // 进入攻击触发距离：发送攻击事件并在攻击期间停止速度覆盖
        if (absDx <= attackTriggerDistance)
        {
            if (rootFsm != null && (fsmAttackBool == null || !fsmAttackBool.Value))
            {
                rootFsm.SendEvent(attackEventName);
            }
            if (!releaseOnAttack)
            {
                wantedSpeedX = 0f; // 攻击起手时就不再推移动
            }
        }

        bool isAttacking = fsmAttackBool != null && fsmAttackBool.Value;
        if ((releaseOnAttack && isAttacking) || !targetDetected)
        {
            DisengageLock();
            return;
        }

        wantedSpeedX = baseLockMoveSpeed * speedMultiplier * facing;

        if (faceMoveDirection)
        {
            var ls = transform.localScale;
            ls.x = Mathf.Abs(ls.x) * (facing >= 0 ? 1 : -1);
            transform.localScale = ls;
        }
    }

    private void LateUpdate()
    {
        if (!overrideVelocityInLateUpdate || !isLocked) return;

        bool isAttacking = fsmAttackBool != null && fsmAttackBool.Value;
        if (releaseOnAttack && isAttacking)
        {
            return; // 攻击期间不强推速度
        }

        float vy = body.velocity.y; // 保留垂直
        body.velocity = new Vector2(wantedSpeedX, vy);
    }

    private void DisengageLock()
    {
        isLocked = false;
        wantedSpeedX = 0f;
    }

    private void CacheFsm()
    {
        rootFsm = FSMUtility.LocateFSM(transform.root.gameObject, rootFsmName);
        if (rootFsm != null)
        {
            fsmTargetGo = rootFsm.FsmVariables.GetFsmGameObject(targetVariableName);
            fsmAttackBool = rootFsm.FsmVariables.GetFsmBool(attackBoolVarName);
            fsmCanSeeEnemies = rootFsm.FsmVariables.GetFsmBool(canSeeEnemiesVarName);
        }
    }

    private GameObject GetCurrentTarget()
    {
        if (rootFsm == null)
        {
            CacheFsm();
        }
        return fsmTargetGo != null ? fsmTargetGo.Value : null;
    }
}