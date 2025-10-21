using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CloneSelectionManager : MonoBehaviour
{
    [Header("Selection")]
    [Tooltip("Tint color applied to selected clone's SpriteRenderers")] public Color selectedTint = new Color(1f, 0.85f, 0.4f, 1f);

    [Header("Labels")]
    public bool showNumbers = true;
    public Vector3 labelOffset = new Vector3(0f, 1.0f, 0f);
    [Range(0.05f, 0.5f)] public float labelCharacterSize = 0.2f;
    public Color labelColor = Color.yellow;

    // 自动检测：定时轮询分身列表变化并自动更新（保留选中）
    [Header("Auto Detect")]
    [Tooltip("自动检测分身启用/禁用的扫描间隔（秒）")] public float autoScanInterval = 0.5f;
    private float autoScanTimer = 0f;
    private HashSet<int> lastSnapshotIds = new HashSet<int>();

    private class CloneEntry
    {
        public Transform t;
        public SpriteRenderer[] renderers;
        public Color[] originalColors;
        public GameObject labelGO;
        public TextMesh label;
        public int number;
    }

    private readonly List<CloneEntry> clones = new List<CloneEntry>();
    private int currentIndex = -1;

    private void Start()
    {
        BuildCloneList();
        NumberClones();
        TakeSnapshot();
        // 初始化时：如果存在分身，自动选择序号为1（索引0）
        if (clones.Count > 0)
        {
            currentIndex = 0;
            SetSelected(clones[currentIndex], true);
            Debug.Log($"[CloneSelect] Auto-select initial clone #{clones[currentIndex].number}: {clones[currentIndex].t.name}");
        }
        else
        {
            currentIndex = -1;
        }
        Debug.Log($"[CloneSelect] Initialized with {clones.Count} clones.");
    }

    private void Update()
    {
        // 使用 InControl 的 HeroActions 绑定：Tab -> SelectCloneNext
        var ih = InputHandler.Instance;
        if (ih != null && ih.inputActions != null && ih.inputActions.selectCloneNext != null && ih.inputActions.selectCloneNext.WasPressed)
        {
            CycleSelection();
        }
        // 自动检测：定时轮询是否有分身数量或实例变化
        autoScanTimer += Time.deltaTime;
        if (autoScanTimer >= autoScanInterval)
        {
            autoScanTimer = 0f;
            AutoRescanIfChanged();
        }
        UpdateLabelsPosition();
    }

    private void BuildCloneList()
    {
        clones.Clear();
        var allBehaviours = GameObject.FindObjectsOfType<MonoBehaviour>();
        foreach (var m in allBehaviours)
        {
            if (m == null) continue;
            string typeName = m.GetType().Name;
            // 包含两种脚本命名：Clone_walker 和 Clone_Walker
            if (typeName == "Clone_walker" || typeName == "Clone_Walker")
            {
                var t = m.transform;
                var rends = t.GetComponentsInChildren<SpriteRenderer>(true);
                var entry = new CloneEntry
                {
                    t = t,
                    renderers = rends,
                    originalColors = rends.Select(r => r != null ? r.color : Color.white).ToArray(),
                    labelGO = null,
                    label = null
                };
                clones.Add(entry);
            }
        }
        // 去除无效项
        for (int i = clones.Count - 1; i >= 0; i--)
        {
            if (clones[i] == null || clones[i].t == null)
            {
                clones.RemoveAt(i);
            }
        }
        // 保持当前索引在有效范围内
        currentIndex = Mathf.Clamp(currentIndex, -1, clones.Count - 1);
    }

    private void NumberClones()
    {
        for (int i = 0; i < clones.Count; i++)
        {
            var c = clones[i];
            c.number = i + 1;
            if (showNumbers)
            {
                if (c.labelGO == null)
                {
                    c.labelGO = new GameObject("CloneLabel_" + c.number);
                    c.labelGO.transform.SetParent(c.t, false);
                    c.labelGO.transform.localPosition = labelOffset;
                    c.label = c.labelGO.AddComponent<TextMesh>();
                    c.label.anchor = TextAnchor.MiddleCenter;
                    c.label.alignment = TextAlignment.Center;
                    c.label.color = labelColor;
                    c.label.fontSize = 64;
                    c.label.characterSize = labelCharacterSize;
                }
                c.label.text = c.number.ToString();
            }
        }
    }

    private void UpdateLabelsPosition()
    {
        if (!showNumbers) return;
        foreach (var c in clones)
        {
            if (c.labelGO != null)
            {
                c.labelGO.transform.localPosition = labelOffset;
            }
        }
    }

    // 自动检测：如果变化则重建列表并尽量保持选中项
    private void AutoRescanIfChanged()
    {
        var currentIds = GetCurrentCloneInstanceIds();
        bool changed = !currentIds.SetEquals(lastSnapshotIds);
        if (!changed) return;

        int selectedId = -1;
        if (currentIndex >= 0 && currentIndex < clones.Count && clones[currentIndex] != null && clones[currentIndex].t != null)
        {
            selectedId = clones[currentIndex].t.GetInstanceID();
        }

        // 清理原有标签
        foreach (var c in clones)
        {
            if (c.labelGO != null)
            {
                Destroy(c.labelGO);
                c.labelGO = null;
                c.label = null;
            }
        }

        BuildCloneList();
        NumberClones();
        TakeSnapshot();

        // 恢复选中项（若仍存在）
        if (selectedId != -1)
        {
            int idx = FindIndexByInstanceId(selectedId);
            if (idx >= 0)
            {
                // 取消旧选中（安全清理）
                if (currentIndex >= 0 && currentIndex < clones.Count)
                {
                    SetSelected(clones[currentIndex], false);
                }
                currentIndex = idx;
                SetSelected(clones[currentIndex], true);
                Debug.Log($"[CloneSelect] AutoRescan: keep selection #{clones[currentIndex].number}: {clones[currentIndex].t.name}");
                return;
            }
        }
        // 若无法保持原选中，则自动选择序号为1（若存在）
        if (clones.Count > 0)
        {
            // 取消旧选中（安全清理）
            if (currentIndex >= 0 && currentIndex < clones.Count)
            {
                SetSelected(clones[currentIndex], false);
            }
            currentIndex = 0;
            SetSelected(clones[currentIndex], true);
            Debug.Log($"[CloneSelect] AutoRescan: selected first clone #{clones[currentIndex].number}: {clones[currentIndex].t.name}");
        }
        else
        {
            // 无分身，清除选中
            if (currentIndex >= 0 && currentIndex < clones.Count)
            {
                SetSelected(clones[currentIndex], false);
            }
            currentIndex = -1;
            Debug.Log("[CloneSelect] AutoRescan: no clones in scene.");
        }
    }

    private HashSet<int> GetCurrentCloneInstanceIds()
    {
        var ids = new HashSet<int>();
        var allBehaviours = GameObject.FindObjectsOfType<MonoBehaviour>();
        foreach (var m in allBehaviours)
        {
            if (m == null) continue;
            string typeName = m.GetType().Name;
            if (typeName == "Clone_walker" || typeName == "Clone_Walker")
            {
                ids.Add(m.transform.GetInstanceID());
            }
        }
        return ids;
    }

    private void TakeSnapshot()
    {
        lastSnapshotIds = GetCurrentCloneInstanceIds();
    }

    private int FindIndexByInstanceId(int instanceId)
    {
        for (int i = 0; i < clones.Count; i++)
        {
            var c = clones[i];
            if (c != null && c.t != null && c.t.GetInstanceID() == instanceId)
                return i;
        }
        return -1;
    }

    private void CycleSelection()
    {
        if (clones.Count == 0) return;
        int nextIndex = (currentIndex + 1) % clones.Count;
        // 取消当前选中
        if (currentIndex >= 0 && currentIndex < clones.Count)
        {
            SetSelected(clones[currentIndex], false);
        }
        // 选中新项
        currentIndex = nextIndex;
        SetSelected(clones[currentIndex], true);
        Debug.Log($"[CloneSelect] Selected #{clones[currentIndex].number}: {clones[currentIndex].t.name}");
    }

    private void SetSelected(CloneEntry entry, bool selected)
    {
        if (entry == null || entry.renderers == null) return;
        for (int i = 0; i < entry.renderers.Length; i++)
        {
            var r = entry.renderers[i];
            if (r == null) continue;
            r.color = selected ? selectedTint : entry.originalColors[i];
        }
    }

    public Transform GetSelectedClone()
    {
        if (currentIndex < 0 || currentIndex >= clones.Count) return null;
        return clones[currentIndex].t;
    }

    // FSM桥接：设置选中分身的“无敌人时跟随玩家”开关
    public void SetFollowPlayerForSelected(bool on)
    {
        var t = GetSelectedClone();
        if (t == null)
        {
            Debug.LogWarning("[CloneSelect] No selected clone to set follow flag.");
            return;
        }
        var cw = t.GetComponent<Clone_Walker>();
        if (cw == null)
        {
            Debug.LogWarning("[CloneSelect] Selected clone has no Clone_Walker component.");
            return;
        }
        cw.SetFollowPlayerWhenNoEnemy(on);
    }

    // FSM桥接：切换选中分身的“无敌人时跟随玩家”开关
    public void ToggleFollowPlayerForSelected()
    {
        var t = GetSelectedClone();
        if (t == null)
        {
            Debug.LogWarning("[CloneSelect] No selected clone to toggle follow flag.");
            return;
        }
        var cw = t.GetComponent<Clone_Walker>();
        if (cw == null)
        {
            Debug.LogWarning("[CloneSelect] Selected clone has no Clone_Walker component.");
            return;
        }
        bool current = cw.GetFollowPlayerWhenNoEnemy();
        cw.SetFollowPlayerWhenNoEnemy(!current);
    }

    // FSM桥接：让选中分身与玩家互换位置
    public void SwapSelectedCloneWithHero()
    {
        var t = GetSelectedClone();
        if (t == null)
        {
            Debug.LogWarning("[CloneSelect] No selected clone to swap with hero.");
            return;
        }
        var hc = HeroController.instance;
        Transform heroT = hc != null ? hc.transform : GameObject.FindWithTag("Player")?.transform;
        if (heroT == null)
        {
            Debug.LogWarning("[CloneSelect] Hero not found for swap.");
            return;
        }
        Vector3 heroPos = heroT.position; // 记录玩家原位置
        Vector3 clonePos = t.position;
        
        // 在交换前，更新分身的 Home 为玩家的原位置
        var cw = t.GetComponent<Clone_Walker>();
        if (cw != null)
        {
            cw.SetHomePosition(heroPos);
        }
        else
        {
            Debug.LogWarning("[CloneSelect] Selected clone has no Clone_Walker to set home.");
        }
        
        // 执行交换
        heroT.position = clonePos;
        t.position = heroPos;
        Debug.Log("[CloneSelect] Swapped positions between hero and selected clone, and updated clone home to hero's original position.");
    }
}