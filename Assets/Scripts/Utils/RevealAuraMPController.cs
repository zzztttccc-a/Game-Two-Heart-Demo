using UnityEngine;

/// <summary>
/// 纯代码方案：不使用 PlayMaker FSM，直接脚本控制显形范围开关与持续消耗 MP。
/// 挂到角色（含 HeroController）对象上，配置 auraRoot 引用即可使用。
///
/// 功能：
/// - 按键/接口开启显形范围（激活 auraRoot），并调用 HeroController.StartMPDrain(drainInterval) 持续扣 MP。
/// - 为避免触发 HeroCtrl-FocusCompleted（原版治疗完成事件），在开启时临时将 PlayerData.focusMP_amount 提高至一个很大值；关闭时恢复原值。
/// - 支持 MP 用尽自动关闭；在脚本禁用/销毁时自动收尾。
/// - 同时提供公共方法与属性，供其它脚本自由呼叫。
/// </summary>
public class RevealAuraMPController : MonoBehaviour
{
    [Header("显形范围引用")]
    [Tooltip("显形范围的预制体实例（挂在角色身上的子物体）。开启时将激活该对象，关闭时将禁用该对象。")]
    public GameObject auraRoot;

    [Header("持续消耗 MP 配置")]
    [Tooltip("每次扣 1 点 MP 的间隔秒数，建议 0.027（与原始节奏一致）。")]
    public float drainInterval = 0.027f;
    [Tooltip("开启显形时，为避免触发 Focus 完成事件而临时设置的超大阈值（例如 9999）。")]
    public int focusBypassLarge = 9999;
    [Tooltip("开启显形所需的最低 MP（不足则不开启）。")]
    public int minMPToEnable = 1;
    [Tooltip("MP 用尽时自动关闭显形与停止扣 MP。")]
    public bool autoCloseAuraOnMPEmpty = true;

    [Header("输入控制（可选）")]
    [Tooltip("是否启用脚本内的按键切换。关闭后只使用外部脚本调用 API 控制开关。")]
    public bool enableToggleInput = true;
    [Tooltip("切换键（默认 Q）。")]
    public KeyCode toggleKey = KeyCode.Q;
    [Tooltip("按住开启、松开关闭（true）；按一次切换开关（false）。")]
    public bool toggleOnHold = false;

    [Header("FSM/外部控制")]
    [Tooltip("通过 FSM/脚本设置的请求开关；true=请求开启，false=请求关闭。")]
    public bool areaOpen = false;
    private bool auraActive = false;
    private int focusOriginal = 33;

    private HeroController hero;
    private PlayerData playerData;

    private void Awake()
    {
        hero = GetComponent<HeroController>();
        playerData = PlayerData.instance;
        if (auraRoot == null)
        {
            Debug.LogWarning("RevealAuraMPController: auraRoot 未设置，请在 Inspector 中指定显形范围对象。");
        }
        if (hero == null)
        {
            Debug.LogError("RevealAuraMPController: 未找到 HeroController，请将本脚本挂到含 HeroController 的角色对象上。");
        }
        if (playerData == null)
        {
            Debug.LogError("RevealAuraMPController: PlayerData.instance 为 null，无法读取/设置 MP 相关参数。");
        }
    }

    private void Update()
    {
        // 输入切换（可选）
        if (enableToggleInput)
        {
            if (!toggleOnHold)
            {
                if (Input.GetKeyDown(toggleKey))
                {
                    // 使用 areaOpen 为主控
                    SetAreaOpen(!areaOpen);
                }
            }
            else
            {
                if (Input.GetKeyDown(toggleKey))
                {
                    SetAreaOpen(true);
                }
                if (Input.GetKeyUp(toggleKey))
                {
                    SetAreaOpen(false);
                }
            }
        }

        // 同步 areaOpen 请求到实际状态
        if (hero != null && playerData != null)
        {
            if (areaOpen && !auraActive)
            {
                EnableAura();
            }
            else if (!areaOpen && auraActive)
            {
                DisableAura();
            }
        }

        // 运行期：MP 用尽自动关闭（同时将 areaOpen 置为 false）
        if (auraActive && autoCloseAuraOnMPEmpty && playerData != null)
        {
            if (playerData.MPCharge <= 0)
            {
                areaOpen = false;
                DisableAura();
            }
        }
    }

    /// <summary>
    /// 开启显形范围，并开始持续扣 MP。
    /// </summary>
    public void EnableAura()
    {
        if (auraActive) { areaOpen = true; return; }
        if (hero == null || playerData == null)
        {
            Debug.LogWarning("RevealAuraMPController: 缺少 HeroController 或 PlayerData，无法开启显形范围。");
            areaOpen = false;
            return;
        }
        // 标记期望为开启
        areaOpen = true;

        if (playerData.MPCharge < minMPToEnable)
        {
            Debug.Log("RevealAuraMPController: MP 不足，拒绝开启显形范围。");
            areaOpen = false;
            return;
        }

        focusOriginal = playerData.GetInt("focusMP_amount");
        playerData.SetInt("focusMP_amount", focusBypassLarge);

        hero.StartMPDrain(drainInterval);

        if (auraRoot != null) auraRoot.SetActive(true);
        auraActive = true;
    }

    /// <summary>
    /// 关闭显形范围，并停止持续扣 MP，恢复 Focus 阈值。
    /// </summary>
    public void DisableAura()
    {
        // 标记期望为关闭
        areaOpen = false;

        if (!auraActive)
        {
            if (auraRoot != null) auraRoot.SetActive(false);
            return;
        }
        auraActive = false;

        if (hero != null)
        {
            hero.StopMPDrain();
        }

        if (playerData != null)
        {
            playerData.SetInt("focusMP_amount", focusOriginal);
        }

        if (auraRoot != null) auraRoot.SetActive(false);
    }

    /// <summary>
    /// 统一开关接口。
    /// </summary>
    public void SetAuraActive(bool active)
    {
        if (active) EnableAura(); else DisableAura();
    }

    public bool IsAuraActive => auraActive;

    private void OnDisable()
    {
        // 在脚本禁用时确保收尾（防止留下大阈值或持续扣 MP）
        if (auraActive) DisableAura();
    }

    private void OnDestroy()
    {
        // 在销毁时确保收尾
        if (auraActive) DisableAura();
    }

    public bool AreaOpen
    {
        get => areaOpen;
        set => SetAreaOpen(value);
    }

    public void SetAreaOpen(bool open)
    {
        if (open)
        {
            EnableAura();
        }
        else
        {
            DisableAura();
        }
    }

    public void SetAreaOpenTrue()
    {
        SetAreaOpen(true);
    }

    public void SetAreaOpenFalse()
    {
        SetAreaOpen(false);
    }

    // Keep ToggleAura but update to use areaOpen
    public void ToggleAura()
    {
        SetAreaOpen(!areaOpen);
    }
}