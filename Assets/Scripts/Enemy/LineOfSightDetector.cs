using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LineOfSightDetector : MonoBehaviour
{
    [SerializeField] private AlertRange[] alertRanges;
    private bool canSeePlayer;
    private bool canSeeEnemy;

    // Target options: support tracking Player and Enemies simultaneously
    [Header("Target")]
    [SerializeField] private bool detectPlayers = true; // 默认追踪玩家
    [SerializeField] private bool detectEnemies = false; // 可选：追踪敌人
    [SerializeField] private bool logVisibleTargetName = false; // 调试：在 Console 打印当前可见目标名称
    private Transform lastLoggedTarget;
    private VisibleTargetType lastLoggedType = VisibleTargetType.None;

    // Expose nearest visible enemy for AI consumers (e.g., CloneWalker)
    private Transform nearestVisibleEnemy;
    public Transform NearestVisibleEnemy { get { return canSeeEnemy ? nearestVisibleEnemy : null; } }

    // 新增：敌人扫描配置（基于 LayerMask，而非 HealthManager 组件）
    [Header("Enemies Scan")]
    [SerializeField] private LayerMask enemyLayer; // 建议设置为 "Enemies" 层
    [SerializeField] private float enemySearchRadius = 100f; // 敌人搜索半径

    // Backward compatibility: keep existing property name
    public bool CanSeeHero
    {
        get { return canSeePlayer; }
    }

    // New properties for clarity
    public bool CanSeePlayer { get { return canSeePlayer; } }
    public bool CanSeeEnemies { get { return canSeeEnemy; } }
    public bool CanSeeAnyTarget { get { return (detectPlayers && canSeePlayer) || (detectEnemies && canSeeEnemy); } }

    public enum VisibleTargetType { None, Player, Enemy }
    public VisibleTargetType CurrentTargetType { get; private set; } = VisibleTargetType.None;
    public Transform CurrentVisibleTarget { get; private set; }

    protected void Awake()
    {
        // 当未在 Inspector 指定时，默认使用 "Enemies" 层
        if (enemyLayer.value == 0)
        {
            enemyLayer = LayerMask.GetMask("Enemies");
        }
    }

    protected void Update()
    {
        // 当 alertRanges 配置存在且仅开启了单一检测模式时，保留“类型一致”的范围门控；
        // - 仅检测玩家：使用 IsPlayerInRange；仅检测敌人：使用 IsEnemyInRange；
        // - 同时检测玩家与敌人：不做范围门控（两者分别计算可见性）。
        bool anyPlayerInRange = false;
        bool anyEnemyInRange = false;
        for (int i = 0; i < alertRanges.Length; i++)
        {
            AlertRange alertRange = alertRanges[i];
            if (alertRange == null) continue;
            if (detectPlayers && alertRange.IsPlayerInRange) anyPlayerInRange = true;
            if (detectEnemies && alertRange.IsEnemyInRange) anyEnemyInRange = true;
        }
        if (alertRanges.Length != 0 && (detectPlayers ^ detectEnemies))
        {
            // 单一模式下，类型化门控
            if (detectPlayers && !detectEnemies && !anyPlayerInRange)
            {
                canSeePlayer = false;
                CurrentVisibleTarget = null;
                CurrentTargetType = VisibleTargetType.None;
                if (logVisibleTargetName) Debug.Log("[LoS] Gate: no Player in AlertRange; skip player detection");
                return;
            }
            if (detectEnemies && !detectPlayers && !anyEnemyInRange)
            {
                canSeeEnemy = false;
                CurrentVisibleTarget = null;
                CurrentTargetType = VisibleTargetType.None;
                if (logVisibleTargetName) Debug.Log("[LoS] Gate: no Enemy in AlertRange; skip enemy detection");
                return;
            }
        }

        Transform nearestEnemy = null;

        // 玩家视线检测
        if (detectPlayers)
        {
            HeroController instance = HeroController.instance;
            if (instance == null)
            {
                canSeePlayer = false;
            }
            else
            {
                Vector2 origin = transform.position;
                Vector2 target = instance.transform.position;
                Vector2 dir = (target - origin).normalized;
                float dist = (target - origin).magnitude;
                if (Physics2D.Raycast(origin, dir, dist, LayerMask.GetMask("Terrain")))
                {
                    canSeePlayer = false;
                }
                else
                {
                    canSeePlayer = true;
                }
                Debug.DrawLine(origin, target, canSeePlayer ? Color.green : Color.yellow);
            }
        }

        // 敌人视线检测
        if (detectEnemies)
        {
            nearestEnemy = FindNearestEnemy(enemySearchRadius);
            if (nearestEnemy == null)
            {
                canSeeEnemy = false;
                if (logVisibleTargetName) Debug.Log($"[LoS] No enemy found in radius {enemySearchRadius}");
            }
            else
            {
                Vector2 origin = transform.position;
                Vector2 target = nearestEnemy.position;
                Vector2 dir = (target - origin).normalized;
                float dist = (target - origin).magnitude;
                if (Physics2D.Raycast(origin, dir, dist, LayerMask.GetMask("Terrain")))
                {
                    canSeeEnemy = false;
                    if (logVisibleTargetName) Debug.Log($"[LoS] Enemy {nearestEnemy.name} blocked by terrain");
                }
                else
                {
                    canSeeEnemy = true;
                    if (logVisibleTargetName) Debug.Log($"[LoS] Enemy {nearestEnemy.name} visible at distance {dist:F2}");
                }
                Debug.DrawLine(origin, target, canSeeEnemy ? Color.cyan : Color.yellow);
            }
            // Cache nearest visible enemy for consumers
            nearestVisibleEnemy = canSeeEnemy ? nearestEnemy : null;
        }

        // 选择当前可见目标：当两者都开启时，选距离更近的可见目标
        CurrentVisibleTarget = null;
        CurrentTargetType = VisibleTargetType.None;
        if (detectPlayers && canSeePlayer && HeroController.instance != null)
        {
            CurrentVisibleTarget = HeroController.instance.transform;
            CurrentTargetType = VisibleTargetType.Player;
        }
        if (detectEnemies && canSeeEnemy && nearestEnemy != null)
        {
            if (CurrentVisibleTarget == null)
            {
                CurrentVisibleTarget = nearestEnemy;
                CurrentTargetType = VisibleTargetType.Enemy;
            }
            else
            {
                float playerSqr = (CurrentVisibleTarget.position - transform.position).sqrMagnitude;
                float enemySqr = (nearestEnemy.position - transform.position).sqrMagnitude;
                if (enemySqr < playerSqr)
                {
                    CurrentVisibleTarget = nearestEnemy;
                    CurrentTargetType = VisibleTargetType.Enemy;
                }
            }
        }

        // 调试：名称日志，仅在变化时打印，避免刷屏
        if (logVisibleTargetName)
        {
            if (CurrentVisibleTarget != lastLoggedTarget || CurrentTargetType != lastLoggedType)
            {
                if (CurrentVisibleTarget != null)
                {
                    Debug.Log($"[LoS] Visible {CurrentTargetType}: {CurrentVisibleTarget.name}");
                }
                else
                {
                    Debug.Log("[LoS] No visible target");
                }
                lastLoggedTarget = CurrentVisibleTarget;
                lastLoggedType = CurrentTargetType;
            }
        }
    }

    private Transform FindNearestEnemy(float radius)
    {
        Vector3 myPos = transform.position;
        float maxDist = radius > 0f ? radius : enemySearchRadius;
        int mask = enemyLayer.value != 0 ? enemyLayer.value : LayerMask.GetMask("Enemies");
        Collider2D[] hits = Physics2D.OverlapCircleAll(myPos, maxDist, mask);
        Transform nearest = null;
        float nearestSqr = maxDist * maxDist;
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            var go = col.attachedRigidbody ? col.attachedRigidbody.gameObject : col.gameObject;
            if (!go.activeInHierarchy) continue;
            // 跳过自己
            if (go.transform == transform || go.transform.IsChildOf(transform)) continue;
            float sqr = (go.transform.position - myPos).sqrMagnitude;
            if (sqr <= nearestSqr)
            {
                nearestSqr = sqr;
                nearest = go.transform;
            }
        }
        return nearest;
    }

    public void SetDetectEnemies(bool value)
    {
        detectEnemies = value;
    }

    public void SetDetectPlayers(bool value)
    {
        detectPlayers = value;
    }
}
