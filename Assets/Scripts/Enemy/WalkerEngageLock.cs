using System;
using UnityEngine;
using HutongGames.PlayMaker;

/// <summary>
/// 当 AlertRange 检测到目标时，临时锁定 Walker：
/// - 将 Walker 的行走速度提升为原来的 1.5 倍；
/// - 持续朝目标方向前进，避免进入 Idle(Stopped) 和 Turn 状态；
/// - 当进入攻击状态时（PlayMaker FSM 布尔变量为真）或目标离开范围时，解除锁定并恢复原速。
/// 
/// 使用方法：
/// 1) 将本脚本挂到拥有 Walker 的敌人根对象上；
/// 2) 在同一对象或其子物体上放置 AlertRange 组件（触发器 Collider2D），用于检测 Hero（或根据 AlertRange 配置）；
/// 3) 可选：配置 PlayMaker 根 FSM 名称与“正在攻击”的布尔变量名，以便在攻击开始时解除锁定；
/// </summary>
[RequireComponent(typeof(Walker))]
public class WalkerEngageLock : MonoBehaviour
{
    [Header("References")]
    [SerializeField, UnityEngine.Tooltip("要控制的 Walker 组件。默认自动获取。")]
    private Walker walker;

    [SerializeField, UnityEngine.Tooltip("用于感知目标的 AlertRange 组件。如果为空将自动在子物体中查找第一个 AlertRange。")]
    private AlertRange alertRange;

    [Header("Lock 设置")]
    [SerializeField, UnityEngine.Tooltip("锁定时的速度倍数。")]
    private float speedMultiplier = 1.5f;

    [SerializeField, UnityEngine.Tooltip("锁定期间持续打断 Turn 并强制行走，以避免进入 Idle 和 Turn。")]
    private bool blockIdleAndTurn = true;

    [Header("PlayMaker 攻击检测（可选）")]
    [SerializeField, UnityEngine.Tooltip("当 FSM 指示进入攻击状态时自动解除锁定。")]
    private bool releaseOnAttack = true;

    [SerializeField, UnityEngine.Tooltip("根 FSM 名称（例如 ZombieSheildControl）。用于查找攻击布尔变量。")]
    private string rootFsmName = "ZombieSheildControl";

    [SerializeField, UnityEngine.Tooltip("FSM 中表示“正在攻击”的布尔变量名（例如 isAttacking）。")]
    private string attackBoolVarName = "isAttacking";

    private PlayMakerFSM rootFsm;
    private FsmBool attackBool;

    // 运行时数据
    private float originalSpeedL;
    private float originalSpeedR;
    private bool isLocked;
    private HeroController hero;

    private void Awake()
    {
        if (walker == null) walker = GetComponent<Walker>();
        if (alertRange == null)
        {
            // 优先在子物体中查找第一个 AlertRange
            alertRange = AlertRange.Find(gameObject, childName: null);
            if (alertRange == null)
            {
                alertRange = GetComponentInChildren<AlertRange>();
            }
        }
        hero = HeroController.instance;
    }

    private void Start()
    {
        CacheFsm();
    }

    private void OnEnable()
    {
        // 防御性恢复（避免残留状态）
        if (isLocked)
        {
            RestoreWalkerSpeed();
            isLocked = false;
        }
    }

    private void OnDisable()
    {
        if (isLocked) RestoreWalkerSpeed();
        isLocked = false;
    }

    private void OnDestroy()
    {
        if (isLocked) RestoreWalkerSpeed();
        isLocked = false;
    }

    private void Update()
    {
        bool targetDetected = alertRange != null && alertRange.IsHeroInRange;

        if (!isLocked)
        {
            if (targetDetected)
            {
                EngageLock();
            }
            return;
        }

        // 已锁定：持续保持面向目标方向并行走
        int facing = DetermineFacingToHero();
        if (facing != 0)
        {
            walker.ChangeFacing(facing);
        }

        if (blockIdleAndTurn)
        {
            // 每帧打断 Turn，并确保在 Walking（若被 Idle 置换则重新开始移动）
            walker.CancelTurn();
            walker.StartMoving();
            // 轻推一次 Go，确保状态机回到 Walking 且方向正确
            walker.Go(facing == 0 ? 1 : facing);
        }

        // 解除锁定条件：攻击中 或 目标离开范围
        if ((releaseOnAttack && IsAttacking()) || !targetDetected)
        {
            DisengageLock();
        }
    }

    private void EngageLock()
    {
        if (walker == null) return;
        isLocked = true;

        // 记录并提升速度
        originalSpeedL = walker.walkSpeedL;
        originalSpeedR = walker.walkSpeedR;
        walker.walkSpeedL = originalSpeedL * speedMultiplier;
        walker.walkSpeedR = originalSpeedR * speedMultiplier;

        // 初次对齐并开始移动
        int facing = DetermineFacingToHero();
        if (facing != 0)
        {
            walker.ChangeFacing(facing);
        }
        walker.CancelTurn();
        walker.StartMoving();
    }

    private void DisengageLock()
    {
        RestoreWalkerSpeed();
        isLocked = false;
    }

    private void RestoreWalkerSpeed()
    {
        if (walker == null) return;
        walker.walkSpeedL = originalSpeedL;
        walker.walkSpeedR = originalSpeedR;
    }

    private int DetermineFacingToHero()
    {
        if (hero == null) hero = HeroController.instance;
        if (hero == null) return 0;
        return hero.transform.position.x > transform.position.x ? 1 : -1;
    }

    private void CacheFsm()
    {
        if (!releaseOnAttack) return;
        rootFsm = FSMUtility.LocateFSM(transform.root.gameObject, rootFsmName);
        if (rootFsm != null)
        {
            attackBool = rootFsm.FsmVariables.GetFsmBool(attackBoolVarName);
        }
    }

    private bool IsAttacking()
    {
        if (!releaseOnAttack) return false;
        if (rootFsm == null || attackBool == null)
        {
            CacheFsm();
        }
        return attackBool != null && attackBool.Value;
    }
}