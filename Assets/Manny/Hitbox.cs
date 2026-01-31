using UnityEngine;

/// <summary>
/// Component attached to fist/hand objects to detect collisions with opponent.
/// Should be attached to a GameObject with a Collider (set as Trigger) on the fist/hand.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Hitbox : MonoBehaviour
{
    [Header("Hitbox Settings")]
    [Tooltip("The owner of this hitbox (Player or Enemy)")]
    [SerializeField] private GameObject owner;
    
    [Tooltip("The type of attack this hitbox represents")]
    [SerializeField] private string attackType = "Jab";
    
    [Tooltip("Layer mask for valid targets (should include opponent layer)")]
    [SerializeField] private LayerMask targetLayers = -1;
    
    [Tooltip("Cooldown between hits to prevent multiple hits from same attack")]
    [SerializeField] private float hitCooldown = 0.5f;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private Color gizmoColor = Color.red;
    
    private Collider hitboxCollider;
    private float lastHitTime;
    private bool isActive;
    
    // Tags to identify player and enemy
    private const string PLAYER_TAG = "Player";
    private const string ENEMY_TAG = "Enemy";
    
    private void Awake()
    {
        hitboxCollider = GetComponent<Collider>();
        
        // Ensure collider is a trigger
        if (hitboxCollider != null)
        {
            hitboxCollider.isTrigger = true;
        }
        
        // Auto-detect owner if not set
        if (owner == null)
        {
            owner = transform.root.gameObject;
        }
    }
    
    private void Start()
    {
        // Ensure owner is set
        if (owner == null)
        {
            Debug.LogWarning($"Hitbox on {gameObject.name} has no owner assigned!");
        }
    }
    
    /// <summary>
    /// Enable the hitbox (call when attack starts)
    /// </summary>
    public void Activate()
    {
        isActive = true;
        lastHitTime = 0f;
    }
    
    /// <summary>
    /// Disable the hitbox (call when attack ends)
    /// </summary>
    public void Deactivate()
    {
        isActive = false;
    }
    
    /// <summary>
    /// Set the attack type for this hitbox
    /// </summary>
    public void SetAttackType(string type)
    {
        attackType = type;
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!isActive)
            return;
        
        // Check cooldown
        if (Time.time < lastHitTime + hitCooldown)
            return;
        
        // Check if we hit a valid target
        GameObject target = other.gameObject;
        
        // Skip if hitting ourselves or our owner
        if (target == owner || target.transform.IsChildOf(owner.transform))
            return;
        
        // Check if target is on a valid layer
        if (targetLayers != -1 && (targetLayers & (1 << target.layer)) == 0)
            return;
        
        // Check if target is the opponent (has PlayerController or EnemyController)
        bool isOpponent = false;
        string ownerTag = owner.tag;
        string targetTag = target.tag;
        
        // Check by tag
        if ((ownerTag == PLAYER_TAG && targetTag == ENEMY_TAG) ||
            (ownerTag == ENEMY_TAG && targetTag == PLAYER_TAG))
        {
            isOpponent = true;
        }
        // Check by component (fallback)
        else if (owner.GetComponent<PlayerController>() != null && target.GetComponent<EnemyController>() != null)
        {
            isOpponent = true;
        }
        else if (owner.GetComponent<EnemyController>() != null && target.GetComponent<PlayerController>() != null)
        {
            isOpponent = true;
        }
        // Check if target is part of opponent's body
        else
        {
            Transform targetRoot = target.transform.root;
            if (targetRoot != owner.transform.root)
            {
                if ((owner.GetComponent<PlayerController>() != null && targetRoot.GetComponent<EnemyController>() != null) ||
                    (owner.GetComponent<EnemyController>() != null && targetRoot.GetComponent<PlayerController>() != null))
                {
                    isOpponent = true;
                    target = targetRoot.gameObject;
                }
            }
        }
        
        if (!isOpponent)
            return;
        
        // Register the hit
        RegisterHit(target);
    }
    
    private void RegisterHit(GameObject victim)
    {
        // Update cooldown
        lastHitTime = Time.time;
        
        // Get hit point (approximate center of collision)
        Vector3 hitPoint = transform.position;
        
        // Try to get more accurate hit point from collider bounds
        if (hitboxCollider != null)
        {
            hitPoint = hitboxCollider.bounds.center;
        }
        
        // Send message to HitboxController
        HitboxController controller = HitboxController.GetInstance();
        if (controller != null)
        {
            controller.OnHitDetected(owner, victim, attackType, hitPoint);
        }
        else
        {
            Debug.LogWarning($"Hit detected but no HitboxController found! {owner.name} hit {victim.name} with {attackType}");
        }
        
        // Deactivate after hit to prevent multiple hits
        Deactivate();
    }
    
    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || hitboxCollider == null)
            return;
        
        Gizmos.color = isActive ? gizmoColor : Color.gray;
        
        // Draw wireframe of collider
        if (hitboxCollider is BoxCollider boxCollider)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(boxCollider.center, boxCollider.size);
        }
        else if (hitboxCollider is SphereCollider sphereCollider)
        {
            Gizmos.DrawWireSphere(transform.position + sphereCollider.center, sphereCollider.radius);
        }
        else if (hitboxCollider is CapsuleCollider capsuleCollider)
        {
            // Draw capsule approximation
            Vector3 center = transform.position + capsuleCollider.center;
            Gizmos.DrawWireSphere(center, capsuleCollider.radius);
        }
    }
}
