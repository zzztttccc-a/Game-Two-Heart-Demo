using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlatformManager : MonoBehaviour
{
    public enum PlatformType
    {
        TimeBased,
        JumpBased,
        DirectionBased
    }

    [Header("Manager Settings")]
    [Tooltip("The type of logic controlling the platforms.")]
    public PlatformType type;

    [System.Serializable]
    public class PlatformEntry
    {
        public GameObject platform;
        [Tooltip("Initial active state of the platform.")]
        public bool startActive;
    }

    [Tooltip("List of platforms to control.")]
    public List<PlatformEntry> platforms = new List<PlatformEntry>();

    [Header("Time Based Settings")]
    public float enableDuration = 2.0f;
    public float disableDuration = 2.0f;
    [Tooltip("Initial delay before first time-based toggle (seconds). Default 0.")]
    public float firstDelay = 0f;

    [Header("Direction Based Settings")]
    [Tooltip("If true, platforms are active when facing right. If false, active when facing left.")]
    public bool activeOnRight = true;

    [Header("Dissolve Settings")]
    [Tooltip("Enable visual dissolve when toggling platform visibility.")]
    public bool enableDissolve = true;
    [Tooltip("Seconds to complete dissolve/undissolve.")]
    public float dissolveDuration = 0.5f;
    [Tooltip("Shader float property name controlling dissolve (e.g. _Fade or Disolve_Value).")]
    public string dissolveProperty = "_Fade";

    // Runtime helpers
    private Dictionary<GameObject, Coroutine> dissolveRoutines = new Dictionary<GameObject, Coroutine>();
    private Coroutine groupTimeRoutine;
    private bool groupAEnabled = true; // startActive=true group currently enabled
    private int dissolvePropId;

    private void Start()
    {
        dissolvePropId = Shader.PropertyToID(dissolveProperty);
        // Apply initial states
        foreach (var entry in platforms)
        {
            if (entry.platform != null)
            {
                SetPlatformActiveWithDissolve(entry.platform, entry.startActive, instant:true);
            }
        }
        groupAEnabled = true; // startActive=true group is enabled at start

        if (type == PlatformType.TimeBased)
        {
            groupTimeRoutine = StartCoroutine(GroupTimeRoutine());
        }
        else if (type == PlatformType.JumpBased)
        {
            if (HeroController.instance != null)
            {
                HeroController.instance.OnJumpEvent += OnJump;
            }
        }
    }

    private void OnDestroy()
    {
        if (HeroController.SilentInstance != null)
        {
            HeroController.SilentInstance.OnJumpEvent -= OnJump;
        }

        if (groupTimeRoutine != null)
        {
            StopCoroutine(groupTimeRoutine);
            groupTimeRoutine = null;
        }
    }

    private void Update()
    {
        if (type == PlatformType.DirectionBased)
        {
            if (HeroController.SilentInstance != null)
            {
                bool isRight = HeroController.SilentInstance.cState.facingRight;
                // Determine target state based on direction
                bool shouldBeActive = (isRight == activeOnRight);

                // Update all platforms
                foreach (var entry in platforms)
                {
                    if (entry.platform != null && entry.platform.activeSelf != shouldBeActive)
                    {
                        SetPlatformActiveWithDissolve(entry.platform, shouldBeActive);
                    }
                }
            }
        }
    }

    private void OnJump()
    {
        if (type == PlatformType.JumpBased)
        {
            TogglePlatforms();
        }
    }

    private IEnumerator GroupTimeRoutine()
    {
        bool first = true;
        while (true)
        {
            float wait = groupAEnabled ? enableDuration : disableDuration;
            if (first)
            {
                float initialWait = Mathf.Max(0f, wait + firstDelay);
                yield return new WaitForSeconds(initialWait);
                first = false;
            }
            else
            {
                yield return new WaitForSeconds(wait);
            }
            groupAEnabled = !groupAEnabled;
            ApplyGroupState(groupAEnabled);
        }
    }

    private void TogglePlatforms()
    {
        groupAEnabled = !groupAEnabled;
        ApplyGroupState(groupAEnabled);
    }

    private void ApplyGroupState(bool groupAOn)
    {
        foreach (var entry in platforms)
        {
            if (entry.platform == null) continue;
            bool target = entry.startActive ? groupAOn : !groupAOn;
            SetPlatformActiveWithDissolve(entry.platform, target);
        }
    }

    private void SetPlatformActiveWithDissolve(GameObject platform, bool active, bool instant = false)
    {
        if (platform == null)
            return;

        // Cancel any running dissolve on this platform
        if (dissolveRoutines.TryGetValue(platform, out var co) && co != null)
        {
            StopCoroutine(co);
            dissolveRoutines[platform] = null;
        }

        if (!enableDissolve || instant)
        {
            platform.SetActive(active);
            SetCollidersEnabled(platform, active);
            return;
        }

        if (active)
        {
            if (!platform.activeSelf)
            {
                platform.SetActive(true);
            }
            SetCollidersEnabled(platform, false);
            var coUndissolve = StartCoroutine(UndissolveRoutine(platform));
            dissolveRoutines[platform] = coUndissolve;
        }
        else
        {
            SetCollidersEnabled(platform, false);
            var coDissolve = StartCoroutine(DissolveThenDisableRoutine(platform));
            dissolveRoutines[platform] = coDissolve;
        }
    }

    private IEnumerator DissolveThenDisableRoutine(GameObject platform)
    {
        var renderers = platform.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            // No visual to dissolve, disable immediately
            platform.SetActive(false);
            yield break;
        }

        float startVal = 1f;
        // Try reading current value from first renderer's material
        var mat = renderers[0].material;
        if (mat != null && mat.HasProperty(dissolvePropId))
        {
            startVal = mat.GetFloat(dissolvePropId);
        }

        float t = 0f;
        while (t < dissolveDuration)
        {
            float v = Mathf.Lerp(startVal, 0f, t / dissolveDuration);
            foreach (var r in renderers)
            {
                ApplyDissolveValue(r, v);
            }
            t += Time.deltaTime;
            yield return null;
        }
        foreach (var r in renderers)
        {
            ApplyDissolveValue(r, 0f);
        }
        platform.SetActive(false);
    }

    private IEnumerator UndissolveRoutine(GameObject platform)
    {
        var renderers = platform.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            yield break;
        }

        float startVal = 0f;
        var mat = renderers[0].material;
        if (mat != null && mat.HasProperty(dissolvePropId))
        {
            startVal = mat.GetFloat(dissolvePropId);
        }

        float t = 0f;
        while (t < dissolveDuration)
        {
            float v = Mathf.Lerp(startVal, 1f, t / dissolveDuration);
            foreach (var r in renderers)
            {
                ApplyDissolveValue(r, v);
            }
            t += Time.deltaTime;
            yield return null;
        }
        foreach (var r in renderers)
        {
            ApplyDissolveValue(r, 1f);
        }
        SetCollidersEnabled(platform, true);
    }

    private void ApplyDissolveValue(Renderer renderer, float value)
    {
        if (renderer == null) return;
        // Prefer MaterialPropertyBlock to avoid duplicating materials
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetFloat(dissolvePropId, value);
        renderer.SetPropertyBlock(block);

        // Fallback in case shader doesn't support property blocks
        var mat = renderer.material;
        if (mat != null && mat.HasProperty(dissolvePropId))
        {
            mat.SetFloat(dissolvePropId, value);
        }
    }

    private void SetCollidersEnabled(GameObject platform, bool enabled)
    {
        var cols2d = platform.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols2d.Length; i++)
        {
            cols2d[i].enabled = enabled;
        }
        var cols3d = platform.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols3d.Length; i++)
        {
            cols3d[i].enabled = enabled;
        }
    }
}
