using UnityEngine;
using UnityEngine.Events;

public class ChallengePoint : MonoBehaviour
{
    [Header("Settings")]
    public ChallengeManager manager;
    [Tooltip("Time limit to reach this point. If 0, uses global default.")]
    public float timeLimit = 0f;
    [Tooltip("Tag of the object that triggers this point.")]
    public string triggerTag = "Nail Attack";
    [Tooltip("If true, requires Player to be in trigger and press Up key.")]
    public bool requiresUpKey = false;

    [Header("Visuals")]
    [Tooltip("GameObject to enable when active, disable when inactive.")]
    public GameObject visualObject;
    public UnityEvent onActivate;
    public UnityEvent onDeactivate;

    private bool isActive = false;
    private bool playerInRange = false;

    private void Start()
    {
        // Auto-find manager if not assigned, assuming it's parent or nearby
        if (manager == null)
        {
            manager = GetComponentInParent<ChallengeManager>();
        }
    }

    private void Update()
    {
        if (isActive && requiresUpKey && playerInRange)
        {
            if (InputHandler.Instance != null && InputHandler.Instance.inputActions != null && InputHandler.Instance.inputActions.up.WasPressed)
            {
                if (manager != null)
                {
                    manager.OnPointHit(this);
                }
            }
        }
    }

    public void Activate()
    {
        isActive = true;
        if (visualObject != null) visualObject.SetActive(true);
        onActivate.Invoke();
    }

    public void Deactivate()
    {
        isActive = false;
        if (visualObject != null) visualObject.SetActive(false);
        onDeactivate.Invoke();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.CompareTag("HeroBox")) // Common player tags
        {
            playerInRange = true;
        }
        CheckHit(other.gameObject);
    }
    
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.CompareTag("HeroBox"))
        {
            playerInRange = false;
        }
    }
    
    private void OnCollisionEnter2D(Collision2D collision)
    {
        CheckHit(collision.gameObject);
    }

    private void CheckHit(GameObject other)
    {
        if (!isActive) return;
        if (requiresUpKey) return; // Handled in Update via Input

        // Check tag
        if (string.IsNullOrEmpty(triggerTag) || other.CompareTag(triggerTag))
        {
            if (manager != null)
            {
                manager.OnPointHit(this);
            }
        }
    }
}
