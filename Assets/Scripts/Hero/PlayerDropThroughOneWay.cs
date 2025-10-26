using System.Collections;
using UnityEngine;

/// <summary>
/// 挂在玩家身上的脚本，支持“下+跳”主动穿过单向平台。
/// 原理：在短时间内忽略玩家与当前接触平台的碰撞 Physics2D.IgnoreCollision。
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerDropThroughOneWay : MonoBehaviour
{
    [Header("下落穿过设置")]
    [SerializeField, Tooltip("忽略碰撞持续时间（秒）")] private float dropDuration = 0.35f;
    [SerializeField, Tooltip("触发时给予的向下速度（避免停在平台边缘）")] private float downwardKickSpeed = 3f;
    [SerializeField, Tooltip("使用 Unity Input 的 Vertical/Jump 自动触发")] private bool useUnityInput = true;
    [SerializeField, Tooltip("Vertical 轴向下阈值（-1~1）")] private float downThreshold = -0.5f;

    private Collider2D playerCollider;
    private Collider2D[] playerColliders;
    private Rigidbody2D rb;

    // 当前接触的平台碰撞器（带 PlatformEffector2D）
    private Collider2D currentOneWayPlatform;
    private bool dropping;

    private void Awake()
    {
        // playerCollider = GetComponent<Collider2D>();
        playerColliders = GetComponentsInChildren<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (!useUnityInput || dropping) return;

        // 简易输入：Vertical < 阈值 且 按下 Jump
        // 提示：项目使用 InControl 时，可禁用此项，改为在你的输入逻辑里手动调用 TriggerDropThrough()
        float v = Input.GetAxisRaw("Vertical");
        bool jumpPressed = Input.GetButtonDown("Jump");
        if (v < downThreshold && jumpPressed)
        {
            TriggerDropThrough();
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        // 记录玩家当前脚下的单向平台（兼容父级/Tilemap/CompositeCollider2D）
        var col = collision.collider;
        var eff = col != null ? (col.GetComponent<PlatformEffector2D>() ?? col.GetComponentInParent<PlatformEffector2D>()) : null;
        if (col != null && (col.usedByEffector || eff != null))
        {
            currentOneWayPlatform = col;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.collider == currentOneWayPlatform)
        {
            currentOneWayPlatform = null;
        }
    }

    /// <summary>
    /// 外部调用此方法以触发下落穿过（例如在自有输入系统中）。
    /// </summary>
    public void TriggerDropThrough()
    {
        if (dropping) return;
        if (currentOneWayPlatform == null)
        {
            currentOneWayPlatform = FindPlatformBelow();
        }
        StartCoroutine(DropThroughPlatformCoroutine());
    }

    private IEnumerator DropThroughPlatformCoroutine()
    {
        var platform = currentOneWayPlatform;
        if (platform == null || playerColliders == null || playerColliders.Length == 0)
        {
            yield break;
        }

        dropping = true;

        // 收集平台上所有碰撞体（避免下落时接触到同平台的其他 Collider2D 被拦住）
        var platformColliders = platform.GetComponentsInChildren<Collider2D>();
        if (platformColliders == null || platformColliders.Length == 0)
        {
            platformColliders = new Collider2D[] { platform };
        }

        // 临时忽略玩家与当前平台的所有碰撞体的碰撞（玩家可能有多个 Collider2D）
        foreach (var pc in playerColliders)
        {
            if (pc == null) continue;
            foreach (var plc in platformColliders)
            {
                if (plc != null)
                {
                    Physics2D.IgnoreCollision(pc, plc, true);
                }
            }
        }

        // 给一个向下速度，帮助脱离平台上表面
        if (rb != null)
        {
            float kick = Mathf.Abs(downwardKickSpeed);
            if (rb.velocity.y > -kick)
            {
                rb.velocity = new Vector2(rb.velocity.x, -kick);
            }
        }

        yield return new WaitForSeconds(dropDuration);

        // 恢复碰撞（平台或玩家可能已经离开/被禁用，做空值保护）
        foreach (var pc in playerColliders)
        {
            if (pc == null) continue;
            foreach (var plc in platformColliders)
            {
                if (pc != null && plc != null)
                {
                    Physics2D.IgnoreCollision(pc, plc, false);
                }
            }
        }
        dropping = false;
    }

    private Collider2D FindPlatformBelow()
    {
        Vector2 origin = transform.position;
        float maxDistance = 0.6f; // 适当的射线距离，用于找脚下平台
        var results = new RaycastHit2D[8];

        // 使用简单过滤：不检测触发器
        ContactFilter2D cf = new ContactFilter2D();
        cf.useTriggers = false;

        int count = Physics2D.Raycast(origin, Vector2.down, cf, results, maxDistance);
        for (int i = 0; i < count; i++)
        {
            var hit = results[i];
            var col = hit.collider;
            if (col == null) continue;

            // 检查是否为带 PlatformEffector2D 的单向平台
            var eff = col.GetComponent<PlatformEffector2D>() ?? col.GetComponentInParent<PlatformEffector2D>();
            if (col.usedByEffector || eff != null)
            {
                return col;
            }
        }
        return null;
    }
}