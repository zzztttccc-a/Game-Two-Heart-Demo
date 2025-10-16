//#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 便捷工具：一键在项目下创建默认的 Stasis Aura 预制体。
/// 菜单：Tools/Create Stasis Aura Prefab
/// </summary>
public static class StasisAuraPrefabCreator
{
    [MenuItem("Tools/Create Stasis Aura Prefab", priority = 1000)]
    public static void CreatePrefab()
    {
        // 创建临时对象并添加组件
        var go = new GameObject("Stasis Aura");
        var aura = go.AddComponent<StasisAura>();
        aura.radius = 8f;
        aura.duration = 2f;
        // 禁用自动触发，改为通过 FSM 手动调用 TriggerStasis()
        aura.autoTriggerOnEnable = false;
        // 不设置回收/销毁相关选项，生命周期由外部 FSM/脚本控制

        // 预制体保存路径
        const string folder = "Assets/Prefabs/Effects";
        const string path = folder + "/StasisAura.prefab";

        // 确保文件夹存在
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Effects");
        }

        // 创建预制体
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        if (prefab != null)
        {
            Debug.Log($"Created prefab at: {path}");
            Selection.activeObject = prefab;
        }
        else
        {
            Debug.LogError("Failed to create Stasis Aura prefab.");
        }
    }
}
//#endif