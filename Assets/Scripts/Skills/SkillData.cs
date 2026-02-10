using UnityEngine;

namespace Game.Skills
{
    public enum SkillType
    {
        Attack,     // 普通攻击
        Buff,       // 强化技能
        Utility,    // 特殊/功能技能
        Weird       // 奇葩/实验性技能
    }

    [CreateAssetMenu(fileName = "NewSkill", menuName = "Skills/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [Header("Basic Info")]
        public string skillID;
        public string skillName;
        public Sprite icon;
        public SkillType type;
        public int level = 1; // 1=基础, 2=强化, 3=终极

        [Header("Effect Settings")]
        public float damageMultiplier = 1f;
        public float cooldown = 0f;
        public GameObject effectPrefab; // 技能特效/弹幕/逻辑预制体

        [Header("Synthesis Rules")]
        // 如果是相同技能合成，升级为这个技能
        public SkillData upgradeResult; 
        
        // 稀有度/权重 (用于随机生成)
        [Range(0, 100)]
        public int rarityWeight = 50;

        [Header("Description")]
        [TextArea(3, 5)] 
        public string description;

        // 用于运行时逻辑的额外参数
        public float duration;
        public float range;
    }
}