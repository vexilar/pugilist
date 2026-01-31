using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class EnemyController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform playerTarget;
    
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float deceleration = 10f;
    
    [Header("AI Settings")]
    [SerializeField] private float punchRange = 2f;
    [SerializeField] private float punchCooldown = 2.0f; // Increased to allow time for movement and defense
    [SerializeField] private float dodgeChance = 0.7f; // Increased for better defense
    [SerializeField] private float reactionTime = 0.15f; // Faster reactions
    
    [Header("Debug/Test")]
    [SerializeField] private bool testMode = false; // Simple test: jab -> lean -> jab -> lean
    
    [Header("Attack Weights")]
    [Tooltip("Relative weights for attack selection (Jab, Straight, LeftHook)")]
    [SerializeField] private float jabWeight = 1f;
    [SerializeField] private float straightWeight = 0.8f;
    [SerializeField] private float leftHookWeight = 0.6f;
    
    [Header("Hitboxes")]
    [Tooltip("Hitboxes for attacks. Will be auto-found if not assigned.")]
    [SerializeField] private Hitbox[] hitboxes;
    
    private Rigidbody rb;
    private Animator animator;
    private float moveInput;
    private PlayerController playerController;
    
    // AI state
    private float lastPunchTime;
    private float attackStartTime; // When current attack started
    private bool isReactingToPlayerAttack;
    private string detectedPlayerAttack;
    private string lastDetectedAttack; // Track to avoid reacting to same attack multiple times
    private Coroutine defensiveResetCoroutine;
    private Coroutine reactionCoroutine;
    private float idleTimer; // Tracks how long enemy has been idle (not punching/defending)
    
    // Attack durations (in seconds)
    // Longest attack: 32 frames at 2.5x speed = 32/60/2.5 = ~0.213s
    // Other attacks: 24 frames at 2x speed = 24/60/2 = 0.2s
    private const float LONG_ATTACK_DURATION = 0.22f; // 32 frames @ 2.5x with margin
    private const float SHORT_ATTACK_DURATION = 0.21f; // 24 frames @ 2x with margin
    private const float ATTACK_CANCEL_TRANSITION = 0.1f; // Transition time if canceled early
    private Coroutine testModeCoroutine;
    
    // Available attacks (excluding RightHook and Uppercut as per requirements)
    private readonly string[] availableAttacks = { "JabPressed", "StraightPressed", "LeftHookPressed" };
    private readonly float[] attackWeights = new float[3];
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        
        // Initialize attack weights
        attackWeights[0] = jabWeight;
        attackWeights[1] = straightWeight;
        attackWeights[2] = leftHookWeight;
        
        // Find hitboxes if not assigned
        if (hitboxes == null || hitboxes.Length == 0)
        {
            hitboxes = GetComponentsInChildren<Hitbox>();
        }
        
        // Ensure all hitboxes are deactivated initially
        foreach (var hitbox in hitboxes)
        {
            if (hitbox != null)
                hitbox.Deactivate();
        }
        
        // Rotate enemy 180 degrees around Y to face negative Z (toward player)
        transform.rotation = Quaternion.Euler(0, 180f, 0);
    }
    
    private void Start()
    {
        // Find player if not assigned
        if (playerTarget == null)
        {
            PlayerController player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                playerTarget = player.transform;
                playerController = player;
                Debug.Log($"[Enemy] Found player: {player.name}, IsAttacking: {player.IsAttacking}, CurrentAttackType: '{player.CurrentAttackType}'");
            }
            else
            {
                Debug.LogError("[Enemy] Could not find PlayerController in scene!");
            }
        }
        else
        {
            playerController = playerTarget.GetComponent<PlayerController>();
            if (playerController == null)
            {
                Debug.LogError($"[Enemy] playerTarget {playerTarget.name} does not have PlayerController component!");
            }
            else
            {
                Debug.Log($"[Enemy] Using assigned player: {playerTarget.name}");
            }
        }
        
        // Start test mode if enabled
        Debug.Log($"[Enemy] Start() - testMode value: {testMode}");
        if (testMode)
        {
            Debug.Log("[Enemy] TEST MODE ENABLED - Starting simple jab/lean cycle");
            testModeCoroutine = StartCoroutine(TestModeCycle());
            if (testModeCoroutine == null)
            {
                Debug.LogError("[Enemy] Failed to start TestModeCycle coroutine!");
            }
            else
            {
                Debug.Log("[Enemy] TestModeCycle coroutine started successfully");
            }
        }
        else
        {
            Debug.Log("[Enemy] Test mode is DISABLED - normal AI will run");
        }
    }
    
    private void Update()
    {
        if (playerTarget == null || animator == null)
        {
            Debug.LogWarning("[Enemy] Missing playerTarget or animator!");
            return;
        }
        
        // Skip normal AI if in test mode
        if (testMode)
            return;
        
        // Check if player is attacking
        CheckPlayerAttack();
        
        // Calculate distance to player
        float distanceToPlayer = Vector3.Distance(transform.position, playerTarget.position);
        
        // Get animator states
        bool isPunching = animator.GetBool("IsPunching");
        bool isDefending = animator.GetBool("IsDefending");
        float timeSinceLastPunch = Time.time - lastPunchTime;
        bool cooldownReady = Time.time >= lastPunchTime + punchCooldown;
        
        // Update idle timer - tracks how long enemy has been idle (not punching/defending)
        if (!isPunching && !isDefending)
        {
            idleTimer += Time.deltaTime;
        }
        else
        {
            idleTimer = 0f; // Reset when not idle
        }
        
        // Disable movement while punching or defending
        if (isPunching || isDefending)
        {
            moveInput = 0f;
            // Don't return early - still check for attacks and defensive reactions
        }
        
        // Check if current attack has been held long enough
        bool attackHeldLongEnough = true;
        if (isPunching && attackStartTime > 0f)
        {
            float attackDuration = Time.time - attackStartTime;
            // Determine which attack duration to use (Straight is longer)
            float requiredDuration = SHORT_ATTACK_DURATION;
            // Could check current animator state, but for now use shorter for safety
            attackHeldLongEnough = attackDuration >= requiredDuration;
        }
        
        // AI Decision Making
        // Priority: Defensive Reaction > Attack > Move
        // Only attack if not currently punching, defending, or reacting to player attack
        // And if we're punching, make sure we've held it long enough
        bool canAttack = distanceToPlayer <= punchRange && 
                        cooldownReady && 
                        !isDefending && 
                        (!isPunching || attackHeldLongEnough) && // Can attack if not punching, or if held long enough
                        !isReactingToPlayerAttack && // Don't attack while reacting to player
                        idleTimer > 0.05f; // Small margin to ensure we're truly idle
        
        if (canAttack)
        {
            // Attack if in range and cooldown is ready
            AttemptAttack();
        }
        
        // Movement logic (only if not punching or defending)
        if (!isPunching && !isDefending)
        {
            // Move toward or away from player
            float zDistance = playerTarget.position.z - transform.position.z;
            
            // Move toward player if too far, or maintain distance
            // Enemy faces negative z, so positive z movement moves toward player
            if (distanceToPlayer > punchRange)
            {
                // Move toward player (positive z direction to get closer)
                moveInput = Mathf.Clamp(zDistance, -1f, 1f);
            }
            else if (distanceToPlayer < punchRange * 0.8f)
            {
                // Step back slightly if too close (negative z direction to move away)
                moveInput = Mathf.Clamp(-zDistance, -1f, 1f) * 0.5f;
            }
            else
            {
                // In optimal range - stay in place but ready to attack
                moveInput = 0f;
            }
        }
    }
    
    private void CheckPlayerAttack()
    {
        if (playerController == null)
        {
            if (Time.frameCount % 300 == 0) // Log every 5 seconds if null
            {
                Debug.LogWarning("[Enemy] playerController is null! Cannot detect player attacks.");
            }
            return;
        }
        
        bool playerIsAttacking = playerController.IsAttacking;
        string currentAttackType = playerController.CurrentAttackType;
        
        // Use PlayerController's public properties for real-time detection
        if (playerIsAttacking && !string.IsNullOrEmpty(currentAttackType))
        {
            // Only react if this is a new attack (different from last detected)
            if (currentAttackType != lastDetectedAttack)
            {
                Debug.Log($"[Enemy] *** DETECTED PLAYER ATTACK: {currentAttackType} ***");
                isReactingToPlayerAttack = true;
                detectedPlayerAttack = currentAttackType;
                lastDetectedAttack = currentAttackType;
                
                // Start reaction immediately (with optional delay for realism)
                if (reactionCoroutine != null)
                {
                    StopCoroutine(reactionCoroutine);
                }
                reactionCoroutine = StartCoroutine(ReactToPlayerAttack());
            }
        }
        else if (!playerIsAttacking && isReactingToPlayerAttack)
        {
            // Player stopped attacking - but DON'T reset immediately
            // Let the reaction coroutine complete its defensive action
            // The ResetReactionState will be called by the coroutine itself
            // Don't reset lastDetectedAttack here - let the coroutine handle cleanup
        }
        else if (!playerIsAttacking && !isReactingToPlayerAttack)
        {
            // Player is not attacking and we're not reacting - clear last detected
            lastDetectedAttack = null;
        }
    }
    
    private IEnumerator ReactToPlayerAttack()
    {
        string attackToReactTo = detectedPlayerAttack; // Store the attack type we're reacting to
        Debug.Log($"[Enemy] ReactToPlayerAttack STARTED for {attackToReactTo}");
        
        // Wait for reaction time (for realism)
        yield return new WaitForSeconds(reactionTime);
        
        Debug.Log($"[Enemy] Reaction time elapsed, checking dodge chance ({dodgeChance})");
        
        // Check if we should dodge based on dodge chance
        float dodgeRoll = Random.value;
        if (dodgeRoll > dodgeChance)
        {
            Debug.Log($"[Enemy] DODGE FAILED (rolled {dodgeRoll:F2} > {dodgeChance}) - enemy will take the hit");
            // Reset reaction state even on failed dodge
            isReactingToPlayerAttack = false;
            detectedPlayerAttack = null;
            reactionCoroutine = null;
            yield break;
        }
        
        Debug.Log($"[Enemy] DODGE SUCCESS (rolled {dodgeRoll:F2} <= {dodgeChance}) - performing defensive action");
        
        // Perform specific defensive action based on attack type (per combat.MD)
        // Use the stored attack type in case detectedPlayerAttack was cleared
        if (attackToReactTo == "Jab")
        {
            PerformDefensiveAction("SlipLeftPressed");
        }
        else if (attackToReactTo == "Straight")
        {
            PerformDefensiveAction("SlipRightPressed");
        }
        else if (attackToReactTo == "LeftHook" || attackToReactTo == "RightHook")
        {
            PerformDefensiveAction("LeanPressed");
        }
        
        // Wait a bit before resetting reaction state to allow defensive action to complete
        yield return new WaitForSeconds(0.3f);
        
        // Reset reaction state after defensive action completes
        isReactingToPlayerAttack = false;
        detectedPlayerAttack = null;
        lastDetectedAttack = null;
        reactionCoroutine = null;
    }
    
    // This method is no longer used - ReactToPlayerAttack handles its own cleanup
    // Keeping it for now in case we need it later
    private IEnumerator ResetReactionState()
    {
        Debug.Log("[Enemy] ResetReactionState called (should not happen during normal flow)");
        // Wait a bit to ensure player attack has fully ended
        yield return new WaitForSeconds(0.2f);
        isReactingToPlayerAttack = false;
        detectedPlayerAttack = null;
        lastDetectedAttack = null;
        reactionCoroutine = null;
    }
    
    private void PerformDefensiveAction(string triggerName)
    {
        if (animator == null)
            return;
        
        // If enemy is currently punching, cancel the attack first
        // This allows defensive actions to interrupt attacks (realistic behavior)
        if (animator.GetBool("IsPunching"))
        {
            Debug.Log($"[Enemy] Canceling attack to perform defensive action: {triggerName}");
            CancelCurrentAttack();
        }
        
        // Stop any existing defensive reset coroutine
        if (defensiveResetCoroutine != null)
        {
            StopCoroutine(defensiveResetCoroutine);
        }
        
        // Set defensive state
        animator.SetBool("IsDefending", true);
        
        // Set the appropriate defensive trigger
        animator.SetTrigger(triggerName);
        
        Debug.Log($"[Enemy] *** DEFENSIVE ACTION: {triggerName} ***");
        
        // Reset idle timer since we're now defending
        idleTimer = 0f;
        
        // Start coroutine to reset IsDefending after exit time (1s)
        defensiveResetCoroutine = StartCoroutine(ResetDefensiveAfterDelay(1.1f)); // Slightly longer than exit time
    }
    
    private IEnumerator ResetDefensiveAfterFrames(int frames)
    {
        // Wait for the specified number of frames
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
        
        // Reset defensive state after frames have passed
        if (animator != null)
        {
            animator.SetBool("IsDefending", false);
        }
        
        defensiveResetCoroutine = null;
    }
    
    private IEnumerator ResetDefensiveAfterDelay(float delay)
    {
        // Wait for the delay (matching animator exit time)
        yield return new WaitForSeconds(delay);
        
        // Reset defensive state after delay
        if (animator != null)
        {
            animator.SetBool("IsDefending", false);
            Debug.Log("[Enemy] Reset IsDefending=false after defensive action");
        }
        
        defensiveResetCoroutine = null;
    }
    
    private void AttemptAttack()
    {
        if (animator == null)
            return;
        
        // Block offensive actions if defensive is active
        if (animator.GetBool("IsDefending"))
            return;
        
        // If already punching, cancel current attack first
        if (animator.GetBool("IsPunching"))
        {
            CancelCurrentAttack();
        }
        
        // Select attack based on weights
        string selectedAttack = SelectWeightedAttack();
        string attackType = GetAttackTypeFromTrigger(selectedAttack);
        
        // Determine attack duration (Straight is longer, others are shorter)
        float attackDuration = (selectedAttack.Contains("Straight")) ? LONG_ATTACK_DURATION : SHORT_ATTACK_DURATION;
        
        // Set attack state
        animator.SetBool("IsPunching", true);
        attackStartTime = Time.time; // Track when attack started
        
        // Set the appropriate attack trigger
        animator.SetTrigger(selectedAttack);
        
        // Activate appropriate hitbox
        ActivateHitboxForAttack(attackType);
        
        // Start coroutine to hold attack for proper duration, then reset
        StartCoroutine(HoldAttackForDuration(attackDuration, attackType));
        
        // Update last punch time
        lastPunchTime = Time.time;
        idleTimer = 0f; // Reset idle timer when attacking
    }
    
    private void CancelCurrentAttack()
    {
        if (animator == null)
            return;
        
        // Cancel attack with transition time
        animator.SetTrigger("AttackCanceled");
        animator.SetBool("IsPunching", false);
        attackStartTime = 0f;
        
        // Deactivate hitboxes
        if (hitboxes != null)
        {
            foreach (var hitbox in hitboxes)
            {
                if (hitbox != null)
                    hitbox.Deactivate();
            }
        }
        
        // Add cancel transition time to cooldown (reduce remaining cooldown by transition time)
        float timeSinceLastPunch = Time.time - lastPunchTime;
        if (timeSinceLastPunch < punchCooldown)
        {
            // Adjust lastPunchTime so cooldown ends earlier (accounting for cancel transition)
            lastPunchTime = Time.time - (punchCooldown - ATTACK_CANCEL_TRANSITION);
        }
    }
    
    private IEnumerator HoldAttackForDuration(float duration, string attackType)
    {
        // Hold the attack for its proper duration
        yield return new WaitForSeconds(duration);
        
        // Deactivate hitboxes
        if (hitboxes != null)
        {
            foreach (var hitbox in hitboxes)
            {
                if (hitbox != null)
                    hitbox.Deactivate();
            }
        }
        
        // Trigger AttackCanceled to transition back to high_idle
        if (animator != null)
        {
            animator.SetTrigger("AttackCanceled");
            animator.SetBool("IsPunching", false);
            attackStartTime = 0f;
        }
    }
    
    private string GetAttackTypeFromTrigger(string triggerName)
    {
        // Map trigger names to attack types
        if (triggerName.Contains("Jab")) return "Jab";
        if (triggerName.Contains("Straight")) return "Straight";
        if (triggerName.Contains("LeftHook")) return "LeftHook";
        if (triggerName.Contains("RightHook")) return "RightHook";
        if (triggerName.Contains("Uppercut")) return "Uppercut";
        return "Unknown";
    }
    
    private void ActivateHitboxForAttack(string attackType)
    {
        foreach (var hitbox in hitboxes)
        {
            if (hitbox != null)
            {
                hitbox.SetAttackType(attackType);
                hitbox.Activate();
            }
        }
    }
    
    // This method is no longer used - replaced by HoldAttackForDuration
    // Keeping for backwards compatibility if needed
    private IEnumerator ResetAttackAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Deactivate all hitboxes
        foreach (var hitbox in hitboxes)
        {
            if (hitbox != null)
                hitbox.Deactivate();
        }
        
        if (animator != null)
        {
            // Trigger AttackCanceled to transition back to high_idle (per animator setup)
            animator.SetTrigger("AttackCanceled");
            animator.SetBool("IsPunching", false);
            attackStartTime = 0f;
        }
    }
    
    private string SelectWeightedAttack()
    {
        // Calculate total weight
        float totalWeight = 0f;
        foreach (float weight in attackWeights)
        {
            totalWeight += weight;
        }
        
        // Select random value
        float randomValue = Random.Range(0f, totalWeight);
        
        // Find which attack to use
        float currentWeight = 0f;
        for (int i = 0; i < availableAttacks.Length; i++)
        {
            currentWeight += attackWeights[i];
            if (randomValue <= currentWeight)
            {
                return availableAttacks[i];
            }
        }
        
        // Fallback to first attack
        return availableAttacks[0];
    }
    
    private void FixedUpdate()
    {
        // Apply movement on the z-axis (inverted for enemy facing negative z)
        // Enemy moves in opposite direction to player
        Vector3 targetVelocity = new Vector3(0, rb.linearVelocity.y, moveInput * moveSpeed);
        
        // Smoothly interpolate to target velocity
        if (moveInput != 0f)
        {
            // Accelerating
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        }
        else
        {
            // Decelerating (stopping)
            Vector3 stopVelocity = new Vector3(0, rb.linearVelocity.y, 0);
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, stopVelocity, deceleration * Time.fixedDeltaTime);
        }
        
        // Constrain position on z-axis (same bounds as player, but inverted)
        Vector3 position = transform.position;
        position.z = Mathf.Clamp(position.z, -3.25f, 4.5f);
        transform.position = position;
    }
    
    private IEnumerator TestModeCycle()
    {
        Debug.Log("[Enemy] TEST MODE: Starting cycle - testMode=" + testMode);
        
        // Array of all actions to test: attacks first, then defensive
        string[] attackActions = { "JabPressed", "StraightPressed", "LeftHookPressed" };
        string[] defensiveActions = { "LeanPressed", "SlipLeftPressed", "SlipRightPressed", "DuckPressed" };
        
        int cycleCount = 0;
        while (testMode)
        {
            cycleCount++;
            Debug.Log($"[Enemy] TEST MODE: Starting cycle #{cycleCount}");
            
            // Test all attack actions
            foreach (string attackTrigger in attackActions)
            {
                if (!testMode) break; // Check if test mode was disabled
                
                string attackType = attackTrigger.Replace("Pressed", "");
                Debug.Log($"[Enemy] TEST MODE: Performing {attackType}");
                
                if (animator != null)
                {
                    // Wait until animator is ready (not punching or defending)
                    int waitFrames = 0;
                    while ((animator.GetBool("IsPunching") || animator.GetBool("IsDefending")) && waitFrames < 60)
                    {
                        yield return null;
                        waitFrames++;
                    }
                    
                    if (!animator.GetBool("IsPunching") && !animator.GetBool("IsDefending"))
                    {
                        animator.SetBool("IsPunching", true);
                        animator.SetTrigger(attackTrigger);
                        Debug.Log($"[Enemy] TEST MODE: Set IsPunching=true, triggered {attackTrigger}");
                        
                        // Activate hitbox if available
                        if (hitboxes != null && hitboxes.Length > 0)
                        {
                            foreach (var hitbox in hitboxes)
                            {
                                if (hitbox != null)
                                {
                                    hitbox.SetAttackType(attackType);
                                    hitbox.Activate();
                                }
                            }
                        }
                        
                        // Wait a frame for animator to process
                        yield return null;
                        
                        // Wait minimal time for animation to play (0.3s should be enough to see it)
                        yield return new WaitForSeconds(0.3f);
                        
                        // Cancel attack to return to idle
                        animator.SetTrigger("AttackCanceled");
                        animator.SetBool("IsPunching", false);
                        
                        // Deactivate hitboxes
                        if (hitboxes != null)
                        {
                            foreach (var hitbox in hitboxes)
                            {
                                if (hitbox != null)
                                    hitbox.Deactivate();
                            }
                        }
                        
                        // Small delay before next action
                        yield return new WaitForSeconds(0.2f);
                    }
                }
            }
            
            // Test all defensive actions
            foreach (string defensiveTrigger in defensiveActions)
            {
                if (!testMode) break; // Check if test mode was disabled
                
                string defensiveType = defensiveTrigger.Replace("Pressed", "");
                Debug.Log($"[Enemy] TEST MODE: Performing {defensiveType}");
                
                if (animator != null)
                {
                    // Wait until animator is ready (not punching or defending)
                    int waitFrames = 0;
                    while ((animator.GetBool("IsPunching") || animator.GetBool("IsDefending")) && waitFrames < 60)
                    {
                        yield return null;
                        waitFrames++;
                    }
                    
                    if (!animator.GetBool("IsPunching") && !animator.GetBool("IsDefending"))
                    {
                        animator.SetBool("IsDefending", true);
                        animator.SetTrigger(defensiveTrigger);
                        Debug.Log($"[Enemy] TEST MODE: Set IsDefending=true, triggered {defensiveTrigger}");
                        
                        // Wait a frame for animator to process
                        yield return null;
                        
                        // Wait for defensive action (exit time = 1, so wait slightly longer)
                        yield return new WaitForSeconds(1.1f);
                        
                        // Reset defensive state (animator should auto-transition, but reset bool)
                        animator.SetBool("IsDefending", false);
                        
                        // Small delay before next action
                        yield return new WaitForSeconds(0.2f);
                    }
                }
            }
            
            Debug.Log("[Enemy] TEST MODE: Full cycle complete, starting next cycle");
            yield return new WaitForSeconds(0.5f); // Brief pause between full cycles
        }
        
        Debug.Log("[Enemy] TEST MODE: Exited loop (testMode is now false)");
    }
}
