using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;
using Tooltip = UnityEngine.TooltipAttribute;

/// <summary>
/// SummonedClone 的同伴 AI：
/// 1) 警戒状态：周围没有敌人时原地不动；与玩家距离超出 guardDistance 时自动靠近玩家。
/// 2) 支援状态：检测玩家是否在战斗（HeroController.cState.attacking 或玩家附近有敌人），在战斗时向玩家移动直至到达支援距离。
/// 3) 距离传送：当分身与玩家距离超过 teleportDistance 时，自动传送到玩家边间（带偏移），并带有传送冷却避免连续触发。
///
/// 脚本仅控制水平移动与朝向（保留垂直速度）。
/// 可选使用 tk2dSpriteAnimator 播放 idle / walk / turn 动画。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class SummonedCloneCompanionAI : MonoBehaviour
{
    public enum CompanionState
    {
        Alert,      // 警戒/闲置（距玩家过远则跟随）
        Support,    // 支援（玩家战斗时靠近玩家）
        Engage      // 追击（检测到敌人时向敌人移动）
    }

    [Header("References")]
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private tk2dSpriteAnimator animator; // 可选

    private HeroController hero;

    [Header("Detection & Layers")]
    [SerializeField] private LayerMask enemiesMask = 0; // 建议设为 Enemies 层
    [Tooltip("在分身周围用于检测敌人的半径（无敌人则原地不动）")]
    [SerializeField] private float enemyCheckRadius = 6f;
    [Tooltip("在玩家周围用于判断是否存在敌人的半径（辅助判断玩家是否处于战斗环境）")]
    [SerializeField] private float playerEnemyCheckRadius = 8f;

    [Header("Alert (Idle / Follow)")]
    [Tooltip("分身与玩家的警戒距离，超过此距离则分身自动靠近玩家")]
    [SerializeField] private float guardDistance = 8f;
    [Tooltip("警戒状态下的跟随速度")]
    [SerializeField] private float alertMoveSpeed = 6f;

    [Header("Support")]
    [Tooltip("玩家处于战斗状态时的接近距离（到达该距离停止移动）")]
    [SerializeField] private float supportApproachDistance = 4f;
    [Tooltip("玩家战斗时支援移动速度")]
    [SerializeField] private float supportMoveSpeed = 7.5f;
    [Tooltip("是否将玩家附近存在敌人视为战斗环境（除直接攻击标志外）")]
    [SerializeField] private bool considerEnemiesNearPlayerAsCombat = true;

    [Header("Engage (Chase Enemies)")]
    [Tooltip("启用后，当分身附近有敌人时会优先追击敌人")]
    [SerializeField] private bool enableEngageEnemies = true;
    [Tooltip("追击敌人时的停止距离（达到该距离后不再前进）")]
    [SerializeField] private float stopDistanceToEnemy = 2.5f;
    [Tooltip("追击敌人时的移动速度")]
    [SerializeField] private float engageMoveSpeed = 7.5f;

    [Header("Teleport")]
    [Tooltip("与玩家的最大距离，超过后触发传送")]
    [SerializeField] private float teleportDistance = 24f;
    [Tooltip("传送到玩家身边的偏移（会基于玩家朝向自动左右翻转 X 偏移）")]
    [SerializeField] private Vector2 teleportOffset = new Vector2(1.25f, 0f);
    [Tooltip("每次传送之间的最小冷却时间（秒）")]
    [SerializeField] private float teleportCooldown = 1.0f;

    [Header("Animation (Optional)")]
    [SerializeField] private string idleClip = "Idle";
    [SerializeField] private string walkClip = "Walk";
    [SerializeField] private string turnClip = "Turn";
    [Tooltip("移动时根据方向翻转 X 轴（保持美术朝向正确）")]
    [SerializeField] private bool faceMoveDirection = true;

    [Header("Physics")]
    [Tooltip("仅控制水平速度，保留 Rigidbody2D 垂直速度")]
    [SerializeField] private bool keepVerticalVelocity = true;
    [Tooltip("停止时水平速度平滑阻尼（0 为瞬停）")]
    [Range(0f, 1f)]
    [SerializeField] private float stopDamping = 0.2f;

    private CompanionState state = CompanionState.Alert;
    private float lastTeleportTime = -999f;
    private int facing = 1; // 1=右, -1=左
    private HealthManager currentEnemyHM;
    private Transform currentEnemyTransform;

    private void Awake()
    {
        if (!body) body = GetComponent<Rigidbody2D>();
        if (!animator) animator = GetComponent<tk2dSpriteAnimator>();

        hero = HeroController.instance;
        if (!hero)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) hero = go.GetComponent<HeroController>();
        }

        if (enemiesMask == 0)
        {
            // 默认尝试使用名为 "Enemies" 的层
            enemiesMask = LayerMask.GetMask("Enemies");
        }

        // PlayMaker 集成：预取根 FSM 引用
        if (integrateWithPlayMaker)
        {
            rootFsm = FSMUtility.LocateFSM(transform.root.gameObject, rootFsmName);
        }
    }

    private void Update()
    {
        if (!hero) return;

        // 如果 FSM 正在攻击，则优先让路：停止水平移动，避免巡逻/追击抢占
        if (integrateWithPlayMaker && rootFsm != null)
        {
            var isAtkVar = rootFsm.FsmVariables.GetFsmBool(fsmAttackingVarName);
            if (isAtkVar != null && isAtkVar.Value)
            {
                StopAndIdle();
                return;
            }
        }

        Vector2 heroPos = hero.transform.position;
        Vector2 selfPos = transform.position;
        float distToHero = Vector2.Distance(selfPos, heroPos);

        // 3) 距离传送
        if (distToHero > teleportDistance && Time.time - lastTeleportTime >= teleportCooldown)
        {
            TeleportNearHero();
            lastTeleportTime = Time.time;
            return;
        }

        // 检测敌人与战斗状态
        bool enemiesNearClone = HasAliveEnemiesNearby(selfPos, enemyCheckRadius);
        bool heroInCombat = IsHeroInCombat() || (considerEnemiesNearPlayerAsCombat && HasAliveEnemiesNearby(heroPos, playerEnemyCheckRadius));

        // 优先：有敌人在分身附近则进入追击
        if (enableEngageEnemies && enemiesNearClone)
        {
            state = CompanionState.Engage;
            EngageNearestEnemy();
            return;
        }

        // 2) 支援状态
        if (heroInCombat)
        {
            state = CompanionState.Support;
            ApproachHeroUntil(supportApproachDistance, supportMoveSpeed);
            return;
        }

        // 1) 警戒状态（无敌人原地不动；超过警戒距离靠近玩家）
        state = CompanionState.Alert;
        if (!enemiesNearClone)
        {
            if (distToHero > guardDistance)
            {
                ApproachHeroUntil(guardDistance, alertMoveSpeed);
            }
            else
            {
                StopAndIdle();
            }
        }
        else
        {
            // 有敌人在附近但未要求攻击/移动：保持原地（可按需拓展为攻击行为）
            StopAndIdle();
        }
    }

    private bool IsHeroInCombat()
    {
        // 以玩家攻击标志为主（可按需扩展到闪避/蓄力等状态）
        var cs = hero.cState;
        return cs.attacking || cs.upAttacking || cs.downAttacking || cs.altAttack;
    }

    private bool HasAliveEnemiesNearby(Vector2 center, float radius)
    {
        // 检测指定半径内是否有未死亡的敌人（基于 Enemies 层 + HealthManager.isDead）
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, enemiesMask);
        if (hits == null || hits.Length == 0) return false;

        // 去重并检查 HealthManager
        HashSet<HealthManager> checkedHM = new HashSet<HealthManager>();
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D col = hits[i];
            if (!col || !col.gameObject.activeInHierarchy) continue;

            HealthManager hm = col.GetComponentInParent<HealthManager>();
            if (!hm || checkedHM.Contains(hm)) continue;
            checkedHM.Add(hm);

            if (!hm.isDead)
            {
                return true;
            }
        }
        return false;
    }

    private void ApproachHeroUntil(float stopDistance, float speed)
    {
        Vector2 heroPos = hero.transform.position;
        Vector2 selfPos = transform.position;
        float dx = heroPos.x - selfPos.x;
        float absDx = Mathf.Abs(dx);

        if (absDx <= stopDistance)
        {
            StopAndIdle();
            return;
        }

        int dir = dx > 0f ? 1 : -1;
        MoveX(dir, speed);
    }

    private void MoveX(int dir, float speed)
    {
        facing = dir;
        if (faceMoveDirection)
        {
            var ls = transform.localScale;
            ls.x = Mathf.Abs(ls.x) * (dir >= 0 ? 1 : -1);
            transform.localScale = ls;
        }

        float vy = keepVerticalVelocity ? body.velocity.y : 0f;
        body.velocity = new Vector2(dir * speed, vy);

        PlayAnimSafe(walkClip);
    }

    private void StopAndIdle()
    {
        float vy = keepVerticalVelocity ? body.velocity.y : 0f;
        float dampedVx = Mathf.Lerp(body.velocity.x, 0f, stopDamping);
        body.velocity = new Vector2(dampedVx, vy);
        PlayAnimSafe(idleClip);
    }

    private void TeleportNearHero()
    {
        Vector3 heroPos = hero.transform.position;
        // 基于玩家朝向自动决定左右偏移
        float xOffset = teleportOffset.x;
        if (!hero.cState.facingRight) xOffset = -xOffset;
        Vector3 targetPos = new Vector3(heroPos.x + xOffset, heroPos.y + teleportOffset.y, transform.position.z);

        transform.position = targetPos;
        body.velocity = Vector2.zero;
        PlayAnimSafe(idleClip);

        // 传送后朝向与玩家一致
        facing = hero.cState.facingRight ? 1 : -1;
        if (faceMoveDirection)
        {
            var ls = transform.localScale;
            ls.x = Mathf.Abs(ls.x) * (facing >= 0 ? 1 : -1);
            transform.localScale = ls;
        }
    }

    private void PlayAnimSafe(string clip)
    {
        if (!animator || string.IsNullOrEmpty(clip)) return;
        if (!animator.IsPlaying(clip)) animator.Play(clip);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, enemyCheckRadius);

        if (HeroController.instance)
        {
            Gizmos.color = new Color(0.1f, 0.6f, 1f, 0.4f);
            Gizmos.DrawWireSphere(HeroController.instance.transform.position, playerEnemyCheckRadius);
        }
    }

    private Transform FindNearestAliveEnemy(Vector2 center, float radius)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, enemiesMask);
        Transform nearest = null;
        float bestDist = float.PositiveInfinity;
        if (hits != null)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D col = hits[i];
                if (!col || !col.gameObject.activeInHierarchy) continue;
                HealthManager hm = col.GetComponentInParent<HealthManager>();
                if (!hm || hm.isDead) continue;
                Transform t = hm.transform;
                float d = Vector2.Distance(center, t.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearest = t;
                    currentEnemyHM = hm;
                }
            }
        }
        return nearest;
    }

    [Header("PlayMaker Integration (Priority / Attack)")]
    [Tooltip("启用后，脚本会读取根 FSM 的攻击标志并在攻击期间让移动脚本让路；到达攻击距离时会向 FSM 发送攻击事件。")]
    [SerializeField] private bool integrateWithPlayMaker = true;
    [Tooltip("分身根对象上的 PlayMaker FSM 名称（例如 SummonedCloneV2 为 'Mantis'；老版为 'ZombieSheildControl'）")]
    [SerializeField] private string rootFsmName = "Mantis";
    [Tooltip("FSM 中用于表示正在攻击的布尔变量名（例如 'isAttacking' 或 'attacking'）")]
    [SerializeField] private string fsmAttackingVarName = "isAttacking";
    [Tooltip("在接近敌人至攻击距离时要发送到 FSM 的事件名（例如 'DO_ATTACK'）")]
    [SerializeField] private string fsmAttackEventName = "DO_ATTACK";
    [Tooltip("触发攻击事件的距离阈值（优先于 stopDistanceToEnemy，用于让 PlayMaker 的攻击状态接管）")]
    [SerializeField] private float attackTriggerDistance = 2.5f;

    private PlayMakerFSM rootFsm;

    private void EngageNearestEnemy()
    {
        currentEnemyTransform = FindNearestAliveEnemy(transform.position, enemyCheckRadius);
        if (currentEnemyTransform == null)
        {
            StopAndIdle();
            return;
        }

        Vector2 selfPos = transform.position;
        float dx = currentEnemyTransform.position.x - selfPos.x;
        float absDx = Mathf.Abs(dx);

        // 到达攻击触发距离时，交由 FSM 执行攻击（并停止移动）。
        // 仅在未处于攻击状态时发送事件，避免事件刷屏。
        if (integrateWithPlayMaker && rootFsm != null && absDx <= attackTriggerDistance)
        {
            var isAtkVar = rootFsm.FsmVariables.GetFsmBool(fsmAttackingVarName);
            if (isAtkVar == null || !isAtkVar.Value)
            {
                rootFsm.SendEvent(fsmAttackEventName);
            }
            StopAndIdle();
            return;
        }

        if (absDx <= stopDistanceToEnemy)
        {
            StopAndIdle();
            return;
        }
        int dir = dx > 0f ? 1 : -1;
        MoveX(dir, engageMoveSpeed);
    }
}