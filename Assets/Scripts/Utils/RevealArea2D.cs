using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 圆形范围显形触发器。
/// - 在半径范围内检测挂载了 RevealHideController 的物体，使其显形；离开范围则恢复（根据控制器配置）。
/// - 通过控制器的全局注册进行距离检测，不依赖碰撞体。
/// </summary>
public class RevealArea2D : MonoBehaviour
{
    [Tooltip("触发半径（世界单位）")]
    public float radius = 5f;

    [Tooltip("仅影响指定 Layer 的对象（基于对象所在 Layer）。不勾选任何位时默认影响全部。")]
    public LayerMask targetLayers = ~0;

    [Tooltip("是否每帧运行检测。为 true 时使用 Update() 距离检测；为 false 可在外部手动调用 ScanOnce()。")]
    public bool autoScan = true;

    [Header("遮罩与行为模式")]
    [Tooltip("启用 SpriteMask 作为可视遮罩。会自动创建或复用本物体上的 SpriteMask 并根据半径与位置同步。")]
    public bool useSpriteMask = true;

    [Tooltip("范围内的行为：为 true 则在范围内隐藏（ConcealInside）；为 false 则在范围内显形（RevealInside）。")]
    public bool concealInside = true;

    [Tooltip("用于 SpriteMask 的圆形精灵（若为空则在运行时生成一个圆形遮罩贴图）。")]
    public Sprite maskSprite;

    [Tooltip("为目标对象的 SpriteRenderer 设置 MaskInteraction（None/Inside/Outside），以配合 SpriteMask 生效。")]
    public bool setMaskInteractionOnTargets = true;

    [Tooltip("SpriteMask 前后排序范围，影响哪些精灵会被遮罩。数值越宽，覆盖层级越多。")]
    public int maskBackSortingOrder = -32768;
    public int maskFrontSortingOrder = 32767;

    private SpriteMask maskComp;
    [Tooltip("并行模式：范围内同时对原本隐形的对象显形、对原本显形的对象隐形。开启后不使用 concealInside 切换。")]
    public bool dualMode = true;

    [Header("检测精度")]
    [Tooltip("使用 CircleCollider2D 触发进行检测（更精确，与遮罩绑定）。开启后将自动创建/配置触发器并与半径同步。")]
    public bool useColliderTrigger = true;

    [Tooltip("在触发器模式下，将 SpriteMask 的显示半径与物理触发半径绑定同步。")]
    public bool bindMaskToTrigger = true;

    private CircleCollider2D triggerCol;
    private Rigidbody2D triggerRb;

    // 已在范围内的控制器映射（记录当前在范围内对其采取的模式），用于判断进入/离开
    private enum InsideMode { Reveal, ConcealMaskOnly, Conceal }
    private readonly Dictionary<RevealHideController, InsideMode> insideMap = new Dictionary<RevealHideController, InsideMode>();
    // 计数器：支持同一个控制器存在多个子 Collider 的情况
    private readonly Dictionary<RevealHideController, int> insideCounter = new Dictionary<RevealHideController, int>();

    private void Update()
    {
        if (autoScan && !useColliderTrigger)
        {
            ScanOnce();
        }
        if (useSpriteMask && maskComp != null)
        {
            UpdateMaskTransform();
        }
    }

    /// <summary>
    /// 进行一次范围检测。
    /// </summary>
    public void ScanOnce()
    {
        var controllers = RevealHideController.AllControllers;
        Vector3 center = transform.position;
        float rSqr = radius * radius;

        // 标记，将在本次循环后处理离开事件
        var stillInside = new HashSet<RevealHideController>();

        foreach (var ctrl in controllers)
        {
            if (ctrl == null) continue;
            var go = ctrl.gameObject;
            int layer = go.layer;
            // Layer 过滤
            if ((targetLayers.value & (1 << layer)) == 0)
            {
                continue;
            }

            Vector3 pos = go.transform.position;
            float distSqr = (pos - center).sqrMagnitude;
            bool inside = distSqr <= rSqr;

            if (inside)
            {
                stillInside.Add(ctrl);
                if (!insideMap.ContainsKey(ctrl))
                {
                    if (dualMode)
                    {
                        // 并行模式：原本隐形 -> 显形（配合遮罩仅在内部可见）；原本显形 -> 仅遮罩内部隐形（不全局 Conceal）
                        if (ctrl.startHidden)
                        {
                            insideMap[ctrl] = InsideMode.Reveal;
                            ctrl.OnRevealerEnter(this);
                            if (setMaskInteractionOnTargets)
                            {
                                ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleInside);
                            }
                        }
                        else
                        {
                            // 注意：这里不调用 OnConcealerEnter，以免把原本可见对象全局隐藏并禁用 Collider
                            insideMap[ctrl] = InsideMode.ConcealMaskOnly;
                            if (setMaskInteractionOnTargets)
                            {
                                ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleOutside);
                            }
                            // 新增：原本显形对象进入遮罩时，禁用其 extraChildren
                            ctrl.SetExtraChildrenActive(false);
                        }
                    }
                    else
                    {
                        // 兼容旧逻辑：按 concealInside 决定单一模式
                        if (concealInside)
                        {
                            insideMap[ctrl] = InsideMode.Conceal;
                            ctrl.OnConcealerEnter(this);
                            if (setMaskInteractionOnTargets)
                            {
                                ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleOutside);
                            }
                        }
                        else
                        {
                            insideMap[ctrl] = InsideMode.Reveal;
                            ctrl.OnRevealerEnter(this);
                            if (setMaskInteractionOnTargets)
                            {
                                ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleInside);
                            }
                        }
                    }
                }
            }
        }

        // 处理离开范围的控制器
        if (insideMap.Count > 0)
        {
            // 为避免集合修改冲突，使用临时列表
            var toRemove = new List<RevealHideController>();
            foreach (var ctrl in insideMap.Keys)
            {
                if (!stillInside.Contains(ctrl))
                {
                    // 根据进入时采取的模式进行对应的退出
                    InsideMode mode;
                    if (insideMap.TryGetValue(ctrl, out mode))
                    {
                        if (mode == InsideMode.Conceal)
                        {
                            ctrl?.OnConcealerExit(this);
                        }
                        else if (mode == InsideMode.Reveal)
                        {
                            ctrl?.OnRevealerExit(this);
                        }
                        else if (mode == InsideMode.ConcealMaskOnly)
                        {
                            // 新增：离开遮罩时恢复子物体
                            ctrl?.SetExtraChildrenActive(true);
                        }
                    }
                    if (setMaskInteractionOnTargets)
                    {
                        ctrl?.ApplySpriteMaskMode(RevealHideController.MaskMode.None);
                    }
                    toRemove.Add(ctrl);
                }
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                insideMap.Remove(toRemove[i]);
            }
        }
    }

    private void OnDisable()
    {
        // 清理：离开事件
        if (insideMap.Count > 0)
        {
            foreach (var kv in insideMap)
            {
                var ctrl = kv.Key;
                var mode = kv.Value;
                if (mode == InsideMode.Conceal)
                {
                    ctrl?.OnConcealerExit(this);
                }
                else if (mode == InsideMode.Reveal)
                {
                    ctrl?.OnRevealerExit(this);
                }
                // ConcealMaskOnly：仅遮罩切换，无需调用控制器的退出（避免错误恢复/交互切换）
                if (setMaskInteractionOnTargets)
                {
                    ctrl?.ApplySpriteMaskMode(RevealHideController.MaskMode.None);
                }
                // 新增：离开遮罩时恢复子物体
                if (mode == InsideMode.ConcealMaskOnly)
                {
                    ctrl?.SetExtraChildrenActive(true);
                }
            }
            // 修正：枚举结束后再清空集合，避免“在枚举期间修改集合”的错误
            insideMap.Clear();
            insideCounter.Clear();
        }
        // 不再需要遮罩时可禁用组件
        if (maskComp != null)
        {
            maskComp.enabled = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawSphere(transform.position, 0.06f);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        // 画圆（2D），使用线环模拟
        const int segments = 50;
        Vector3 center = transform.position;
        float angleStep = 360f / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float ang = angleStep * i * Mathf.Deg2Rad;
            Vector3 next = center + new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    private void OnEnable()
    {
        if (useSpriteMask)
        {
            SetupMask();
        }
        if (useColliderTrigger)
        {
            SetupCollider();
        }
    }

    private void OnValidate()
    {
        if (useSpriteMask)
        {
            SetupMask();
        }
        if (useColliderTrigger)
        {
            SetupCollider();
        }
    }

    private void SetupMask()
    {
        maskComp = GetComponent<SpriteMask>();
        if (maskComp == null)
        {
            maskComp = gameObject.AddComponent<SpriteMask>();
        }
        // 贴图与形状
        if (maskSprite == null)
        {
            maskComp.sprite = GenerateCircleSprite(128, 100);
        }
        else
        {
            maskComp.sprite = maskSprite;
        }
        // 排序范围
        maskComp.backSortingOrder = maskBackSortingOrder;
        maskComp.frontSortingOrder = maskFrontSortingOrder;
        maskComp.enabled = true;
        UpdateMaskTransform();
    }

    private void SetupCollider()
    {
        triggerCol = GetComponent<CircleCollider2D>();
        if (triggerCol == null)
        {
            triggerCol = gameObject.AddComponent<CircleCollider2D>();
        }
        triggerCol.isTrigger = true;
        // 2D 触发事件需要至少一方存在 Rigidbody2D，这里为触发区自动配置一个静态刚体
        triggerRb = GetComponent<Rigidbody2D>();
        if (triggerRb == null)
        {
            triggerRb = gameObject.AddComponent<Rigidbody2D>();
        }
        triggerRb.bodyType = RigidbodyType2D.Kinematic;
        triggerRb.simulated = true;
        triggerRb.gravityScale = 0f;
        triggerRb.angularDrag = 0f;
        triggerRb.freezeRotation = true;
    
        // 将世界半径转换为本地半径（考虑非统一缩放时取 XY 最大轴）
        Vector3 s = transform.lossyScale;
        float axis = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), 0.0001f);
        triggerCol.radius = radius / axis;
        // 遮罩绑定：确保 SpriteMask 与触发器半径一致
        if (useSpriteMask && bindMaskToTrigger)
        {
            UpdateMaskTransform();
        }
    }

    private void UpdateMaskTransform()
    {
        if (maskComp == null || maskComp.sprite == null) return;
        maskComp.transform.position = transform.position;
        // 根据 sprite 尺寸缩放到半径
        Vector2 size = maskComp.sprite.bounds.size;
        float scaleX = (radius * 2f) / Mathf.Max(size.x, 0.0001f);
        float scaleY = (radius * 2f) / Mathf.Max(size.y, 0.0001f);
        maskComp.transform.localScale = new Vector3(scaleX, scaleY, 1f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!useColliderTrigger) return;
        if (other == null) return;
        var ctrl = other.GetComponentInParent<RevealHideController>();
        if (ctrl == null) return;
        // Layer 过滤
        int layer = ctrl.gameObject.layer;
        if ((targetLayers.value & (1 << layer)) == 0) return;

        int count;
        insideCounter.TryGetValue(ctrl, out count);
        count++;
        insideCounter[ctrl] = count;
        if (count == 1)
        {
            if (dualMode)
            {
                if (ctrl.startHidden)
                {
                    insideMap[ctrl] = InsideMode.Reveal;
                    ctrl.OnRevealerEnter(this);
                    if (setMaskInteractionOnTargets)
                        ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleInside);
                }
                else
                {
                    // 并行模式：原本显形对象仅做遮罩内部隐形，不进行全局 Conceal
                    insideMap[ctrl] = InsideMode.ConcealMaskOnly;
                    if (setMaskInteractionOnTargets)
                        ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleOutside);
                    // 新增：原本显形对象进入遮罩时，禁用其 extraChildren
                    ctrl.SetExtraChildrenActive(false);
                }
            }
            else
            {
                if (concealInside)
                {
                    insideMap[ctrl] = InsideMode.Conceal;
                    ctrl.OnConcealerEnter(this);
                    if (setMaskInteractionOnTargets)
                        ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleOutside);
                }
                else
                {
                    insideMap[ctrl] = InsideMode.Reveal;
                    ctrl.OnRevealerEnter(this);
                    if (setMaskInteractionOnTargets)
                        ctrl.ApplySpriteMaskMode(RevealHideController.MaskMode.VisibleInside);
                }
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!useColliderTrigger) return;
        if (other == null) return;
        var ctrl = other.GetComponentInParent<RevealHideController>();
        if (ctrl == null) return;
        // Layer 过滤
        int layer = ctrl.gameObject.layer;
        if ((targetLayers.value & (1 << layer)) == 0) return;

        int count;
        if (!insideCounter.TryGetValue(ctrl, out count)) return;
        count = Mathf.Max(0, count - 1);
        if (count == 0)
        {
            insideCounter.Remove(ctrl);
            InsideMode mode;
            if (insideMap.TryGetValue(ctrl, out mode))
            {
                if (mode == InsideMode.Conceal)
                {
                    ctrl?.OnConcealerExit(this);
                }
                else if (mode == InsideMode.Reveal)
                {
                    ctrl?.OnRevealerExit(this);
                }
                // ConcealMaskOnly：仅遮罩切换，无需调用控制器的退出（避免错误恢复/交互切换）
                if (setMaskInteractionOnTargets)
                {
                    ctrl?.ApplySpriteMaskMode(RevealHideController.MaskMode.None);
                }
                // 新增：离开遮罩时恢复子物体
                if (mode == InsideMode.ConcealMaskOnly)
                {
                    ctrl?.SetExtraChildrenActive(true);
                }
                insideMap.Remove(ctrl);
            }
        }
        else
        {
            insideCounter[ctrl] = count;
        }
    }

    private Sprite GenerateCircleSprite(int size, int pixelsPerUnit)
    {
        // 生成一个中间不透明、边缘硬切的圆形贴图
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float r = size * 0.5f - 1f;
        Vector2 c = new Vector2(r + 1f, r + 1f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                float a = d <= r ? 1f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, true);
        Rect rect = new Rect(0f, 0f, size, size);
        Vector2 pivot = new Vector2(0.5f, 0.5f);
        return Sprite.Create(tex, rect, pivot, pixelsPerUnit);
    }
}