using System;
using UnityEngine;
using HutongGames.PlayMaker;

[RequireComponent(typeof(Collider2D))]
public class AlertRange : MonoBehaviour
{
    // 独立范围标志：分别记录玩家/敌人是否在范围内
    private bool isPlayerInRange;
    private bool isEnemyInRange;
    private Collider2D[] colliders;

    // Target 选项：支持同时检测玩家与敌人
    [Header("Target")]
    [SerializeField] private bool detectPlayers = true; // 默认检测玩家
    [SerializeField] private bool detectEnemies = false; // 可选：检测敌人

    // PlayMaker Hook
    [Header("PlayMaker Hook")]
    [SerializeField] private string targetFsmName = "ZombieSheildControl";
    [SerializeField] private string enemyVariableName = "Enemy";
    [SerializeField] private bool assignEnemyOnEnter = true;
    [SerializeField] private bool clearEnemyOnExit = false;
    [SerializeField] private bool findHealthManagerUpwards = true;
    private PlayMakerFSM cachedTargetFsm;

    // 兼容旧接口：保留 IsHeroInRange 名称
    // - 仅检测玩家时返回 isPlayerInRange
    // - 仅检测敌人时返回 isEnemyInRange
    // - 同时检测时返回任一目标在范围内
    public bool IsHeroInRange
    {
        get
        {
            if (detectPlayers && !detectEnemies) return isPlayerInRange;
            if (detectEnemies && !detectPlayers) return isEnemyInRange;
            return (detectPlayers && isPlayerInRange) || (detectEnemies && isEnemyInRange);
        }
    }

    // 新增清晰的属性
    public bool IsPlayerInRange => isPlayerInRange;
    public bool IsEnemyInRange => isEnemyInRange;
    public bool IsAnyTargetInRange => (detectPlayers && isPlayerInRange) || (detectEnemies && isEnemyInRange);

    protected void Awake()
    {
        colliders = GetComponents<Collider2D>();
        // Cache FSM on root for fast assignment
        cachedTargetFsm = FSMUtility.LocateFSM(transform.root.gameObject, targetFsmName);
    }

    protected void OnTriggerEnter2D(Collider2D collision)
    {
        int layer = collision.gameObject.layer;
        // 玩家进入
        if (detectPlayers && IsLayer(layer, "Player"))
        {
            isPlayerInRange = true;
        }
        // 敌人进入
        if (detectEnemies && IsLayer(layer, "Enemies"))
        {
            isEnemyInRange = true;
            if (assignEnemyOnEnter)
            {
                AssignEnemyToFsm(GetTargetEnemyObject(collision));
            }
        }
    }

    protected void OnTriggerExit2D(Collider2D collision)
    {
        int layer = collision.gameObject.layer;
        // 玩家退出
        if (detectPlayers && IsLayer(layer, "Player"))
        {
            if (colliders.Length <= 1 || !StillInCollidersForMask(LayerMask.GetMask("Player")))
            {
                isPlayerInRange = false;
            }
        }
        // 敌人退出
        if (detectEnemies && IsLayer(layer, "Enemies"))
        {
            if (colliders.Length <= 1 || !StillInCollidersForMask(LayerMask.GetMask("Enemies")))
            {
                isEnemyInRange = false;
                if (clearEnemyOnExit)
                {
                    AssignEnemyToFsm(null);
                }
            }
        }
    }

    private bool StillInCollidersForMask(int targetMask)
    {
        bool flag = false;
        foreach (Collider2D collider2D in colliders)
        {
            if (collider2D is CircleCollider2D)
            {
                CircleCollider2D circleCollider2D = (CircleCollider2D)collider2D;
                flag = Physics2D.OverlapCircle(transform.TransformPoint(circleCollider2D.offset), circleCollider2D.radius * Mathf.Max(transform.localScale.x, transform.localScale.y), targetMask) != null;
            }
            else if (collider2D is BoxCollider2D)
            {
                BoxCollider2D boxCollider2D = (BoxCollider2D)collider2D;
                flag = Physics2D.OverlapBox(transform.TransformPoint(boxCollider2D.offset), new Vector2(boxCollider2D.size.x * transform.localScale.x, boxCollider2D.size.y * transform.localScale.y), transform.eulerAngles.z, targetMask) != null;
            }
            if (flag)
            {
                break;
            }
        }
        return flag;
    }

    private bool IsLayer(int layer, string layerName)
    {
        return (LayerMask.GetMask(layerName) & (1 << layer)) != 0;
    }

    private GameObject GetTargetEnemyObject(Collider2D collision)
    {
        GameObject go = collision.gameObject;
        if (findHealthManagerUpwards)
        {
            Transform t = go.transform;
            while (t != null)
            {
                if (t.GetComponent<HealthManager>() != null)
                {
                    return t.gameObject;
                }
                t = t.parent;
            }
        }
        if (collision.transform.parent != null) return collision.transform.parent.gameObject;
        return collision.transform.root != null ? collision.transform.root.gameObject : go;
    }

    private void AssignEnemyToFsm(GameObject enemyGo)
    {
        if (cachedTargetFsm == null)
        {
            cachedTargetFsm = FSMUtility.LocateFSM(transform.root.gameObject, targetFsmName);
            if (cachedTargetFsm == null) return;
        }
        var varGo = cachedTargetFsm.FsmVariables.GetFsmGameObject(enemyVariableName);
        if (varGo != null)
        {
            varGo.Value = enemyGo;
        }
    }

    public static AlertRange Find(GameObject root,string childName)
    {
        if (root == null)
            return null;
        bool flag = !string.IsNullOrEmpty(childName);
        Transform transform = root.transform;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            AlertRange component = child.GetComponent<AlertRange>();
            if(component != null && (!flag || !(child.gameObject.name != childName)))
            {
                return component;
            }
        }
        return null;
    }

    public void SetDetectEnemies(bool value)
    {
        detectEnemies = value;
    }

    public void SetDetectPlayers(bool value)
    {
        detectPlayers = value;
    }
}
