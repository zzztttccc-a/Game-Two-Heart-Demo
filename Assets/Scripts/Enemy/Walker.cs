using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Walker : MonoBehaviour
{
    [Header("Structure")]
    //�����ҵĽű�һ��������
    [SerializeField] private LineOfSightDetector lineOfSightDetector;
    [SerializeField] private AlertRange alertRange; 

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
    [SerializeField] private bool pauses;  //�Ƿ���Ҫ��ֹ״̬
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

    [SerializeField] private Walker.States state;
    [SerializeField] private Walker.StopReasons stopReason;
    private bool didFulfilCameraDistanceCondition; //��ʱû���õ�
    private bool didFulfilHeroXCondition; //��ʱû���õ�
    private int currentFacing;//Debug��ʱ�������ǰ��Ӹ�[SerializeField]
    private int turningFacing;
    //������ʱ���ҹ���˼��
    private float walkTimeRemaining;
    private float pauseTimeRemaining;
    private float turnCooldownRemaining;

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
    }

    /// <summary>
    /// ���Ǵ�����һ��״̬������Ϊ����״̬��ÿһ�ֶ���Update��Stop�ķ�����
    /// </summary>
    protected void Update()
    {
	turnCooldownRemaining -= Time.deltaTime;
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
	    BeginStopped(StopReasons.Bored);
	    StartMoving();
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
    }

    /// <summary>
    /// ��Update�б����ã�ִ��ֹͣStop״̬����Ϊ
    /// </summary>
    private void UpdateStopping()
    {
	if(stopReason == StopReasons.Bored)
	{
	    pauseTimeRemaining -= Time.deltaTime;
	    if(pauseTimeRemaining <= 0f)
	    {
		EndStopping();
	    }
	}
    }

    /// <summary>
    /// ��ֹֹͣ״̬
    /// </summary>
    private void EndStopping()
    {
	if(currentFacing == 0)
	{
	    BeginWalkingOrTurning(UnityEngine.Random.Range(0, 2) == 0 ? 1 : -1);
	    return;
	}
	if(UnityEngine.Random.Range(0,100) < turnAfterIdlePercentage)
	{
	    BeginTurning(-currentFacing);
	    return;
	}
	BeginWalking(currentFacing); //����Ӧ���ǿ�ʼ����Walk�����ǿ�ʼת��Turn
    }

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
	body.velocity = new Vector2((facing > 0) ? walkSpeedR : walkSpeedL,body.velocity.y);
    }

    /// <summary>
    /// ��Update�б����ã���ִ̬��Walking״̬��������������Ƿ�Ҫ����Turning״̬����Stopped״̬
    /// </summary>
    private void UpdateWalking()
    {
	if(turnCooldownRemaining <= 0f)
	{
	    Sweep sweep = new Sweep(bodyCollider, 1 - currentFacing, Sweep.DefaultRayCount,Sweep.DefaultSkinThickness);
	    if (sweep.Check(transform.position, bodyCollider.bounds.extents.x + 0.5f, LayerMask.GetMask("Terrain")))
	    {
		BeginTurning(-currentFacing);
		return;
	    }
	    if (!preventTurningToFaceHero && (hero != null && hero.transform.GetPositionX() > transform.GetPositionX() != currentFacing > 0) && lineOfSightDetector != null && lineOfSightDetector.CanSeeHero && alertRange != null && alertRange.IsHeroInRange)
	    {
		BeginTurning(-currentFacing);
		return;
	    }
	    if (!ignoreHoles)
	    {
		Sweep sweep2 = new Sweep(bodyCollider, DirectionUtils.Down, Sweep.DefaultRayCount, 0.1f);
		if (!sweep2.Check((Vector2)transform.position + new Vector2((bodyCollider.bounds.extents.x + 0.5f + edgeXAdjuster) * currentFacing, 0f), 0.25f, LayerMask.GetMask("Terrain")))
		{
		    BeginTurning(-currentFacing);
		    return;
		}
	    }
	}
	if (pauses)
	{
	    walkTimeRemaining -= Time.deltaTime;
	    if(walkTimeRemaining <= 0f)
	    {
		BeginStopped(StopReasons.Bored);
		return;
	    }
	}
	body.velocity = new Vector2((currentFacing > 0) ? walkSpeedR : walkSpeedL, body.velocity.y);
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
	Controlled
    }

}
