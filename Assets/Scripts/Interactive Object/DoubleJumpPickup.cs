using UnityEngine;

/// <summary>
/// 能力拾取：二段跳（Double Jump）。
/// 将本脚本挂到一个带有 2D 触发器碰撞体的物体上，
/// 当玩家接触到该物体时，授予二段跳能力并可选地保存游戏、播放音效/特效。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DoubleJumpPickup : MonoBehaviour
{
    [Header("拾取设置")]
    [Tooltip("是否一次性拾取后消失")]
    public bool oneShot = true;

    [Header("可选：音效/特效")]
    public AudioClip pickupClip;
    public GameObject pickupVfx;
    public Vector3 vfxOffset = Vector3.zero;

    private bool picked;

    private void Reset()
    {
        // 确保碰撞体为触发器
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (picked) return;

        var hero = other.GetComponent<HeroController>();
        if (hero == null) return;

        var gm = GameManager.instance;
        if (gm != null && gm.playerData != null)
        {
            // 授予二段跳能力
            gm.playerData.hasDoubleJump = true;

            picked = true;

            // 可选：播放特效
            if (pickupVfx != null)
            {
                Instantiate(pickupVfx, transform.position + vfxOffset, Quaternion.identity);
            }

            // 可选：播放音效
            if (pickupClip != null)
            {
                var src = GetComponent<AudioSource>();
                if (src == null) src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.volume = 1f;
                src.pitch = 1f;
                src.PlayOneShot(pickupClip);
            }

            // 立即保存
            gm.SaveGame();
        }

        if (oneShot)
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;

            if (pickupClip != null)
            {
                Destroy(gameObject, pickupClip.length + 0.1f);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}