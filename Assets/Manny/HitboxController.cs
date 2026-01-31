using UnityEngine;

/// <summary>
/// Controller script that receives and handles hit messages from hitboxes.
/// Currently just logs debug messages, but can be extended to handle damage, scoring, etc.
/// </summary>
public class HitboxController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool logHits = true;
    
    private void Awake()
    {
        // Ensure this is a singleton or find existing instance
        if (FindObjectsByType<HitboxController>(FindObjectsSortMode.None).Length > 1)
        {
            Debug.LogWarning("Multiple HitboxController instances found. Consider using a singleton pattern.");
        }
    }
    
    /// <summary>
    /// Called when a hit is detected. Receives information about the hit.
    /// </summary>
    /// <param name="attacker">The GameObject that performed the attack</param>
    /// <param name="victim">The GameObject that was hit</param>
    /// <param name="attackType">The type of attack (Jab, Straight, LeftHook, etc.)</param>
    /// <param name="hitPoint">The world position where the hit occurred</param>
    public void OnHitDetected(GameObject attacker, GameObject victim, string attackType, Vector3 hitPoint)
    {
        if (logHits)
        {
            Debug.Log($"[HIT] {attacker.name} hit {victim.name} with {attackType} at position {hitPoint}");
        }

        // Notify victim's EnemyControllerInput if present
        GameObject victimRoot = victim.transform.root.gameObject;
        var enemyController = victimRoot.GetComponent<EnemyControllerInput>();
        if (enemyController != null)
            enemyController.OnHitReceived(attacker, attackType, hitPoint);
        
        // TODO: Add damage calculation, scoring, effects, etc.
    }
    
    /// <summary>
    /// Static method to find and use the HitboxController instance.
    /// </summary>
    public static HitboxController GetInstance()
    {
        HitboxController instance = FindFirstObjectByType<HitboxController>();
        if (instance == null)
        {
            Debug.LogWarning("No HitboxController found in scene. Creating one.");
            GameObject go = new GameObject("HitboxController");
            instance = go.AddComponent<HitboxController>();
        }
        return instance;
    }
}
