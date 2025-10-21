using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Clone_Walker : MonoBehaviour
{
    [Header("Structure")]
    //�����ҵĽű�һ��������
    [SerializeField] private LineOfSightDetector lineOfSightDetector;
    [SerializeField] private AlertRange alertRange; 
    [SerializeField] private AlertRange playerAlertRange; // 新增：玩家用 AlertRange（追踪 Player 的范围门控）
    [SerializeField] private bool requireHeroInPlayerRange = true; // 新增：跟随玩家需要在 Player AlertRange 内
    
    //ÿһ�����˵��ļ���ʽ������rb2d,col2d,animator,audiosource,�ټ�һ������ͷ��heroλ��
    private Rigidbody2D body;
    private Collider2D bodyCollider;
    private tk2dSpriteAnimator animator;
    private AudioSource audioSource;
    private Camera mainCamera;
    private HeroController hero;

    private const float CameraDistanceForActivation = 60f;
    private const float WaitHeroXThreshold = 1f; //�������X�����ϵļ��޾���ֵ

    [Header("Configuration")]
    [SerializeField] private bool ambush; //�Ƿ����
    [SerializeField] private string idleClip; //idle�Ķ���Ƭ������
    [SerializeField] private string turnClip; //turn�Ķ���Ƭ������
    [SerializeField] private string walkClip; //walk�Ķ���Ƭ������
    [SerializeField] private float edgeXAdjuster; //���ǽ��x�ϵ�����ֵ
    [SerializeField] private bool preventScaleChange; //�Ƿ��ֹx���localscale�����仯
    [SerializeField] private bool preventTurn; //�Ƿ���ֹת��
    [SerializeField] private float pauseTimeMin; //ֹͣ������ʱ��
    [SerializeField] private float pauseTimeMax;
    [SerializeField] private float pauseWaitMin; //��·��ʱ��
    [SerializeField] private float pauseWaitMax;
    [SerializeField] private bool pauses = false;  // 禁用随机停走以避免“巡逻”行为
    [SerializeField] private float rightScale; //��ʼʱ��x�᷽��
    [SerializeField] public bool startInactive; //��ʼʱ����Ծ
    [SerializeField] private int turnAfterIdlePercentage; //Idle״̬�������ת��Turn״̬�ĸ���

    [SerializeField] private float turnPause; //����ת������ȴʱ��
    [SerializeField] private bool waitForHeroX; //�Ƿ�ȴ����X����λ
    [SerializeField] private float waitHeroX; //�ȴ����X�������
    [SerializeField] public float walkSpeedL; //������·���ٶ�
    [SerializeField] public float walkSpeedR;//������·���ٶ�
    [SerializeField] public bool ignoreHoles; //�Ƿ���Զ�
    [SerializeField] private bool preventTurningToFaceHero; //��ֹת����ҵ�λ��

    [Header("Chase (Enemies)")]
    [SerializeField] private bool chaseEnemies = true;            // ���������ģʽ
    [SerializeField] private float chaseSpeedMultiplier = 1.5f;   // ��׷��倍��
    [SerializeField] private bool requireEnemyInRange = false;    // ��� AlertRange �����ڷ�Χ��Ż���׷（改为false，允许追击视线内的所有敌人）
    private bool isChasingEnemy;                                   // ���ڱ�־

    [Header("Follow (Player Fallback)")]
    [SerializeField] private bool followPlayerWhenNoEnemy = true;       // ���Χ���޵��ˣ������� Player ����
    [SerializeField] private float followOffsetMin = 1.5f;              // ����λ�õ���Сƫ��
    [SerializeField] private float followOffsetMax = 3.5f;              // ����λ�õ����ƫ��
    [SerializeField] private float followSpeedMultiplier = 1.2f;        // ����ʱ���ٶȱ���
    [SerializeField] private float followRetargetThreshold = 0.25f;     // �������λ��ʣ������ٷ�ֵ�ٴ��趨
    [SerializeField] private float followStopDistance = 0.6f;           // �������Hero X���룬�����ͻ�Idle
    // 新增：跟随侧重新择位冷却（秒）
    [SerializeField] private float followRetargetCooldown = 1.5f;
    private float followRetargetCooldownRemaining = 0f;
    // 调试
    [Header("Debug")]

    [SerializeField] private bool debugFollow = true;
    [SerializeField] private bool alwaysFaceHero = true; // 无敌人/跟随时基于X位置总是面向Hero
    // 新增：玩家靠近后的 Idle 时长与恢复距离
    [SerializeField] private float followStopIdleMin = 1.5f;            // 玩家附近 Idle 的最小时长
    [SerializeField] private float followStopIdleMax = 3.0f;            // 玩家附近 Idle 的最大时长
    [SerializeField] private float followResumeDistance = 1.0f;         // 玩家离开到该距离后恢复跟随
    // 新增：玩家不在 playerAlertRange 时，传送到玩家附近（避免分身在远处卡住）
    [SerializeField] private bool teleportNearHeroWhenOutOfPlayerRange = true;
    [SerializeField] private float teleportCooldown = 1.5f;
    // 新增：Idle 偏好配置
    [Header("Idle Preference")] 
    [SerializeField] private int idleStayPercentage = 85; // 停止结束后继续停留的概率（默认 85%）
    [SerializeField] private bool preferIdleAfterTurn = true; // 转身结束后优先进入停止（在非追击/跟随时）
    private bool isFollowingPlayer;
    private float playerFollowTargetX;
    private int playerFollowSide;  // -1 or 1
    // Home return when followPlayerWhenNoEnemy=false
    [SerializeField] private float homeArriveThreshold = 0.3f;
    private Vector3 homePosition;
    private bool hasHomePosition;
    private bool isReturningHome;

    [SerializeField] private States state;
    [SerializeField] private StopReasons stopReason;
    private bool didFulfilCameraDistanceCondition; //��ʱû���õ�
    private bool didFulfilHeroXCondition; //��ʱû���õ�
    private int currentFacing;//Debug��ʱ�������ǰ��Ӹ�[SerializeField]
    private int turningFacing;
    //������ʱ���ҹ���˼��
    private float walkTimeRemaining;
    private float pauseTimeRemaining;
    private float turnCooldownRemaining;
    // 新增：传送冷却时间累计
    private float teleportCooldownRemaining;

    protected void Awake()
    {
	body = GetComponent<Rigidbody2D>();
	bodyCollider = GetComponent<BoxCollider2D>();
	animator = GetComponent<tk2dSpriteAnimator>();
	audioSource = GetComponent<AudioSource>();
    }

    protected void Start()
    {
	mainCamera = Camera.main;
	hero = HeroController.instance;
	if(currentFacing == 0)
	{
	    currentFacing = ((transform.localScale.x * rightScale >= 0f) ? 1 : -1); //�����-1���ұ���1
	}
	if(state == States.NotReady)
	{
	    turnCooldownRemaining = -Mathf.Epsilon;
	    BeginWaitingForConditions();
	}
    // Record home when follow disabled
    if (!followPlayerWhenNoEnemy)
    {
        homePosition = transform.position;
        hasHomePosition = true;
        DebugFollowLog($"[Home] Record start pos: {homePosition.x:F2},{homePosition.y:F2}");
    }
    }

    /// <summary>
    /// ���Ǵ�����һ��״̬������Ϊ����״̬��ÿһ�ֶ���Update��Stop�ķ�����
    /// </summary>
    protected void Update()
    {
	turnCooldownRemaining -= Time.deltaTime;
	teleportCooldownRemaining = Mathf.Max(0f, teleportCooldownRemaining - Time.deltaTime);
    // 新增：跟随重选冷却递减
    followRetargetCooldownRemaining = Mathf.Max(0f, followRetargetCooldownRemaining - Time.deltaTime);
	if (TryTeleportNearHero()) { return; }
	switch (state)
	{
	    case States.WaitingForConditions:
		UpdateWaitingForConditions();
		break;
	    case States.Stopped:
		UpdateStopping();
		break;
	    case States.Walking:
		UpdateWalking();
		break;
	    case States.Turning:
		UpdateTurning();
		break;
	    default:
		break;
	}
    }

    /// <summary>
    /// ��Waiting״̬���뿪ʼ�ƶ�״̬(��һ����WalkҲ������Turn)
    /// </summary>
    public void StartMoving()
    {
	if(state == States.Stopped || state == States.WaitingForConditions)
	{
	    startInactive = false;
	    int facing;
	    if(currentFacing == 0)
	    {
		facing = UnityEngine.Random.Range(0, 2) == 0 ? -1 : 1;
	    }
	    else
	    {
		facing = currentFacing;
	    }
	    BeginWalkingOrTurning(facing);
	}
	Update();
    }

    /// <summary>
    /// ����Ҫʱȡ��ת��
    /// </summary>
    public void CancelTurn()
    {
	if(state == States.Turning)
	{
	    BeginWalking(currentFacing);
	}
    }

    public void Go(int facing)
    {
	turnCooldownRemaining = -Time.deltaTime;
	if(state == States.Stopped || state == States.Walking)
	{
	    BeginWalkingOrTurning(facing);
	}
	else if(state == States.Turning && currentFacing == facing)
	{
	    CancelTurn();
	}
	Update();
    }

    public void RecieveGoMessage(int facing)
    {
	if(state != States.Stopped || stopReason != StopReasons.Controlled)
	{
	    Go(facing);
	}
    }

    // 新增：检测是否存在可见敌人，并根据 requireEnemyInRange 进行范围门控
    private bool EnemyDetected(out Transform enemy)
    {
        enemy = null;
        if (!chaseEnemies || lineOfSightDetector == null)
        {
            DebugFollowLog($"[EnemyDetected] Early exit: chaseEnemies={chaseEnemies}, LoS={(lineOfSightDetector != null ? "OK" : "NULL")}");
            return false;
        }
        // 可见性判定
        if (!lineOfSightDetector.CanSeeEnemies)
        {
            DebugFollowLog($"[EnemyDetected] CanSeeEnemies=false");
            return false;
        }
        // 范围门控（需要至少一个敌人在 AlertRange 中）
        if (requireEnemyInRange && alertRange != null && !alertRange.IsEnemyInRange)
        {
            DebugFollowLog("[EnemyDetected] Gate: requireEnemyInRange=true but no enemy in AlertRange");
            return false;
        }
        // 兼容 detectPlayers 为 true：即使 LoS 当前目标是 Player，仍优先选择最近的可见敌人
        var nearest = lineOfSightDetector.NearestVisibleEnemy;
        if (nearest != null)
        {
            enemy = nearest;
            DebugFollowLog($"[EnemyDetected] LoS NearestEnemy: {enemy.name}");
            return true;
        }

        // 如果 LoS 明确选择了敌人作为当前目标，则保持这一选择
        if (lineOfSightDetector.CurrentTargetType == LineOfSightDetector.VisibleTargetType.Enemy && lineOfSightDetector.CurrentVisibleTarget != null)
        {
            enemy = lineOfSightDetector.CurrentVisibleTarget;
            DebugFollowLog($"[EnemyDetected] LoS Enemy: {enemy.name}");
            return true;
        }

        // 兜底：扫描并进行射线检测以在场景中找到可见敌人
        enemy = FindNearestVisibleEnemy();
        return enemy != null;
    }

    // 备用：在 LoS 当前目标不是敌人时，查找最近可见的敌人
    private Transform FindNearestVisibleEnemy()
    {
        HealthManager[] enemies = GameObject.FindObjectsOfType<HealthManager>();
        Transform nearest = null;
        float nearestSqr = float.MaxValue;
        Vector3 origin = transform.position;

        foreach (var hm in enemies)
        {
            if (hm == null || !hm.gameObject.activeInHierarchy) continue;
            Transform t = hm.transform;
            Vector3 to = t.position - origin;
            // 地形遮挡判定（与 LoS 相同层级）
            RaycastHit2D hit = Physics2D.Raycast(origin, to.normalized, to.magnitude, LayerMask.GetMask("Terrain"));
            if (hit.collider != null)
            {
                continue; // 被地形遮挡
            }
            float sqr = to.sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = t;
            }
        }
        DebugFollowLog($"[EnemyDetected] Nearest visible enemy: {(nearest != null ? nearest.name : "none")}");
        return nearest;
    }

    // 新增：当范围内没有敌人时，跟随到玩家附近（随机在玩家前/后）
    private bool TryUpdatePlayerFollowTarget(out float targetX)
    {
        targetX = 0f;
        if (!followPlayerWhenNoEnemy || hero == null)
        {
            DebugFollowLog($"[Follow] Disabled or Hero null. followPlayerWhenNoEnemy={followPlayerWhenNoEnemy}, heroNull={hero==null}");
            return false;
        }
        // 移除敌人范围门控，因为敌人检测已在 UpdateWalking 中优先处理
        // 跟随玩家不再依赖范围门控（默认模式改为跟随 Player），忽略 requireHeroInPlayerRange
        // 初次进入或达到目标附近时重新择位（加入冷却，避免频繁左右切换）
        float selfX = transform.GetPositionX();
        bool shouldRetarget = !isFollowingPlayer || (Mathf.Abs(selfX - playerFollowTargetX) <= followRetargetThreshold && followRetargetCooldownRemaining <= 0f);
        DebugFollowLog($"[Follow] RetargetCheck shouldRetarget={shouldRetarget} selfX={selfX:F2} targetX={playerFollowTargetX:F2} cooldown={followRetargetCooldownRemaining:F2}");
        if (shouldRetarget)
        {
            // 首次跟随时随机选择跟随侧；其后保持当前跟随侧稳定
            if (!isFollowingPlayer)
            {
                playerFollowSide = (UnityEngine.Random.Range(0, 2) == 0) ? -1 : 1; // 左或右
                DebugFollowLog($"[Follow] First follow -> choose side={playerFollowSide}");
            }
            float offset = UnityEngine.Random.Range(followOffsetMin, followOffsetMax) * playerFollowSide;
            playerFollowTargetX = hero.transform.GetPositionX() + offset;
            isFollowingPlayer = true;
            DebugFollowLog($"[Follow] targetX set to {playerFollowTargetX:F2} (heroX={hero.transform.GetPositionX():F2}, offset={offset:F2}, side={playerFollowSide}); cooldown={followRetargetCooldown:F2}");
            // 设置重选冷却
            followRetargetCooldownRemaining = followRetargetCooldown;
        }
        targetX = playerFollowTargetX;
        DebugFollowLog($"[Follow] Return true. targetX={targetX:F2}");
        return true;
    }

    // 调试输出（按 debugFollow 开关控制）
    private void DebugFollowLog(string msg)
    {
        if (!debugFollow) return;
        Debug.Log($"[CloneWalker][{gameObject.name}] {msg}");
    }

    /// <summary>
    /// ���ű�StopWalker.cs���ã�����reasonΪcontrolled
    /// </summary>
    /// <param name="reason"></param>
    public void Stop(StopReasons reason)
    {
	BeginStopped(reason);
    }

    /// <summary>
    /// ����turningFacing��currentFacing������Turn״̬����Ϊ
    /// </summary>
    /// <param name="facing"></param>
    public void ChangeFacing(int facing)
    {
	if(state == States.Turning)
	{
	    turningFacing = facing;
	    currentFacing = -facing;
	    return;
	}
	currentFacing = facing;
    }

    /// <summary>
    /// ��ʼ����ȴ�״̬
    /// </summary>
    private void BeginWaitingForConditions()
    {
	state = States.WaitingForConditions;
	didFulfilCameraDistanceCondition = false;
	didFulfilHeroXCondition = false;
	UpdateWaitingForConditions();
    }

    /// <summary>
    /// ��Update�Լ�BeginWaitingForConditions�������е��ã����µȴ�״̬�µ���Ϊ
    /// </summary>
    private void UpdateWaitingForConditions()
    {
	if (!didFulfilCameraDistanceCondition && (mainCamera.transform.position - transform.position).sqrMagnitude < CameraDistanceForActivation * CameraDistanceForActivation)
	{
	    didFulfilCameraDistanceCondition = true;
	}
	if(didFulfilCameraDistanceCondition && !didFulfilHeroXCondition && hero != null && 
	    Mathf.Abs(hero.transform.GetPositionX() - waitHeroX) < WaitHeroXThreshold)
	{
	    didFulfilHeroXCondition = true;
	}
	if(didFulfilCameraDistanceCondition && (!waitForHeroX || didFulfilHeroXCondition) && !startInactive && !ambush)
	{
	    // 默认停留：不再在激活后立即开始移动
	    BeginStopped(StopReasons.Bored);
	    // StartMoving(); // 移除以避免激活即开始行走/转身
	}
    }

    /// <summary>
    /// ��ʼ����ֹͣ״̬
    /// </summary>
    /// <param name="reason"></param>
    private void BeginStopped(StopReasons reason)
    {
        state = States.Stopped;
        stopReason = reason;
        if (audioSource)
        {
            audioSource.Stop();
        }
        if(reason == StopReasons.Bored)
        {
            // 新增：若检测到敌人，直接进入追击（跳过 Idle）
            Transform enemy;
            if (EnemyDetected(out enemy))
            {
                isChasingEnemy = true;
                isReturningHome = false;
                ClearTurnCoolDown();
                int targetFacing = enemy.position.x > transform.position.x ? 1 : -1;
                if (currentFacing != targetFacing && !preventTurn)
                {
                    BeginTurning(targetFacing);
                }
                else
                {
                    BeginWalking(targetFacing);
                }
                return;
            }
            // 新增：无敌人时进行玩家跟随回退（跳过 Idle）
            float followX;
            if (TryUpdatePlayerFollowTarget(out followX))
            {
                isFollowingPlayer = true;
                ClearTurnCoolDown();
                int targetFacing = (followX > transform.GetPositionX()) ? 1 : -1;
                if (currentFacing != targetFacing && !preventTurn)
                {
                    BeginTurning(targetFacing);
                }
                else
                {
                    BeginWalking(targetFacing);
                }
                return;
            }
            // 新增：当关闭“跟随玩家”时，返回记录位置
            float homeX;
            if (!followPlayerWhenNoEnemy && TryUpdateHomeReturnTarget(out homeX))
            {
                ClearTurnCoolDown();
                int targetFacing = (homeX > transform.GetPositionX()) ? 1 : -1;
                if (currentFacing != targetFacing && !preventTurn)
                {
                    BeginTurning(targetFacing);
                }
                else
                {
                    BeginWalking(targetFacing);
                }
                return;
            }
    
            tk2dSpriteAnimationClip clipByName = animator.GetClipByName(idleClip);
            if(clipByName != null)
            {
                animator.Play(clipByName);
            }
            body.velocity = Vector2.Scale(body.velocity, new Vector2(0f, 1f)); //�൱�ڰ�x�����ϵ��ٶ�����Ϊ0
            if (pauses)
            {
                pauseTimeRemaining = UnityEngine.Random.Range(pauseTimeMin, pauseTimeMax);
                return;
            }
            EndStopping();
        }
        else if (reason == StopReasons.PlayerNear)
        {
            // 玩家附近：仅进入 Idle，停留更久，避免立即转身/跟随导致转圈
            tk2dSpriteAnimationClip clipByName = animator.GetClipByName(idleClip);
            if(clipByName != null)
            {
                animator.Play(clipByName);
            }
            body.velocity = Vector2.Scale(body.velocity, new Vector2(0f, 1f));
            pauseTimeRemaining = UnityEngine.Random.Range(followStopIdleMin, followStopIdleMax);
            return;
        }
    }

    /// <summary>
    /// ��Update�б����ã�ִ��ֹͣStop״̬����Ϊ
    /// </summary>
    private void UpdateStopping()
    {
        if(stopReason == StopReasons.Bored)
        {
            // 新增：在停止（Idle）期间若出现敌人，立刻进入追击
            Transform enemy;
            if (EnemyDetected(out enemy))
            {
                isChasingEnemy = true;
                isReturningHome = false;
                ClearTurnCoolDown();
                int targetFacing = enemy.position.x > transform.position.x ? 1 : -1;
                if (currentFacing != targetFacing && !preventTurn)
                {
                    BeginTurning(targetFacing);
                }
                else
                {
                    BeginWalking(targetFacing);
                }
                return;
            }
            // 新增：无敌人时进行玩家跟随回退（跳过 Idle）
            float followX;
            if (TryUpdatePlayerFollowTarget(out followX))
            {
                isFollowingPlayer = true;
                ClearTurnCoolDown();
                int targetFacing = (followX > transform.GetPositionX()) ? 1 : -1;
                if (currentFacing != targetFacing && !preventTurn)
                {
                    BeginTurning(targetFacing);
                }
                else
                {
                    BeginWalking(targetFacing);
                }
                return;
            }
            // 新增：当关闭“跟随玩家”时，返回记录位置
            float homeX;
            if (!followPlayerWhenNoEnemy && TryUpdateHomeReturnTarget(out homeX))
            {
                ClearTurnCoolDown();
                int targetFacing = (homeX > transform.GetPositionX()) ? 1 : -1;
                if (currentFacing != targetFacing && !preventTurn)
                {
                    BeginTurning(targetFacing);
                }
                else
                {
                    BeginWalking(targetFacing);
                }
                return;
            }
            pauseTimeRemaining -= Time.deltaTime;
            if(pauseTimeRemaining <= 0f)
            {
                EndStopping();
            }
        }
        else if (stopReason == StopReasons.PlayerNear)
        {
            // 玩家离开到一定距离时恢复跟随
            if (hero != null)
            {
                float dxHero = Mathf.Abs(hero.transform.GetPositionX() - transform.GetPositionX());
                // 跟随玩家不再依赖范围门控（默认模式改为跟随 Player）
                if (dxHero >= followResumeDistance)
                {
                    float followX;
                    if (TryUpdatePlayerFollowTarget(out followX))
                    {
                        isFollowingPlayer = true;
                        ClearTurnCoolDown();
                        int targetFacing = (followX > transform.GetPositionX()) ? 1 : -1;
                        if (currentFacing != targetFacing && !preventTurn)
                        {
                            BeginTurning(targetFacing);
                        }
                        else
                        {
                            BeginWalking(targetFacing);
                        }
                        return;
                    }
                }
            }
            // 当关闭“跟随玩家”时，优先返回记录位置
            if (!followPlayerWhenNoEnemy)
            {
                float homeX;
                if (TryUpdateHomeReturnTarget(out homeX))
                {
                    ClearTurnCoolDown();
                    int targetFacing = (homeX > transform.GetPositionX()) ? 1 : -1;
                    if (currentFacing != targetFacing && !preventTurn)
                    {
                        BeginTurning(targetFacing);
                    }
                    else
                    {
                        BeginWalking(targetFacing);
                    }
                    return;
                }
            }
            // 倒计时结束仍靠近玩家则继续 Idle，避免随机转身
            pauseTimeRemaining -= Time.deltaTime;
            if (pauseTimeRemaining <= 0f)
            {
                if (hero != null)
                {
                    float dxHero = Mathf.Abs(hero.transform.GetPositionX() - transform.GetPositionX());
                    if (dxHero < followResumeDistance)
                    {
                        pauseTimeRemaining = UnityEngine.Random.Range(followStopIdleMin, followStopIdleMax);
                        return;
                    }
                }
                EndStopping();
            }
        }
    }

    /// <summary>
    /// ��ֹֹͣ״̬
    /// </summary>
    private void EndStopping()
    {
        // 改为：默认跟随 Player 或追击敌人，不再触发巡逻（随机转身/行走）
        Transform enemy;
        if (EnemyDetected(out enemy))
        {
            isChasingEnemy = true;
            isReturningHome = false;
            ClearTurnCoolDown();
            int targetFacing = enemy.position.x > transform.position.x ? 1 : -1;
            if (currentFacing != targetFacing && !preventTurn)
            {
                BeginTurning(targetFacing);
            }
            else
            {
                BeginWalking(targetFacing);
            }
            return;
        }
        float followX;
        if (TryUpdatePlayerFollowTarget(out followX))
        {
            isFollowingPlayer = true;
            ClearTurnCoolDown();
            int targetFacing = (followX > transform.GetPositionX()) ? 1 : -1;
            if (currentFacing != targetFacing && !preventTurn)
            {
                BeginTurning(targetFacing);
            }
            else
            {
                BeginWalking(targetFacing);
            }
            return;
        }
        // 当没有敌人且关闭“跟随玩家”时，直接传送到记录的Home位置
        if (!followPlayerWhenNoEnemy && TryTeleportToHome())
        {
            BeginStopped(StopReasons.Bored);
            return;
        }
        // 如果既无敌人也不需跟随，则继续 Idle 一段时间（不进行巡逻）
        pauseTimeRemaining = UnityEngine.Random.Range(pauseTimeMin, pauseTimeMax);
        return;
    }

    // 删除重复的旧 EndStopping 实现（已禁用巡逻逻辑，默认改为跟随 Player）

    /// <summary>
    /// Ҫ����·Ҫ��ת��
    /// </summary>
    /// <param name="facing"></param>
    private void BeginWalkingOrTurning(int facing)
    {
	if(currentFacing == facing)
	{
	    BeginWalking(facing);
	    return;
	}
	BeginTurning(facing);
    }

    /// <summary>
    /// ��ʼ����Walking״̬
    /// </summary>
    /// <param name="facing"></param>
    private void BeginWalking(int facing)
    {
	state = States.Walking;
	animator.Play(walkClip);
	if (!preventScaleChange)
	{
	    transform.SetScaleX(facing * rightScale);
	}
	walkTimeRemaining = UnityEngine.Random.Range(pauseWaitMin, pauseWaitMax);
	if (audioSource)
	{
	    audioSource.Play();
	}
	float baseSpeedStart = (facing > 0) ? walkSpeedR : walkSpeedL;
	float speedStart = isChasingEnemy ? (baseSpeedStart * chaseSpeedMultiplier) : (isFollowingPlayer ? (baseSpeedStart * followSpeedMultiplier) : baseSpeedStart);
	body.velocity = new Vector2(speedStart,body.velocity.y);
    }

    /// <summary>
    /// 在Update中被调用，当执行Walking状态，检测是否需要进入Turning状态或Stopped状态
    /// </summary>
    private void UpdateWalking()
    {
	// 优先级1：检测敌人并直接向敌人前进
	Transform enemy;
	bool enemyDetected = EnemyDetected(out enemy);
	isChasingEnemy = enemyDetected;
	
	// 优先级2：若没有敌人，则尝试进入玩家跟随；否则直接传送回记录位置
    float followX = 0f;
    bool shouldFollowOrReturn = !isChasingEnemy && (followPlayerWhenNoEnemy && TryUpdatePlayerFollowTarget(out followX));
    isFollowingPlayer = followPlayerWhenNoEnemy && shouldFollowOrReturn;
    isReturningHome = false;
    
    DebugFollowLog($"[UpdateWalking] isChasingEnemy={isChasingEnemy}, shouldFollowOrReturn={shouldFollowOrReturn}, followX={followX:F2}, facing={(currentFacing > 0 ? 'R' : 'L')}, preventTurningToFaceHero={preventTurningToFaceHero}");

    if (!followPlayerWhenNoEnemy && !isChasingEnemy)
    {
        if (TryTeleportToHome())
        {
            BeginStopped(StopReasons.Bored);
            return;
        }
    }

	if(turnCooldownRemaining <= 0f)
	{
	    Sweep sweep = new Sweep(bodyCollider, 1 - currentFacing, Sweep.DefaultRayCount,Sweep.DefaultSkinThickness);
	    if (sweep.Check(transform.position, bodyCollider.bounds.extents.x + 0.5f, LayerMask.GetMask("Terrain")))
            {
                DebugFollowLog($"[Sweep] BlockedAhead following={isFollowingPlayer} action={(isFollowingPlayer ? "Stop" : "Turn")} posX={transform.GetPositionX():F2} facing={(currentFacing > 0 ? "R" : "L")}");
                if (isFollowingPlayer || isReturningHome)
                {
                    BeginStopped(StopReasons.PlayerNear);
                }
                else
                {
		    BeginTurning(-currentFacing);
                }
		return;
            }
	    // 优先级1：敌人在背面则优先转身面向敌人
	    if (isChasingEnemy && enemy != null)
	    {
	        bool enemyOnRight = enemy.position.x > transform.position.x;
	        bool facingRight = currentFacing > 0;
	        bool enemyBehind = (enemyOnRight != facingRight);
	        if (enemyBehind && !preventTurn)
        {
            DebugFollowLog($"[EnemyTurn] EnemyBehind={enemyBehind}, preventTurn={preventTurn}, enemy={enemy.name}");
            BeginTurning(-currentFacing);
            return;
        }
	    }
	    // 优先级2：跟随玩家时，若玩家在背面且允许转身，则基于X位置立即转身（忽略面向限制）
	    if (isFollowingPlayer && hero != null && alwaysFaceHero)
	    {
	        bool heroOnRight = hero.transform.GetPositionX() > transform.GetPositionX();
	        bool facingRight = currentFacing > 0;
	        bool heroBehind = (heroOnRight != facingRight);
            float dxHero = Mathf.Abs(hero.transform.GetPositionX() - transform.GetPositionX());
            DebugFollowLog($"[FollowTurn-X] heroBehind={heroBehind}, dxHero={dxHero:F2}, stopDist={followStopDistance:F2}");
	        if (heroBehind && !preventTurn && dxHero > followStopDistance)
            {
                BeginTurning(-currentFacing);
                return;
            }
	    }
	    // 优先级3：无敌人时面向玩家（在追击敌人时跳过）
            if (!isChasingEnemy && hero != null && alwaysFaceHero && !isReturningHome)
            {
                bool heroOnRight = hero.transform.GetPositionX() > transform.GetPositionX();
                bool facingRight = currentFacing > 0;
                bool heroBehind = (heroOnRight != facingRight);
                float dxHero = Mathf.Abs(hero.transform.GetPositionX() - transform.GetPositionX());
                DebugFollowLog($"[FaceHero-X] heroBehind={heroBehind}, dxHero={dxHero:F2}, stopDist={followStopDistance:F2}");
                if (heroBehind && dxHero > followStopDistance)
                {
                    BeginTurning(-currentFacing);
                    return;
                }
            }
            // 跟随玩家时，靠近玩家或接近跟随目标X则停止；同时在非追击情况下，只要靠近玩家也会停下
            if (isFollowingPlayer)
            {
                float dxHero = (hero != null) ? Mathf.Abs(hero.transform.GetPositionX() - transform.GetPositionX()) : float.MaxValue;
                float dxTarget = Mathf.Abs(followX - transform.GetPositionX());
                if (dxHero <= followStopDistance || dxTarget <= followRetargetThreshold)
                {
                    DebugFollowLog($"[StopNear] isFollowingPlayer={isFollowingPlayer}, dxHero={dxHero:F2}, dxTarget={dxTarget:F2}, stopDist={followStopDistance:F2}, targetThresh={followRetargetThreshold:F2}");
                    BeginStopped(StopReasons.PlayerNear);
                    return;
                }
            }
            else if (isReturningHome)
            {
                float dxTarget = Mathf.Abs(followX - transform.GetPositionX());
                if (dxTarget <= homeArriveThreshold)
                {
                    DebugFollowLog($"[HomeArrive] dxTarget={dxTarget:F2}, threshold={homeArriveThreshold:F2}");
                    isReturningHome = false;
                    BeginStopped(StopReasons.Bored);
                    return;
                }
            }
            else if (!isChasingEnemy && hero != null && !isReturningHome)
            {
                float dxHero = Mathf.Abs(hero.transform.GetPositionX() - transform.GetPositionX());
                if (dxHero <= followStopDistance)
                {
                    DebugFollowLog($"[StopNear] NonChase dxHero={dxHero:F2}, stopDist={followStopDistance:F2}");
                    BeginStopped(StopReasons.PlayerNear);
                    return;
                }
            }
	    if (!ignoreHoles)
	    {
		Sweep sweep2 = new Sweep(bodyCollider, DirectionUtils.Down, Sweep.DefaultRayCount, 0.1f);
		if (!sweep2.Check((Vector2)transform.position + new Vector2((bodyCollider.bounds.extents.x + 0.5f + edgeXAdjuster) * currentFacing, 0f), 0.25f, LayerMask.GetMask("Terrain")))
		{
		    // 跟随玩家时，遇到平台边缘/缺口改为停止而非来回转身，避免左右巡逻
		    if (isFollowingPlayer)
		    {
		        BeginStopped(StopReasons.PlayerNear);
		    }
		    else
		    {
		        BeginTurning(-currentFacing);
		    }
		    return;
		}
	    }
	}
	if (pauses && !(isChasingEnemy || isFollowingPlayer))
	{
	    walkTimeRemaining -= Time.deltaTime;
	    if(walkTimeRemaining <= 0f)
	    {
		BeginStopped(StopReasons.Bored);
		return;
	    }
	}
	float baseSpeed = (currentFacing > 0) ? walkSpeedR : walkSpeedL;
	float speed = isChasingEnemy ? (baseSpeed * chaseSpeedMultiplier) : (isFollowingPlayer ? (baseSpeed * followSpeedMultiplier) : baseSpeed);
	body.velocity = new Vector2(speed, body.velocity.y);
    }

    private void BeginTurning(int facing)
    {
	state = States.Turning;
	turningFacing = facing;
	if (preventTurn)
	{
	    EndTurning();
	    return;
	}
	turnCooldownRemaining = turnPause;
	body.velocity = Vector2.Scale(body.velocity, new Vector2(0f, 1f));
	animator.Play(turnClip);
	FSMUtility.SendEventToGameObject(gameObject, (facing > 0) ? "TURN RIGHT" : "TURN LEFT", false);
    }
    
   /// <summary>
   /// ��Update�б����ã�ִ��Turningת��״̬��
   /// </summary>
    private void UpdateTurning()
    {
	body.velocity = Vector2.Scale(body.velocity, new Vector2(0f, 1f));
	if (!animator.Playing)
	{
	    EndTurning();
	}
    }

    /// <summary>
    /// ��UpdateTurning()���ã�������������ɺ��л���Walking״̬��
    /// ��BeginTurning()���ã���preventTurnΪtrueʱ�Ͳ�������ִ���ˡ�
    /// </summary>
    private void EndTurning()
    {
	currentFacing = turningFacing;
	// 非追击/跟随时优先进入停止，减少走-停-转抖动
	if (preferIdleAfterTurn && !(isChasingEnemy || isFollowingPlayer))
	{
	    BeginStopped(StopReasons.Bored);
	    return;
	}
	BeginWalking(currentFacing);
    }

    /// <summary>
    /// �����turnCooldownRemaining
    /// </summary>
    public void ClearTurnCoolDown()
    {
	turnCooldownRemaining = -Mathf.Epsilon;
    }

    public enum States
    {
	NotReady,
	WaitingForConditions,
	Stopped,
	Walking,
	Turning
    }

    public enum StopReasons
    {
	Bored,
	Controlled,
    	PlayerNear
    }

    // 新增：辅助 - 选择用于 Hero 范围判定的 AlertRange（优先 playerAlertRange，其次 alertRange）
    private AlertRange GetHeroAlertRange()
    {
        return playerAlertRange != null ? playerAlertRange : alertRange;
    }

    // 新增：辅助 - Hero 是否在用于跟随的范围内（可选门控）
    private bool IsHeroInFollowRange()
    {
        if (!requireHeroInPlayerRange)
            return true;
        AlertRange hr = GetHeroAlertRange();
        return hr != null && hr.IsPlayerInRange;
    }

    // 新增：辅助 - Hero 是否在用于转身判定的范围内（不依赖 requireHeroInPlayerRange）
    private bool IsHeroInRangeForTurn()
    {
        AlertRange hr = GetHeroAlertRange();
        return hr != null && hr.IsPlayerInRange;
    }

    // 新增：当玩家不在玩家范围时，尝试将分身传送到玩家附近（最高优先级，返回是否已传送）
    private bool TryTeleportNearHero()
    {
        if (!teleportNearHeroWhenOutOfPlayerRange || hero == null)
            return false;
        AlertRange hr = GetHeroAlertRange();
        // 若存在范围组件且玩家在范围内，则不传送；若为空则视为需要传送
        if (hr != null && hr.IsPlayerInRange)
            return false;
        // 最高优先级：不受追击敌人限制；仍受冷却限制
        if (teleportCooldownRemaining > 0f)
            return false;

        // 基于玩家位置随机左右偏移一个跟随距离
        int side = (UnityEngine.Random.Range(0, 2) == 0) ? -1 : 1;
        float offset = UnityEngine.Random.Range(followOffsetMin, followOffsetMax) * side;
        float baseX = hero.transform.GetPositionX() + offset;
        float newX = FindNonOverlappingX(baseX);
        Vector3 pos = transform.position;
        transform.position = new Vector3(newX, pos.y, pos.z); // use non-overlapping X to avoid stacking with other clones

        // 重置速度，避免瞬移后继续沿旧方向移动
        body.velocity = new Vector2(0f, body.velocity.y);

        // 在关闭跟随玩家时，刷新“记录位置”为传送后的当前位置
        if (!followPlayerWhenNoEnemy)
        {
            homePosition = transform.position;
            hasHomePosition = true;
            DebugFollowLog($"[Home] Refresh after teleport -> {homePosition.x:F2}");
        }

        // 立即标记为跟随玩家（使行走逻辑优先朝向玩家目标）
        playerFollowSide = side;
        playerFollowTargetX = hero.transform.GetPositionX() + UnityEngine.Random.Range(followOffsetMin, followOffsetMax) * playerFollowSide;
        isFollowingPlayer = true;

        // 开始冷却
        teleportCooldownRemaining = teleportCooldown;
        return true;
    }

    // 新增：辅助 - 在目标X附近寻找不与其他分身重叠的可用X
    private float FindNonOverlappingX(float desiredX)
    {
        // 基于自身碰撞体的宽度计算间距
        float halfWidth = bodyCollider.bounds.extents.x;
        float spacing = halfWidth * 2f + 0.1f; // 稍微加一点余量，避免边界贴合
        int[] sequence = new int[] { 0, 1, -1, 2, -2, 3, -3, 4, -4, 5, -5 };

        Vector3 pos = transform.position;
        Vector2 size = new Vector2(bodyCollider.bounds.size.x, bodyCollider.bounds.size.y);

        for (int i = 0; i < sequence.Length; i++)
        {
            float testX = desiredX + sequence[i] * spacing;
            Vector2 center = new Vector2(testX, pos.y);
            var hits = Physics2D.OverlapBoxAll(center, size, 0f);
            bool occupiedByClone = false;
            foreach (var hit in hits)
            {
                if (hit == null) continue;
                if (hit.transform == transform) continue;
                // 仅避免与其他分身重叠（而不是所有碰撞体），更稳妥
                if (hit.GetComponent<Clone_Walker>() != null)
                {
                    occupiedByClone = true;
                    break;
                }
            }
            if (!occupiedByClone)
            {
                return testX;
            }
        }
        // 如果找不到空位，退回到期望值（不改变原逻辑）
        return desiredX;
    }

// 新增：当没有敌人时，直接传送到记录的Home位置
private bool TryTeleportToHome()
{
    if (followPlayerWhenNoEnemy) return false;
    if (!hasHomePosition)
    {
        homePosition = transform.position;
        hasHomePosition = true;
        DebugFollowLog($"[HomeTeleport] No home recorded, auto-record at {homePosition.x:F2}");
        return false;
    }
    float dx = Mathf.Abs(transform.GetPositionX() - homePosition.x);
    if (dx <= homeArriveThreshold)
    {
        isReturningHome = false;
        return false;
    }
    Vector3 target = homePosition;
        float newX = FindNonOverlappingX(target.x);
        transform.position = new Vector3(newX, target.y, target.z);
    body.velocity = new Vector2(0f, body.velocity.y);
    isReturningHome = false;
    DebugFollowLog($"[HomeTeleport] Teleported to ({target.x:F2},{target.y:F2})");
    return true;
}

// 新增：当跟随玩家关闭时，返回记录的Home位置
private bool TryUpdateHomeReturnTarget(out float targetX)
{
    targetX = 0f;
    if (followPlayerWhenNoEnemy) return false;
    if (!hasHomePosition)
    {
        homePosition = transform.position;
        hasHomePosition = true;
        DebugFollowLog($"[Home] Auto-record home at {homePosition.x:F2}");
    }
    float selfX = transform.GetPositionX();
    float dx = Mathf.Abs(selfX - homePosition.x);
    if (dx <= homeArriveThreshold)
    {
        isReturningHome = false;
        return false;
    }
    isReturningHome = true;
    targetX = homePosition.x;
    DebugFollowLog($"[Home] targetX set to {targetX:F2}, dx={dx:F2}");
    return true;
}

// FSM桥接：设置“无敌人时跟随玩家”开关
public void SetFollowPlayerWhenNoEnemy(bool on)
{
    followPlayerWhenNoEnemy = on;
    DebugFollowLog($"[FollowToggle] followPlayerWhenNoEnemy set to {on}");
    if (!on)
    {
        // 关闭跟随时：记录当前位置为新的 Home，并清理相关状态
        homePosition = transform.position;
        hasHomePosition = true;
        isFollowingPlayer = false;
        isReturningHome = false;
        DebugFollowLog($"[Home] Toggle off -> record new home at {homePosition.x:F2},{homePosition.y:F2}");
        DebugFollowLog("[FollowToggle] Disabled -> clear follow/home state");
    }
    else
    {
        // 开启跟随时：停止“回家”过程，后续根据玩家位置选择跟随
        isReturningHome = false;
    }
}

// FSM桥接：获取当前“无敌人时跟随玩家”开关状态（用于切换）
public bool GetFollowPlayerWhenNoEnemy()
{
    DebugFollowLog($"[FollowToggle] Query state: {followPlayerWhenNoEnemy}");
    return followPlayerWhenNoEnemy;
}
// 新增：显式设置 Home 位置（用于与玩家交换位置后，将 Home 更新为玩家原位置）
public void SetHomePosition(Vector3 pos)
{
    homePosition = pos;
    hasHomePosition = true;
    DebugFollowLog($"[Home] Set by swap -> {homePosition.x:F2},{homePosition.y:F2}");
}
}
