using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Runtime setup for SummonedClone:
/// - Switches AlertRange/LineOfSightDetector to detect enemies instead of Hero
/// - Converts all child DamageHero attack colliders to DamageEnemies so the clone attacks enemies
/// - Receives enemy DamageHero hazards and applies damage to the clone's HealthManager (hero-like receive)
/// Attach this to the SummonedClone root.
/// </summary>
[DisallowMultipleComponent]
public class SummonedCloneSetup : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("When enabled, AlertRange/LineOfSightDetector under this object will target Enemies instead of Player.")]
    [SerializeField] private bool switchTargetToEnemies = true;

    [Header("Attacking")]
    [Tooltip("When enabled, convert DamageHero components on child attack colliders to DamageEnemies.")]
    [SerializeField] private bool switchAttacksToEnemies = true;

    [Header("Receiving Damage")]
    [Tooltip("When enabled, treat enemy DamageHero hazards as HitInstance and apply to this clone's HealthManager.")]
    [SerializeField] private bool receiveEnemiesDamage = true;

    [Header("Hurtbox (auto)")]
    [Tooltip("If no BoxCollider2D is found on root, an isTrigger hurtbox will be auto-created.")]
    [SerializeField] private Vector2 autoHurtboxSize = new Vector2(1f, 1.2f);
    [SerializeField] private Vector2 autoHurtboxOffset = new Vector2(0f, 0.2f);

    private HealthManager hm;
    private BoxCollider2D bodyBox;
    private readonly List<Collider2D> overlapResults = new List<Collider2D>(32);

    private void Awake()
    {
        hm = GetComponent<HealthManager>();
        bodyBox = GetComponent<BoxCollider2D>();
        if (bodyBox == null)
        {
            bodyBox = gameObject.AddComponent<BoxCollider2D>();
            bodyBox.isTrigger = true;
            bodyBox.size = autoHurtboxSize;
            bodyBox.offset = autoHurtboxOffset;
        }

        if (switchTargetToEnemies)
        {
            // Flip detection to enemies
            foreach (var alert in GetComponentsInChildren<AlertRange>(includeInactive: true))
            {
                TrySetAlertDetectEnemies(alert, true);
            }
            var los = GetComponent<LineOfSightDetector>();
            if (los) { TrySetLoSDetectEnemies(los, true); }
        }

        if (switchAttacksToEnemies)
        {
            // Convert DamageHero to DamageEnemies on child attack colliders
            var damageHeroes = GetComponentsInChildren<DamageHero>(includeInactive: true);
            foreach (var dh in damageHeroes)
            {
                // Skip if the DamageHero is on this root (contact damage). Disable it.
                if (dh.gameObject == gameObject)
                {
                    dh.damageDealt = 0; // neutralize contact damage vs hero
                    dh.enabled = false;
                    continue;
                }

                var col = dh.GetComponent<Collider2D>();
                if (col == null)
                {
                    // Only meaningful on attack trigger colliders
                    dh.enabled = false;
                    continue;
                }

                // Add or configure DamageEnemies on the same object
                var de = dh.GetComponent<DamageEnemies>();
                if (de == null) de = dh.gameObject.AddComponent<DamageEnemies>();
                de.attackType = AttackTypes.Nail;
                de.circleDirection = true;
                de.damageDealt = dh.damageDealt;
                de.direction = 0f;
                de.ignoreInvuln = true;
                de.magnitudeMult = 1f;
                de.moveDirection = false;
                de.specialType = SpecialTypes.None;

                // Disable DamageHero to avoid hurting the player.
                dh.damageDealt = 0;
                dh.enabled = false;
            }
        }
    }

    private void FixedUpdate()
    {
        if (!receiveEnemiesDamage) return;
        if (hm == null || bodyBox == null || !bodyBox.isActiveAndEnabled) return;

        // Build a filter to include triggers from all layers. We'll manually check component types.
        var filter = new ContactFilter2D();
        filter.useLayerMask = false;
        filter.useTriggers = true;

        overlapResults.Clear();
        bodyBox.OverlapCollider(filter, overlapResults);

        for (int i = 0; i < overlapResults.Count; i++)
        {
            var other = overlapResults[i];
            if (other == null || !other.isActiveAndEnabled) continue;

            // Ignore self colliders
            if (other.transform.IsChildOf(transform)) continue;

            var dh = other.GetComponent<DamageHero>();
            if (dh == null) continue;
            if (dh.damageDealt <= 0) continue;
            if (other.CompareTag("Geo")) continue;

            // Convert the hazard to a HitInstance and apply to this clone's health
            var hit = new HitInstance
            {
                Source = dh.gameObject,
                AttackType = AttackTypes.Generic,
                CircleDirection = true,
                DamageDealt = dh.damageDealt,
                Direction = 0f,
                IgnoreInvulnerable = false,
                MagnitudeMultiplier = 1f,
                MoveAngle = 0f,
                MoveDirection = false,
                Multiplier = 1f,
                SpecialType = SpecialTypes.None,
                IsExtraDamage = false
            };

            // Use HitTaker to keep consistency with other damage sources (sends TAKE DAMAGE, etc.)
            HitTaker.Hit(gameObject, hit, 3);
        }
    }

    // Prefer public setters if available, otherwise fall back to reflection
    private static void TrySetAlertDetectEnemies(AlertRange alert, bool value)
    {
        if (alert == null) return;
        // public method path
        var m = alert.GetType().GetMethod("SetDetectEnemies", BindingFlags.Public | BindingFlags.Instance);
        if (m != null)
        {
            m.Invoke(alert, new object[] { value });
            return;
        }
        // field path via reflection
        var f = alert.GetType().GetField("detectEnemies", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(alert, value);
    }

    private static void TrySetLoSDetectEnemies(LineOfSightDetector los, bool value)
    {
        if (los == null) return;
        var m = los.GetType().GetMethod("SetDetectEnemies", BindingFlags.Public | BindingFlags.Instance);
        if (m != null)
        {
            m.Invoke(los, new object[] { value });
            return;
        }
        var f = los.GetType().GetField("detectEnemies", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(los, value);
    }
}