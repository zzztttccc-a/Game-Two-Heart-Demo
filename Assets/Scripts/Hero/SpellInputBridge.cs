using UnityEngine;

public class SpellInputBridge : MonoBehaviour
{
    [SerializeField] private CloneSelectionManager selectionManager;
    [SerializeField] private float swapCooldownSeconds = 1.0f; // 互换位置的冷却时间
    private float nextSwapTime = 0f; // 下次可以互换的时间

    private void Awake()
    {
        if (selectionManager == null)
        {
            selectionManager = FindObjectOfType<CloneSelectionManager>();
            if (selectionManager == null)
            {
                Debug.LogWarning("[SpellInputBridge] CloneSelectionManager not found in scene.");
            }
        }
    }

    // 切换选中分身的"无敌人时跟随玩家"开关 - 供 FSM 调用
    public void ToggleFollowPlayerForSelected()
    {
        if (selectionManager != null)
        {
            var selectedClone = selectionManager.GetSelectedClone();
            if (selectedClone != null)
            {
                Debug.Log($"[SpellInputBridge] Selected clone: {selectedClone.name}");
                var cloneWalker = selectedClone.GetComponent<Clone_Walker>();
                
                if (cloneWalker != null)
                {
                    bool beforeState = cloneWalker.GetFollowPlayerWhenNoEnemy();
                    Debug.Log($"[SpellInputBridge] Before toggle: followPlayerWhenNoEnemy = {beforeState}");
                    selectionManager.ToggleFollowPlayerForSelected();
                    bool afterState = cloneWalker.GetFollowPlayerWhenNoEnemy();
                    Debug.Log($"[SpellInputBridge] After toggle: followPlayerWhenNoEnemy = {afterState}");
                }
                else
                {
                    Debug.LogError($"[SpellInputBridge] No Clone_Walker component found on {selectedClone.name}");
                }
            }
            else
            {
                Debug.LogWarning("[SpellInputBridge] No clone selected");
            }
        }
        else
        {
            Debug.LogWarning("[SpellInputBridge] No CloneSelectionManager to toggle follow flag.");
        }
    }

    // 选中分身与玩家互换位置（带冷却时间）- 供 FSM 调用
    public void SwapSelectedCloneWithHero()
    {
        // 检查冷却时间
        if (Time.time < nextSwapTime)
        {
            Debug.Log($"[SpellInputBridge] Swap on cooldown. Next available in {nextSwapTime - Time.time:F1}s");
            return;
        }
        
        if (selectionManager != null)
        {
            selectionManager.SwapSelectedCloneWithHero();
            nextSwapTime = Time.time + swapCooldownSeconds; // 设置下次可用时间
            Debug.Log($"[SpellInputBridge] Swap executed. Next available in {swapCooldownSeconds}s");
        }
        else
        {
            Debug.LogWarning("[SpellInputBridge] No CloneSelectionManager to swap positions.");
        }
    }

    // 动态设置互换冷却时间的方法，供外部调用
    public void SetSwapCooldown(float seconds)
    {
        swapCooldownSeconds = seconds;
    }

    // 检查互换是否在冷却中 - 供 FSM 查询
    public bool IsSwapOnCooldown()
    {
        return Time.time < nextSwapTime;
    }

    // 获取剩余冷却时间 - 供 FSM 查询
    public float GetSwapCooldownRemaining()
    {
        return Mathf.Max(0f, nextSwapTime - Time.time);
    }
}