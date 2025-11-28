using UnityEngine;

/// <summary>
/// 一次性开关的按钮：玩家接触（或按键）后触发绑定的 AttackSwitch。
/// 默认：进入触发器即触发一次；可选：需要按键确认。
/// 使用：
/// 1) 在场景中放置一个带 Collider2D（IsTrigger=true）的按钮物体，并挂载本脚本；
/// 2) 将 targetSwitch 指向要控制的 AttackSwitch；
/// 3) 若需要按键确认，勾选 requirePressKey（默认键 E）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class OneShotButton : MonoBehaviour
{
    [Tooltip("要触发的一次性开关（AttackSwitch），建议将其 toggleOnEachHit 设为 false 以只执行一次")]
    public AttackSwitch targetSwitch;

    [Header("按键设置（可选）")]
    public bool requirePressKey = false;
    public KeyCode interactKey = KeyCode.E;

    private bool heroInside;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null || targetSwitch == null) return;
        // 通过检测 HeroController 判断是玩家
        var hero = other.GetComponent<HeroController>();
        if (hero != null)
        {
            heroInside = true;
            if (!requirePressKey)
            {
                targetSwitch.TriggerOnce();
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var hero = other.GetComponent<HeroController>();
        if (hero != null)
        {
            heroInside = false;
        }
    }

    private void Update()
    {
        if (requirePressKey && heroInside && targetSwitch != null)
        {
            if (Input.GetKeyDown(interactKey))
            {
                targetSwitch.TriggerOnce();
            }
        }
    }
}