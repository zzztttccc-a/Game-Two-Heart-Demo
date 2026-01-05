using UnityEngine;
using System.Collections;

public class SpringPlatform : MonoBehaviour
{
    [Header("Spring Physics")]
    [Tooltip("弹簧劲度系数：值越大，回弹越快，越硬")]
    public float stiffness = 150f;
    
    [Tooltip("阻尼系数：值越大，停下来的越快，能量耗散越快")]
    public float damping = 8f;
    
    [Tooltip("质量：值越大，惯性越大，动作越慢")]
    public float mass = 1f;

    [Header("Interaction")]
    [Tooltip("踩踏时的冲击力：决定了初始下沉的幅度")]
    public float impactForce = 8f;

    [Tooltip("最小触发冲击的速度：防止在平台上走动时反复触发冲击")]
    public float minImpactVelocity = 2f;
    
    [Tooltip("响应延迟：踩下后多久开始摇晃（秒）")]
    public float responseDelay = 0.08f;

    [Header("Optional Rotation")]
    [Tooltip("是否启用旋转摇晃")]
    public bool enableRotationShake = false;
    [Tooltip("如果方向反了（比如走到左边右边沉），请勾选此项")]
    public bool invertRotation = false;
    public float rotationImpactFactor = 2f; // 旋转受力系数

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    
    // Y轴模拟状态
    private float currentDisplacementY;
    private float velocityY;

    // 旋转模拟状态 (简化为Z轴旋转)
    private float currentAngle;
    private float angularVelocity;

    private Collider2D col;
    private bool isSimulationActive = false;
    private Transform playerTransform;
    
    // 修复哆嗦用的变量
    private Coroutine exitRoutine;
    private float lastImpactTime;
    private float impactCooldown = 0.2f;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
    }

    private void Start()
    {
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    }

    private void FixedUpdate()
    {
        // 持续检测：如果玩家在平台上，持续施加重量带来的旋转和下沉影响
        // 这模拟了"竹筏"感觉：你走到哪边，哪边就会持续受力倾斜
        if (playerTransform != null)
        {
            isSimulationActive = true;
            
            // 持续的重力影响 (不仅仅是瞬间冲击)
            // 这里的系数可能需要微调，通常比冲击力小很多，代表静止体重
            float continuousForce = impactForce * 0.1f; 
            velocityY -= continuousForce / mass * Time.fixedDeltaTime;

            if (enableRotationShake)
            {
                float relativeX = playerTransform.position.x - transform.position.x;
                // 力矩 = 力 * 距离
                // 持续施加力矩
                float torque = relativeX * rotationImpactFactor * continuousForce;
                
                // 根据反转设置决定方向
                if (invertRotation)
                {
                    angularVelocity += (torque / mass) * Time.fixedDeltaTime;
                }
                else
                {
                    angularVelocity -= (torque / mass) * Time.fixedDeltaTime;
                }
            }
        }

        if (!isSimulationActive && Mathf.Approximately(currentDisplacementY, 0f) && Mathf.Approximately(currentAngle, 0f))
            return;

        Vector3 lastPosition = transform.position;
        float dt = Time.fixedDeltaTime;

        // --- Y轴弹簧模拟 ---
        // 弹簧力 F = -kx
        float springForceY = -stiffness * currentDisplacementY;
        // 阻尼力 F = -cv
        float dampingForceY = -damping * velocityY;
        // 总力
        float totalForceY = springForceY + dampingForceY;
        // 加速度 a = F/m
        float accelerationY = totalForceY / mass;
        
        velocityY += accelerationY * dt;
        currentDisplacementY += velocityY * dt;

        // --- 旋转弹簧模拟 (如果启用) ---
        if (enableRotationShake)
        {
            float springTorque = -stiffness * currentAngle;
            float dampingTorque = -damping * angularVelocity;
            float totalTorque = springTorque + dampingTorque;
            float angularAccel = totalTorque / mass; // 假设转动惯量与质量成正比

            angularVelocity += angularAccel * dt;
            currentAngle += angularVelocity * dt;
        }

        // 应用变换
        transform.position = initialPosition + new Vector3(0f, currentDisplacementY, 0f);
        if (enableRotationShake)
        {
            transform.rotation = initialRotation * Quaternion.Euler(0f, 0f, currentAngle);
        }

        // 带动角色移动 (位置同步)
        if (playerTransform != null)
        {
            Vector3 deltaMovement = transform.position - lastPosition;
            if (deltaMovement != Vector3.zero)
            {
                playerTransform.position += deltaMovement;
            }
        }

        // 停止模拟阈值 (仅当玩家不在平台上时才允许完全休眠)
        if (playerTransform == null &&
            Mathf.Abs(currentDisplacementY) < 0.001f && Mathf.Abs(velocityY) < 0.001f &&
            Mathf.Abs(currentAngle) < 0.01f && Mathf.Abs(angularVelocity) < 0.01f)
        {
            currentDisplacementY = 0f;
            velocityY = 0f;
            currentAngle = 0f;
            angularVelocity = 0f;
            transform.position = initialPosition;
            transform.rotation = initialRotation;
            isSimulationActive = false;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // Layer 9 is usually Hero/Player
        if (collision.gameObject.layer == 9)
        {
            // 简单的判定：玩家必须在平台上方
            // 使用 bounds 判定
            if (col != null && collision.collider.bounds.min.y >= col.bounds.max.y - 0.2f) // 允许一点点误差
            {
                // 如果有之前的退出倒计时，取消它（说明是微小抖动，玩家还在上面）
                if (exitRoutine != null)
                {
                    StopCoroutine(exitRoutine);
                    exitRoutine = null;
                }
                
                playerTransform = collision.transform;
                
                // --- 修复哆嗦的核心逻辑 ---
                // 只有当撞击速度足够大（真的是跳上去或落上去）时，才触发冲击摇晃
                // 如果只是在上面走（相对速度很小），只更新 playerTransform 进行持续施压，但不触发 impactForce
                if (collision.relativeVelocity.magnitude >= minImpactVelocity && Time.time > lastImpactTime + impactCooldown)
                {
                    lastImpactTime = Time.time;

                    // 立即计算相对位置，避免协程中 collision 数据失效或报错
                    float relativeX = 0f;
                    if (collision.contactCount > 0)
                    {
                        relativeX = collision.GetContact(0).point.x - transform.position.x;
                    }
                    else
                    {
                        // 备用方案：使用 transform 位置差
                        relativeX = collision.transform.position.x - transform.position.x;
                    }

                    StartCoroutine(ApplyImpactRoutine(relativeX));
                }
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.layer == 9 && collision.transform == playerTransform)
        {
            // 不立即清除，给一点“土狼时间” (Coyote Time)
            // 防止因为微小的物理抖动导致平台回弹打击玩家
            if (exitRoutine != null) StopCoroutine(exitRoutine);
            exitRoutine = StartCoroutine(ClearPlayerDelay());
        }
    }

    private IEnumerator ClearPlayerDelay()
    {
        yield return new WaitForSeconds(0.15f);
        playerTransform = null;
        exitRoutine = null;
    }

    private IEnumerator ApplyImpactRoutine(float relativeX)
    {
        yield return new WaitForSeconds(responseDelay);

        isSimulationActive = true;
        
        // 施加向下的冲击 (速度变负)
        velocityY -= impactForce / mass;

        // 如果启用旋转，根据玩家落点相对于中心的偏移施加旋转
        if (enableRotationShake)
        {
            // 如果踩在右边 (relativeX > 0)，应该向右倾斜 (顺时针，角度变负)
            // Torque = Force * Distance
            // 这里简化模拟
            float torqueImpact = relativeX * rotationImpactFactor * impactForce;
            
            if (invertRotation)
            {
                angularVelocity += torqueImpact / mass;
            }
            else
            {
                angularVelocity -= torqueImpact / mass;
            }
        }
    }
}
