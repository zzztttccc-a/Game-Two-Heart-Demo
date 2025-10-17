using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 显形/隐形控制器。挂载到父物体上，支持：
/// - 渐显/渐隐（SpriteRenderer、tk2dBaseSprite、CanvasGroup）
/// - 通过接口管理需要启用/禁用的子物体列表
/// - 被 RevealArea2D 注册引用计数控制：进入范围显形，离开范围恢复原始隐形
/// </summary>
public class RevealHideController : MonoBehaviour
{
    // 全局注册，供 RevealArea2D 扫描
    private static readonly HashSet<RevealHideController> registry = new HashSet<RevealHideController>();
    public static IReadOnlyCollection<RevealHideController> AllControllers => registry;

    [Header("可见性配置")]
    [Tooltip("初始是否隐形（原始状态）。若为 true，则在范围外保持隐形，进入范围显形，离开范围恢复隐形。")]
    public bool startHidden = true;

    [Tooltip("仅影响‘原本隐形’的对象。若为 true，则对‘初始可见’对象不做进入/离开范围自动切换。")]
    public bool onlyAffectOriginallyHidden = true;

    [Tooltip("渐显耗时（秒）")]
    public float fadeInDuration = 0.3f;

    [Tooltip("渐隐耗时（秒）")]
    public float fadeOutDuration = 0.3f;

    [Tooltip("渐变曲线（0-1）。仅在启用淡变时生效。")]
    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("淡变选项")]
    [Tooltip("显隐是否使用淡变动画。关闭（默认）则立即显隐，不运行淡变协程。")]
    public bool enableFade = false;

    [Header("子物体启用/禁用")]
    [Tooltip("当隐形时禁用列表中的子物体；当显形时启用它们。仅作用于 extraChildren 列表，不自动遍历所有子物体。")]
    public bool disableChildrenWhenHidden = true;

    [Tooltip("需要随显隐启用/禁用的子物体列表，可在运行时通过接口增删。")]
    public List<GameObject> extraChildren = new List<GameObject>();

    [Header("持久显形选项")]
    [Tooltip("勾选后：对象一旦完全显形（渐显协程结束或立即显形），离开范围时不再自动恢复隐形（但你仍可手动调用 Conceal 进行隐藏）。")]
    public bool persistAfterFullReveal = false;

    [Header("原本可见对象控制")]
    [Tooltip("允许范围在‘进入时’对原本可见的对象进行强制隐形（与 onlyAffectOriginallyHidden 相反，用于实现‘原本显形的变为隐形’效果）。开启后 RevealArea2D 可调用 OnConcealerEnter/Exit 强制切换。")]
    public bool allowConcealOriginallyVisible = false;

    [Header("功能遮罩（交互）")]
    [Tooltip("隐形时是否禁用所有 Collider2D（使其不可交互）。")]
    public bool disableCollidersWhenHidden = true;

    [Header("可视遮罩（SpriteMask）")]
    [Tooltip("为目标 SpriteRenderer 设置 SpriteMask 交互模式。由范围脚本决定设置为：None / VisibleInside / VisibleOutside。")]
    public bool allowSpriteMaskControl = true;

    [Header("初始状态交互")]
    [Tooltip("当 startHidden=true 时，是否在场景开始就禁用所有 Collider2D。默认 false（不禁用）。")] 
    public bool disableCollidersOnInitialHidden = false;

    [Header("碰撞体控制（检测）")]
    [Tooltip("检测（进入/离开范围）驱动的显隐不改变 Collider2D 启用状态。保持你在 Inspector 中手动设置的状态。")]
    public bool keepCollidersStateOnDetection = true;

    [Header("子物体遮罩控制")]
    [Tooltip("允许遮罩触发时启用/禁用 extraChildren。")]
    public bool allowChildrenMaskControl = true;

    [Header("变种隐形行为")]
    [Tooltip("启用后：被检测时将完全显形（不受遮罩限制），并淡变渐显；然后倒计时 variantRevealHoldSeconds 后进入隐形；隐形前先频闪 variantPreHideFlickerSeconds，再淡变渐隐。")]
    public bool enableVariantHiddenBehavior = false;
    [Tooltip("变种显形持续秒数（到时后自动隐形）。")]
    public float variantRevealHoldSeconds = 10f;
    [Tooltip("隐形前频闪时长（秒）。")]
    public float variantPreHideFlickerSeconds = 2f;
    [Tooltip("频闪频率（次/秒）。")]
    public float variantFlickerFrequency = 8f;
    [Tooltip("隐形前频闪最低透明度（0-1），避免完全透明。")]
    public float variantFlickerMinAlpha = 0.3f;
    [Tooltip("隐形前频闪最高透明度（0-1）。")]
    public float variantFlickerMaxAlpha = 1f;
    
    // 渐变目标集合
    private SpriteRenderer[] spriteRenderers;
    private Component[] tk2dSprites; // 延迟获取 tk2dBaseSprite（避免硬依赖）
    private CanvasGroup[] canvasGroups;
    private Collider2D[] colliders2D;

    // 运行时状态
    private bool isRevealed;
    private bool initialized;
    private Coroutine fadeRoutine;
    private readonly HashSet<object> activeRevealers = new HashSet<object>();
    private readonly HashSet<object> activeConcealers = new HashSet<object>();
    private bool hasFullyRevealed;

    // 变种隐形运行时
    private Coroutine variantCycleRoutine;
    private bool _spriteMaskLockedByVariant = false;

    // 防重入/安全切换标记
    private bool _isApplyingChildrenActive = false;
    private int _lastChildrenToggleFrame = -1;

    private void Awake()
    {
        // 收集常见渲染组件
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        canvasGroups = GetComponentsInChildren<CanvasGroup>(true);
        colliders2D = GetComponentsInChildren<Collider2D>(true);

        // 尝试获取 tk2d 基类（若项目使用 tk2d）。
        // 通过类型名反射方式获取，避免编译失败（某些场景 tk2d 可能被移除）。
        var tkType = System.Type.GetType("tk2dBaseSprite");
        if (tkType != null)
        {
            tk2dSprites = GetComponentsInChildren(tkType, true);
        }
        else
        {
            tk2dSprites = new Component[0];
        }
    }

    private void OnEnable()
    {
        registry.Add(this);
        // 初始化可见状态
        if (!initialized)
        {
            initialized = true;
            SetVisible(!startHidden, immediate: true);
            // 新增：初始隐形但不禁用 Collider 的选项
            if (startHidden && !disableCollidersOnInitialHidden && colliders2D != null)
            {
                for (int i = 0; i < colliders2D.Length; i++)
                {
                    var col = colliders2D[i];
                    if (col != null) col.enabled = true;
                }
            }
        }
        else
        {
            // 恢复当前状态
            SetVisible(isRevealed, immediate: true);
        }
    }

    private void OnDisable()
    {
        registry.Remove(this);
        StopFade();
    }

    private void OnDestroy()
    {
        registry.Remove(this);
    }

    public bool IsRevealed => isRevealed;

    /// <summary>
    /// 将对象显形（可渐显）。
    /// </summary>
    public void Reveal(bool immediate = false)
    {
        SetVisible(true, immediate);
    }

    /// <summary>
    /// 将对象隐形（可渐隐）。
    /// </summary>
    public void Conceal(bool immediate = false)
    {
        SetVisible(false, immediate);
    }

    /// <summary>
    /// 供范围触发器调用：进入范围。
    /// </summary>
    public void OnRevealerEnter(object source)
    {
        if (source == null) return;
        if (!activeRevealers.Contains(source))
        {
            activeRevealers.Add(source);
            // 变种隐形：检测到后开始一次完整显形-计时-频闪-隐形的周期
            if (enableVariantHiddenBehavior && startHidden)
            {
                StartVariantRevealCycle();
                return;
            }
            if (onlyAffectOriginallyHidden && !startHidden)
            {
                // 初始可见且仅影响“原本隐形”，则不自动改变
                return;
            }
            SetVisible(true, immediate: false, touchColliders: !keepCollidersStateOnDetection);
        }
    }

    /// <summary>
    /// 供范围触发器调用：离开范围。
    /// </summary>
    public void OnRevealerExit(object source)
    {
        if (source == null) return;
        if (activeRevealers.Contains(source))
        {
            activeRevealers.Remove(source);
            // 变种隐形：离开范围不立即隐形，继续走倒计时周期
            if (enableVariantHiddenBehavior && startHidden)
            {
                return;
            }
            if (activeRevealers.Count == 0)
            {
                // 若已完全显形且开启持久显形，则不再自动恢复隐形
                if (persistAfterFullReveal && hasFullyRevealed)
                {
                    return;
                }
                if (onlyAffectOriginallyHidden)
                {
                    if (startHidden)
                    {
                        SetVisible(false, immediate: false, touchColliders: !keepCollidersStateOnDetection);
                    }
                }
                else
                {
                    SetVisible(false, immediate: false, touchColliders: !keepCollidersStateOnDetection);
                }
            }
        }
    }

    /// <summary>
    /// 供范围触发器调用：进入范围（用于强制隐形原本可见对象）。
    /// </summary>
    public void OnConcealerEnter(object source)
    {
        if (!allowConcealOriginallyVisible) return;
        if (source == null) return;
        if (!activeConcealers.Contains(source))
        {
            activeConcealers.Add(source);
            SetVisible(false, immediate: false, touchColliders: !keepCollidersStateOnDetection);
        }
    }

    /// <summary>
    /// 供范围触发器调用：离开范围（用于强制隐形原本可见对象）。
    /// </summary>
    public void OnConcealerExit(object source)
    {
        if (!allowConcealOriginallyVisible) return;
        if (source == null) return;
        if (activeConcealers.Contains(source))
        {
            activeConcealers.Remove(source);
            if (activeConcealers.Count == 0)
            {
                SetVisible(true, immediate: false, touchColliders: !keepCollidersStateOnDetection);
            }
        }
    }

    /// <summary>
    /// 添加/移除需要跟随显隐启用/禁用的子物体。
    /// </summary>
    public void AddChild(GameObject child)
    {
        if (child != null && !extraChildren.Contains(child))
        {
            extraChildren.Add(child);
            // 按当前状态立即应用
            ApplyChildrenActive(isRevealed);
        }
    }
    public void RemoveChild(GameObject child)
    {
        if (child != null && extraChildren.Contains(child))
        {
            extraChildren.Remove(child);
        }
    }

    private void SetVisible(bool visible, bool immediate, bool touchColliders = true, bool forceFade = false)
    {
        isRevealed = visible;
        StopFade();
        float duration = visible ? fadeInDuration : fadeOutDuration;
        bool doFade = !immediate && duration > 0f && (enableFade || forceFade);
        if (!doFade)
        {
            float alpha = visible ? 1f : 0f;
            SetAlphaInstant(alpha);
            if (visible)
            {
                hasFullyRevealed = true;
            }
            // 立即模式下：子物体与交互立即切换
            if (disableChildrenWhenHidden)
            {
                ApplyChildrenActive(visible);
            }
            if (touchColliders)
            {
                ApplyInteractiveState(visible);
            }
        }
        else
        {
            if (visible)
            {
                // 显形：先启用子物体，再渐显
                if (disableChildrenWhenHidden) ApplyChildrenActive(true);
                // 显形开始时启用交互（可选）
                if (touchColliders) ApplyInteractiveState(true);
                fadeRoutine = StartCoroutine(FadeTo(1f, duration, disableChildrenAtEnd: false, toggleCollidersAtEnd: false, markFullyRevealedAtEnd: true));
            }
            else
            {
                // 隐形：先渐隐，渐隐完成后再禁用子物体
                fadeRoutine = StartCoroutine(FadeTo(0f, duration, disableChildrenAtEnd: disableChildrenWhenHidden, toggleCollidersAtEnd: (touchColliders && disableCollidersWhenHidden), markFullyRevealedAtEnd: false));
            }
        }
    }

    private void ApplyChildrenActive(bool visible)
    {
        if (!disableChildrenWhenHidden) return;
        if (extraChildren == null) return;

        // 避免同帧重复切换导致 Unity 报错："GameObject is already being activated or deactivated"
        if (_isApplyingChildrenActive) return;
        if (_lastChildrenToggleFrame == Time.frameCount) return;
        _isApplyingChildrenActive = true;
        _lastChildrenToggleFrame = Time.frameCount;
        try
        {
            for (int i = 0; i < extraChildren.Count; i++)
            {
                var child = extraChildren[i];
                if (child == null) continue;
                // 安全：禁止把自身加入列表，避免在显隐过程中递归切换自身导致报错
                if (child == this.gameObject) continue;
                // 仅在状态不一致时切换，避免无意义重复调用
                if (child.activeSelf != visible)
                {
                    child.SetActive(visible);
                }
            }
        }
        finally
        {
            _isApplyingChildrenActive = false;
        }
    }

    private void ApplyInteractiveState(bool visible)
    {
        // 交互功能：禁用/启用 Collider2D（与子物体开关独立）
        if (!disableCollidersWhenHidden) return;
        if (colliders2D == null) return;
        for (int i = 0; i < colliders2D.Length; i++)
        {
            var col = colliders2D[i];
            if (col != null)
            {
                col.enabled = visible;
            }
        }
    }

    private void StopFade()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
    }

    private IEnumerator FadeTo(float targetAlpha, float duration, bool disableChildrenAtEnd, bool toggleCollidersAtEnd, bool markFullyRevealedAtEnd)
    {
        // 读取当前 alpha（取第一个可用组件的当前值）
        float startAlpha = GetCurrentAlpha();
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            float k = fadeCurve != null ? fadeCurve.Evaluate(p) : p;
            float a = Mathf.Lerp(startAlpha, targetAlpha, k);
            SetAlphaInstant(a);
            yield return null;
        }
        SetAlphaInstant(targetAlpha);
        if (markFullyRevealedAtEnd && targetAlpha >= 1f)
        {
            hasFullyRevealed = true;
        }
        // 渐隐完成后再禁用子物体（若仍处于隐形状态且启用该选项）
        if (disableChildrenAtEnd && targetAlpha <= 0f && !isRevealed)
        {
            ApplyChildrenActive(false);
        }
        // 渐隐完成后禁用交互（若仍处于隐形状态且启用该选项）
        if (toggleCollidersAtEnd && targetAlpha <= 0f && !isRevealed)
        {
            ApplyInteractiveState(false);
        }
        fadeRoutine = null;
    }

    private float GetCurrentAlpha()
    {
        // 优先从 SpriteRenderer 读取
        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                var sr = spriteRenderers[i];
                if (sr != null)
                {
                    return sr.color.a;
                }
            }
        }
        // tk2d BaseSprite
        if (tk2dSprites != null)
        {
            for (int i = 0; i < tk2dSprites.Length; i++)
            {
                var comp = tk2dSprites[i];
                if (comp != null)
                {
                    var clrProp = comp.GetType().GetProperty("color");
                    if (clrProp != null)
                    {
                        Color c = (Color)clrProp.GetValue(comp, null);
                        return c.a;
                    }
                }
            }
        }
        // CanvasGroup
        if (canvasGroups != null)
        {
            for (int i = 0; i < canvasGroups.Length; i++)
            {
                var cg = canvasGroups[i];
                if (cg != null)
                {
                    return cg.alpha;
                }
            }
        }
        return isRevealed ? 1f : 0f;
    }

    private void SetAlphaInstant(float a)
    {
        // SpriteRenderer
        if (spriteRenderers != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                var sr = spriteRenderers[i];
                if (sr != null)
                {
                    Color c = sr.color;
                    c.a = a;
                    sr.color = c;
                }
            }
        }
        // tk2d BaseSprite
        if (tk2dSprites != null)
        {
            for (int i = 0; i < tk2dSprites.Length; i++)
            {
                var comp = tk2dSprites[i];
                if (comp != null)
                {
                    var clrProp = comp.GetType().GetProperty("color");
                    if (clrProp != null)
                    {
                        Color c = (Color)clrProp.GetValue(comp, null);
                        c.a = a;
                        clrProp.SetValue(comp, c, null);
                    }
                }
            }
        }
        // CanvasGroup
        if (canvasGroups != null)
        {
            for (int i = 0; i < canvasGroups.Length; i++)
            {
                var cg = canvasGroups[i];
                if (cg != null)
                {
                    cg.alpha = a;
                }
            }
        }
    }

    public enum MaskMode
    {
        None,
        VisibleInside,
        VisibleOutside
    }

    /// <summary>
    /// 设置当前对象下所有 SpriteRenderer 的 SpriteMask 交互模式。
    /// </summary>
    public void ApplySpriteMaskMode(MaskMode mode)
    {
        if (_spriteMaskLockedByVariant) return; // 变种隐形期间锁定外部遮罩控制
        if (!allowSpriteMaskControl) return;
        if (spriteRenderers == null) return;
        SpriteMaskInteraction toInteraction = SpriteMaskInteraction.None;
        switch (mode)
        {
            case MaskMode.VisibleInside:
                toInteraction = SpriteMaskInteraction.VisibleInsideMask;
                break;
            case MaskMode.VisibleOutside:
                toInteraction = SpriteMaskInteraction.VisibleOutsideMask;
                break;
            case MaskMode.None:
            default:
                toInteraction = SpriteMaskInteraction.None;
                break;
        }
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            var sr = spriteRenderers[i];
            if (sr != null)
            {
                sr.maskInteraction = toInteraction;
            }
        }
    }

    // 强制设置 SpriteMask 模式，忽略外部控制开关
    private void ForceSpriteMaskMode(MaskMode mode)
    {
        if (spriteRenderers == null) return;
        SpriteMaskInteraction toInteraction = SpriteMaskInteraction.None;
        switch (mode)
        {
            case MaskMode.VisibleInside:
                toInteraction = SpriteMaskInteraction.VisibleInsideMask;
                break;
            case MaskMode.VisibleOutside:
                toInteraction = SpriteMaskInteraction.VisibleOutsideMask;
                break;
            case MaskMode.None:
            default:
                toInteraction = SpriteMaskInteraction.None;
                break;
        }
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            var sr = spriteRenderers[i];
            if (sr != null)
            {
                sr.maskInteraction = toInteraction;
            }
        }
    }

    // 变种隐形：开始一次完整周期
    private void StartVariantRevealCycle()
    {
        if (!enableVariantHiddenBehavior) return;
        // 锁定外部遮罩控制并强制全局可见（不受遮罩）
        ForceSpriteMaskMode(MaskMode.None);
        _spriteMaskLockedByVariant = true;
        // 重启周期
        if (variantCycleRoutine != null)
        {
            StopCoroutine(variantCycleRoutine);
            variantCycleRoutine = null;
        }
        variantCycleRoutine = StartCoroutine(VariantCycle());
    }

    private IEnumerator VariantCycle()
    {
        // 1) 淡变渐显（强制淡变），且不碰撞体（遵循 keepCollidersStateOnDetection）
        SetVisible(true, immediate: false, touchColliders: !keepCollidersStateOnDetection, forceFade: true);

        // 2) 显形维持：总时长减去频闪时长
        float hold = Mathf.Max(0f, variantRevealHoldSeconds - variantPreHideFlickerSeconds);
        if (hold > 0f)
        {
            yield return new WaitForSeconds(hold);
        }

        // 3) 隐形前频闪
        float flickerTime = Mathf.Max(0f, variantPreHideFlickerSeconds);
        if (flickerTime > 0f)
        {
            float freq = Mathf.Max(0.1f, variantFlickerFrequency);
            float halfPeriod = 0.5f / freq; // 半周期翻转一次
            float t = 0f;
            float low = Mathf.Clamp01(variantFlickerMinAlpha);
            float high = Mathf.Clamp01(variantFlickerMaxAlpha);
            if (high < low) high = low; // 保证上限不低于下限
            bool on = true;
            while (t < flickerTime)
            {
                SetAlphaInstant(on ? low : high);
                on = !on;
                yield return new WaitForSeconds(halfPeriod);
                t += halfPeriod;
            }
            // 频闪结束恢复为高透明度，再进入淡变渐隐
            SetAlphaInstant(high);
        }

        // 4) 淡变渐隐（强制淡变）。在隐形结束后解锁遮罩控制
        SetVisible(false, immediate: false, touchColliders: !keepCollidersStateOnDetection, forceFade: true);
        _spriteMaskLockedByVariant = false;
        variantCycleRoutine = null;
    }

    // 允许遮罩模式直接控制子物体启用/禁用（不改变全局显隐状态/Collider）
    public void SetExtraChildrenActive(bool active)
    {
        if (!allowChildrenMaskControl) return;
        if (extraChildren == null) return;

        if (_isApplyingChildrenActive) return;
        if (_lastChildrenToggleFrame == Time.frameCount) return;
        _isApplyingChildrenActive = true;
        _lastChildrenToggleFrame = Time.frameCount;
        try
        {
            for (int i = 0; i < extraChildren.Count; i++)
            {
                var child = extraChildren[i];
                if (child == null) continue;
                if (child == this.gameObject) continue;
                if (child.activeSelf != active)
                {
                    child.SetActive(active);
                }
            }
        }
        finally
        {
            _isApplyingChildrenActive = false;
        }
    }
}