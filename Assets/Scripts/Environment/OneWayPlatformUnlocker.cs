using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class OneWayPlatformUnlocker : MonoBehaviour
{
    // 组名：同组平台在未配置 targetsToUnlock 时一起被解锁
    public string group;
    // 是否初始禁用：禁用时会关闭碰撞并设为透明，等待被解锁
    public bool startLocked = true;
    // 解锁功能开关：为 false 时踩踏不触发任何解锁
    public bool unlockEnabled = true;
    // 淡入到不透明的时长
    public float unlockFadeDuration = 0.25f;
    // 解锁完成后的高亮闪烁颜色
    public Color flashColor = new Color(1f, 1f, 1f, 1f);
    // 高亮闪烁的时长
    public float flashDuration = 0.15f;
    // 指定目标：踩到本平台时要解锁的其它平台；为空则回退到同组解锁
    public OneWayPlatformUnlocker[] targetsToUnlock;

    private Collider2D col;
    private SpriteRenderer sr;
    private tk2dSprite tk;
    private Image uiImage;
    private Collider2D childCollider;
    private SpriteRenderer childSpriteRenderer;
    private tk2dSprite childTkSprite;
    private Image childImage;
    private Collider2D[] childColliders;
    private SpriteRenderer[] childSpriteRenderers;
    private tk2dSprite[] childTkSprites;
    private Image[] childImages;
    public string imageChildName = "image";
    public string colliderChildName = "collider";
    private bool isLocked;
    private bool triggeredUnlock;
    private Color originalColor;

    private void Awake()
    {
        // 初始化渲染与碰撞组件；根据 startLocked 设置初始可见性与碰撞
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        tk = GetComponent<tk2dSprite>();
        uiImage = GetComponent<Image>();
        var imageTr = transform.Find(imageChildName);
        if (imageTr != null)
        {
            childSpriteRenderer = imageTr.GetComponent<SpriteRenderer>();
            if (childSpriteRenderer == null) childTkSprite = imageTr.GetComponent<tk2dSprite>();
            if (childSpriteRenderer == null && childTkSprite == null) childImage = imageTr.GetComponent<Image>();
        }
        if (childSpriteRenderer == null && childTkSprite == null)
        {
            var srs = GetComponentsInChildren<SpriteRenderer>(true);
            childSpriteRenderers = System.Array.FindAll(srs, r => r.gameObject != gameObject);
            foreach (var r in childSpriteRenderers)
            {
                if (r.gameObject != gameObject) { childSpriteRenderer = r; break; }
            }
            if (childSpriteRenderer == null)
            {
                var tks = GetComponentsInChildren<tk2dSprite>(true);
                childTkSprites = System.Array.FindAll(tks, t => t.gameObject != gameObject);
                foreach (var t in childTkSprites)
                {
                    if (t.gameObject != gameObject) { childTkSprite = t; break; }
                }
            }
            if (childSpriteRenderer == null && childTkSprite == null)
            {
                var imgs = GetComponentsInChildren<Image>(true);
                childImages = System.Array.FindAll(imgs, im => im.gameObject != gameObject);
                foreach (var im in childImages)
                {
                    if (im.gameObject != gameObject) { childImage = im; break; }
                }
            }
        }
        var colliderTr = transform.Find(colliderChildName);
        if (colliderTr != null)
        {
            childCollider = colliderTr.GetComponent<Collider2D>();
        }
        if (childCollider == null)
        {
            var cols = GetComponentsInChildren<Collider2D>(true);
            childColliders = System.Array.FindAll(cols, c0 => c0.gameObject != gameObject);
            foreach (var c0 in childColliders)
            {
                if (c0.gameObject != gameObject) { childCollider = c0; break; }
            }
        }
        else
        {
            childColliders = new Collider2D[] { childCollider };
        }
        originalColor = GetColor();
        isLocked = startLocked;
        if (startLocked)
        {
            if (childColliders != null && childColliders.Length > 0)
            {
                for (int i = 0; i < childColliders.Length; i++) childColliders[i].enabled = false;
            }
            else if (childCollider != null) childCollider.enabled = false; else if (col != null) col.enabled = false;
            SetColor(new Color(originalColor.r, originalColor.g, originalColor.b, 0f));
        }
    }

    private void OnCollisionEnter2D(Collision2D c)
    {
        if (triggeredUnlock) return;
        if (!unlockEnabled) return;
        var hero = c.collider.GetComponentInParent<HeroController>();
        if (hero == null) return;
        for (int i = 0; i < c.contactCount; i++)
        {
            var n = c.GetContact(i).normal;
            if (n.y > 0.5f)
            {
                triggeredUnlock = true;
                TriggerUnlock();
                break;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggeredUnlock) return;
        if (!unlockEnabled) return;
        var hero = other.GetComponentInParent<HeroController>();
        if (hero == null) return;
        if (other.bounds.center.y > transform.position.y)
        {
            triggeredUnlock = true;
            TriggerUnlock();
        }
    }

    private void TriggerUnlock()
    {
        // 优先按 targetsToUnlock 指定目标解锁；为空则回退到同组解锁
        if (!unlockEnabled) return;
        bool usedTargets = false;
        if (targetsToUnlock != null && targetsToUnlock.Length > 0)
        {
            for (int i = 0; i < targetsToUnlock.Length; i++)
            {
                var p = targetsToUnlock[i];
                if (p != null)
                {
                    p.StartUnlock();
                }
            }
            usedTargets = true;
        }
        if (!usedTargets)
        {
            var platforms = FindObjectsOfType<OneWayPlatformUnlocker>();
            for (int i = 0; i < platforms.Length; i++)
            {
                var p = platforms[i];
                if (p != null && p != this && p.group == group && p.isLocked)
                {
                    p.StartCoroutine(p.AnimateUnlock());
                }
            }
        }
    }

    private IEnumerator AnimateUnlock()
    {
        // 解锁动画：开启碰撞→透明淡入→高亮闪烁
        isLocked = false;
        if (childColliders != null && childColliders.Length > 0)
        {
            for (int i = 0; i < childColliders.Length; i++) childColliders[i].enabled = true;
        }
        else if (childCollider != null) childCollider.enabled = true; else if (col != null) col.enabled = true;
        Color start = GetColor();
        Color baseColor = new Color(originalColor.r, originalColor.g, originalColor.b, 1f);
        float t = 0f;
        while (t < unlockFadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, unlockFadeDuration));
            Color c = Color.Lerp(start, baseColor, a);
            SetColor(c);
            yield return null;
        }
        SetColor(baseColor);
        Color prev = GetColor();
        SetColor(flashColor);
        yield return new WaitForSeconds(flashDuration);
        SetColor(prev);
    }

    public void StartUnlock()
    {
        // 外部调用接口：立即播放解锁动画（重复调用会重置动画）
        if (!isLocked)
        {
            StopAllCoroutines();
            StartCoroutine(AnimateUnlock());
        }
        else
        {
            StartCoroutine(AnimateUnlock());
        }
    }

    public void SetUnlockEnabled(bool enabled)
    {
        // 运行时切换解锁功能开关
        unlockEnabled = enabled;
    }

    private Color GetColor()
    {
        if (childSpriteRenderer != null) return childSpriteRenderer.color;
        if (childTkSprite != null) return childTkSprite.color;
        if (childImage != null) return childImage.color;
        if (sr != null) return sr.color;
        if (tk != null) return tk.color;
        if (uiImage != null) return uiImage.color;
        return originalColor.a <= 0 ? new Color(1f, 1f, 1f, 0f) : new Color(1f, 1f, 1f, 1f);
    }

    private void SetColor(Color c)
    {
        if (childSpriteRenderers != null && childSpriteRenderers.Length > 0)
        {
            for (int i = 0; i < childSpriteRenderers.Length; i++) childSpriteRenderers[i].color = c;
        }
        else if (childSpriteRenderer != null) childSpriteRenderer.color = c;
        if (childTkSprites != null && childTkSprites.Length > 0)
        {
            for (int i = 0; i < childTkSprites.Length; i++) childTkSprites[i].color = c;
        }
        else if (childTkSprite != null) childTkSprite.color = c;
        if (childImages != null && childImages.Length > 0)
        {
            for (int i = 0; i < childImages.Length; i++) childImages[i].color = c;
        }
        else if (childImage != null) childImage.color = c;
        if (sr != null) sr.color = c;
        if (tk != null) tk.color = c;
        if (uiImage != null) uiImage.color = c;
    }
}
