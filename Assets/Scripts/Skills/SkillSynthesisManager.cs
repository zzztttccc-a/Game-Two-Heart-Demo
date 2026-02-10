using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GlobalEnums; // Assuming ActorStates is here or similar

namespace Game.Skills
{
    [System.Serializable]
    public class SkillSlot
    {
        public SkillData currentSkill;
        public bool isCoolingDown;
        public float cooldownTimer;
        
        public bool IsEmpty => currentSkill == null;
    }

    [System.Serializable]
    public struct SkillCombination
    {
        public SkillData inputA;
        public SkillData inputB;
        public SkillData result;
    }

    public class SkillSynthesisManager : MonoBehaviour
    {
        public static SkillSynthesisManager Instance;

        [Header("Configuration")]
        public List<SkillData> skillPool; // 随机池
        public SkillData[] weirdSkillPool; // 奇葩技能池
        public List<SkillCombination> specificCombinations; // 特定组合
        public int maxOrbs = 5;
        public float refillDelay = 1.0f; // 自动补充延迟

        [Header("State")]
        public SkillSlot[] slots; // 2 slots by default
        public int currentOrbs = 3;
        
        [Header("Input Keys (Temporary)")]
        public KeyCode skillKey1 = KeyCode.Q;
        public KeyCode skillKey2 = KeyCode.E;
        public KeyCode synthesisKey = KeyCode.R; // 手动合成键

        private HeroController heroCtrl;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            heroCtrl = GetComponent<HeroController>();
            if (heroCtrl == null) heroCtrl = FindObjectOfType<HeroController>();

            // Initialize slots
            if (slots == null || slots.Length != 2)
            {
                slots = new SkillSlot[2];
                slots[0] = new SkillSlot();
                slots[1] = new SkillSlot();
            }
        }

        private void Start()
        {
            // Initial fill
            StartCoroutine(RefillSlotRoutine(0, 0.5f));
            StartCoroutine(RefillSlotRoutine(1, 0.5f));
        }

        private void Update()
        {
            HandleInput();
            UpdateCooldowns();
        }

        private void HandleInput()
        {
            // Release Skills
            if (Input.GetKeyDown(skillKey1)) UseSkill(0);
            if (Input.GetKeyDown(skillKey2)) UseSkill(1);

            // Manual Synthesis
            if (Input.GetKeyDown(synthesisKey)) TryManualSynthesis();
        }

        private void UpdateCooldowns()
        {
            foreach (var slot in slots)
            {
                if (slot.isCoolingDown)
                {
                    slot.cooldownTimer -= Time.deltaTime;
                    if (slot.cooldownTimer <= 0)
                    {
                        slot.isCoolingDown = false;
                        // Refill happens after use, usually. 
                        // But if cooldown is just for "cannot use", refill is separate.
                        // Requirement: "当前技能释放完成→下一个技能自动补充"
                        // So when used, it becomes empty. Refill starts.
                    }
                }
            }
        }

        public void UseSkill(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Length) return;
            SkillSlot slot = slots[slotIndex];

            if (slot.IsEmpty || slot.isCoolingDown) return;

            // Execute Skill Logic
            Debug.Log($"[Skill] Using Skill: {slot.currentSkill.skillName} (Level {slot.currentSkill.level})");
            
            // TODO: Instantiate Effect Prefab at Hero position
            if (heroCtrl != null && slot.currentSkill.effectPrefab != null)
            {
                Instantiate(slot.currentSkill.effectPrefab, heroCtrl.transform.position, Quaternion.identity);
            }

            // Consumption Logic
            slot.currentSkill = null; // Remove skill
            // Start refill timer
            StartCoroutine(RefillSlotRoutine(slotIndex, refillDelay));
        }

        private IEnumerator RefillSlotRoutine(int slotIndex, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (slots[slotIndex].IsEmpty)
            {
                GenerateRandomSkillForSlot(slotIndex);
            }
        }

        private void GenerateRandomSkillForSlot(int slotIndex)
        {
            if (skillPool == null || skillPool.Count == 0) return;

            // Weighted Random Selection
            SkillData newSkill = null;
            int totalWeight = 0;
            foreach (var s in skillPool) totalWeight += s.rarityWeight;

            int randomValue = Random.Range(0, totalWeight);
            int currentWeight = 0;
            foreach (var s in skillPool)
            {
                currentWeight += s.rarityWeight;
                if (randomValue < currentWeight)
                {
                    newSkill = s;
                    break;
                }
            }
            if (newSkill == null) newSkill = skillPool[0]; // Fallback
            
            // Check for Auto-Synthesis (Same Skill)
            int otherSlotIndex = (slotIndex + 1) % 2;
            SkillSlot otherSlot = slots[otherSlotIndex];

            if (!otherSlot.IsEmpty && otherSlot.currentSkill.skillID == newSkill.skillID)
            {
                // Auto Merge!
                if (otherSlot.currentSkill.upgradeResult != null)
                {
                    Debug.Log($"[Skill] Auto-Synthesis! {newSkill.skillName} merged into Slot {otherSlotIndex}");
                    otherSlot.currentSkill = otherSlot.currentSkill.upgradeResult;
                    // Current slot remains empty, try refill again?
                    // To prevent infinite loop, add delay
                    StartCoroutine(RefillSlotRoutine(slotIndex, refillDelay)); 
                }
                else
                {
                    // Max level reached or no upgrade? Just fill it.
                    slots[slotIndex].currentSkill = newSkill;
                }
            }
            else
            {
                slots[slotIndex].currentSkill = newSkill;
                Debug.Log($"[Skill] Slot {slotIndex} filled with {newSkill.skillName}");
            }
        }

        public void TryManualSynthesis()
        {
            if (currentOrbs <= 0)
            {
                Debug.Log("[Skill] Not enough Orbs!");
                return;
            }

            if (slots[0].IsEmpty || slots[1].IsEmpty)
            {
                Debug.Log("[Skill] Need 2 skills to synthesize!");
                return;
            }

            SkillData skillA = slots[0].currentSkill;
            SkillData skillB = slots[1].currentSkill;

            if (skillA.skillID == skillB.skillID)
            {
                Debug.Log("[Skill] Same skills auto-merge. Wait for refill or use one.");
                return;
            }

            // Different Skills -> Consume Orb -> Weird/Random Synthesis
            currentOrbs--;
            Debug.Log($"[Skill] Manual Synthesis! Orbs left: {currentOrbs}");

            SkillData resultSkill = null;

            // 1. Check Specific Combinations
            if (specificCombinations != null)
            {
                foreach (var combo in specificCombinations)
                {
                    if ((combo.inputA.skillID == skillA.skillID && combo.inputB.skillID == skillB.skillID) ||
                        (combo.inputA.skillID == skillB.skillID && combo.inputB.skillID == skillA.skillID))
                    {
                        resultSkill = combo.result;
                        Debug.Log($"[Skill] Combo Found! {skillA.skillName} + {skillB.skillName} -> {resultSkill.skillName}");
                        break;
                    }
                }
            }

            // 2. If no combo, try Weird or Random Upgrade
            if (resultSkill == null)
            {
                // 50% chance for a Weird skill, 50% for a high level random skill?
                if (weirdSkillPool != null && weirdSkillPool.Length > 0 && Random.value > 0.5f)
                {
                    resultSkill = weirdSkillPool[Random.Range(0, weirdSkillPool.Length)];
                }
                else
                {
                    // Just pick a random one and upgrade it?
                    // Or combine effects? (Complex, for now pick from pool)
                    resultSkill = skillPool[Random.Range(0, skillPool.Count)];
                    if(resultSkill.upgradeResult != null) resultSkill = resultSkill.upgradeResult; // Boost it
                }
            }

            // Apply Result
            slots[0].currentSkill = resultSkill;
            slots[1].currentSkill = null; // Clear second slot

            // Refill second slot
            StartCoroutine(RefillSlotRoutine(1, refillDelay));
        }

        // Helper to add orbs (e.g. gameplay reward)
        public void AddOrbs(int amount)
        {
            currentOrbs = Mathf.Min(currentOrbs + amount, maxOrbs);
        }
    }
}