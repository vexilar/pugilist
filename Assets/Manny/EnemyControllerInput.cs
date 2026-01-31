using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class EnemyControllerInput : MonoBehaviour
{
    [Header("AI Toggle")]
    [Tooltip("When true, enemy stands still and is controlled only by input. When false, full AI runs.")]
    [SerializeField] private bool disableAI = true;

    [Header("Input")]
    [Tooltip("Assign an Input Action asset with an 'Enemy' map and actions: Enemy_Move, Enemy_Jab, Enemy_Straight, etc.")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Target (used only when AI enabled)")]
    [SerializeField] private Transform playerTarget;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float deceleration = 10f;

    [Header("AI Settings (used only when AI enabled)")]
    [SerializeField] private float punchRange = 2f;
    [SerializeField] private float punchCooldown = 2.0f;
    [SerializeField] private float dodgeChance = 0.7f;
    [SerializeField] private float reactionTime = 0.15f;

    [Header("Debug/Test (AI only)")]
    [SerializeField] private bool testMode = false;

    [Header("Attack Weights (AI only)")]
    [SerializeField] private float jabWeight = 1f;
    [SerializeField] private float straightWeight = 0.8f;
    [SerializeField] private float leftHookWeight = 0.6f;

    [Header("Hitboxes")]
    [SerializeField] private Hitbox[] hitboxes;

    private Rigidbody rb;
    private Animator animator;
    private float moveInput;
    private PlayerController playerController;

    // Input actions
    private InputAction moveAction;
    private Dictionary<InputAction, string> attackActions = new Dictionary<InputAction, string>();
    private Dictionary<InputAction, string> defensiveActions = new Dictionary<InputAction, string>();

    // AI state (only used when disableAI == false)
    private float lastPunchTime;
    private float attackStartTime;
    private bool isReactingToPlayerAttack;
    private string detectedPlayerAttack;
    private string lastDetectedAttack;
    private Coroutine defensiveResetCoroutine;
    private Coroutine reactionCoroutine;
    private float idleTimer;
    private Coroutine testModeCoroutine;

    private const float LONG_ATTACK_DURATION = 0.22f;
    private const float SHORT_ATTACK_DURATION = 0.21f;
    private const float ATTACK_CANCEL_TRANSITION = 0.1f;

    private readonly string[] availableAttacks = { "JabPressed", "StraightPressed", "LeftHookPressed" };
    private readonly float[] attackWeights = new float[3];

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();

        attackWeights[0] = jabWeight;
        attackWeights[1] = straightWeight;
        attackWeights[2] = leftHookWeight;

        if (hitboxes == null || hitboxes.Length == 0)
            hitboxes = GetComponentsInChildren<Hitbox>();

        foreach (var hitbox in hitboxes)
        {
            if (hitbox != null)
                hitbox.Deactivate();
        }

        transform.rotation = Quaternion.Euler(0, 180f, 0);

        // Set up input when using Enemy_ prefixed actions
        if (inputActions != null)
        {
            var enemyMap = inputActions.FindActionMap("Enemy");
            if (enemyMap != null)
            {
                moveAction = enemyMap.FindAction("Enemy_Move");

                SetupAttackAction(enemyMap, "Enemy_Jab", "JabPressed");
                SetupAttackAction(enemyMap, "Enemy_Straight", "StraightPressed");
                SetupAttackAction(enemyMap, "Enemy_LeftHook", "LeftHookPressed");
                SetupAttackAction(enemyMap, "Enemy_RightHook", "RightHookPressed");
                SetupAttackAction(enemyMap, "Enemy_Uppercut", "UppercutPressed");

                SetupDefensiveAction(enemyMap, "Enemy_Duck", "DuckPressed");
                SetupDefensiveAction(enemyMap, "Enemy_Lean", "LeanPressed");
                SetupDefensiveAction(enemyMap, "Enemy_SlipLeft", "SlipLeftPressed");
                SetupDefensiveAction(enemyMap, "Enemy_SlipRight", "SlipRightPressed");
            }
        }
    }

    private void SetupAttackAction(InputActionMap enemyMap, string actionName, string triggerName)
    {
        var action = enemyMap?.FindAction(actionName);
        if (action != null)
            attackActions[action] = triggerName;
    }

    private void SetupDefensiveAction(InputActionMap enemyMap, string actionName, string triggerName)
    {
        var action = enemyMap?.FindAction(actionName);
        if (action != null)
            defensiveActions[action] = triggerName;
    }

    private void OnEnable()
    {
        if (!disableAI)
            return;

        if (moveAction != null)
            moveAction.Enable();

        foreach (var kvp in attackActions)
        {
            kvp.Key.Enable();
            kvp.Key.performed += OnAttackPerformed;
        }

        foreach (var kvp in defensiveActions)
        {
            kvp.Key.Enable();
            kvp.Key.performed += OnDefensivePerformed;
        }
    }

    private void OnDisable()
    {
        if (moveAction != null)
            moveAction.Disable();

        foreach (var kvp in attackActions)
        {
            kvp.Key.Disable();
            kvp.Key.performed -= OnAttackPerformed;
        }

        foreach (var kvp in defensiveActions)
        {
            kvp.Key.Disable();
            kvp.Key.performed -= OnDefensivePerformed;
        }
    }

    private void Start()
    {
        if (disableAI)
            return;

        if (playerTarget == null)
        {
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                playerTarget = player.transform;
                playerController = player;
            }
        }
        else
        {
            playerController = playerTarget.GetComponent<PlayerController>();
        }

        if (testMode)
            testModeCoroutine = StartCoroutine(TestModeCycle());
    }

    private void Update()
    {
        if (disableAI)
        {
            // Input-only: stand there, move only from input
            moveInput = 0f;
            if (moveAction != null)
            {
                // Use Vector2 (stick): .y = forward/back (z). Bind Enemy_Move as Vector2 in Input Actions.
                Vector2 v = moveAction.ReadValue<Vector2>();
                moveInput = Mathf.Clamp(v.y, -1f, 1f);
            }

            if (animator != null && (animator.GetBool("IsPunching") || animator.GetBool("IsDefending")))
                moveInput = 0f;

            return;
        }

        // ---------- AI behaviour below ----------
        if (playerTarget == null || animator == null)
            return;

        if (testMode)
            return;

        CheckPlayerAttack();

        float distanceToPlayer = Vector3.Distance(transform.position, playerTarget.position);
        bool isPunching = animator.GetBool("IsPunching");
        bool isDefending = animator.GetBool("IsDefending");
        bool cooldownReady = Time.time >= lastPunchTime + punchCooldown;

        if (!isPunching && !isDefending)
            idleTimer += Time.deltaTime;
        else
            idleTimer = 0f;

        if (isPunching || isDefending)
            moveInput = 0f;

        bool attackHeldLongEnough = true;
        if (isPunching && attackStartTime > 0f)
        {
            float attackDuration = Time.time - attackStartTime;
            attackHeldLongEnough = attackDuration >= SHORT_ATTACK_DURATION;
        }

        bool canAttack = distanceToPlayer <= punchRange &&
                         cooldownReady &&
                         !isDefending &&
                         (!isPunching || attackHeldLongEnough) &&
                         !isReactingToPlayerAttack &&
                         idleTimer > 0.05f;

        if (canAttack)
            AttemptAttack();

        if (!isPunching && !isDefending)
        {
            float zDistance = playerTarget.position.z - transform.position.z;
            if (distanceToPlayer > punchRange)
                moveInput = Mathf.Clamp(zDistance, -1f, 1f);
            else if (distanceToPlayer < punchRange * 0.8f)
                moveInput = Mathf.Clamp(-zDistance, -1f, 1f) * 0.5f;
            else
                moveInput = 0f;
        }
    }

    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        if (!disableAI || animator == null)
            return;
        if (attackActions.TryGetValue(context.action, out string triggerName))
            DoAttackFromInput(triggerName);
    }

    private void OnDefensivePerformed(InputAction.CallbackContext context)
    {
        if (!disableAI || animator == null)
            return;
        if (defensiveActions.TryGetValue(context.action, out string triggerName))
            DoDefensiveFromInput(triggerName);
    }

    private void DoAttackFromInput(string triggerName)
    {
        if (animator.GetBool("IsDefending"))
            return;
        if (animator.GetBool("IsPunching"))
            CancelCurrentAttack();

        string attackType = GetAttackTypeFromTrigger(triggerName);
        float duration = triggerName.Contains("Straight") ? LONG_ATTACK_DURATION : SHORT_ATTACK_DURATION;

        animator.SetBool("IsPunching", true);
        animator.SetTrigger(triggerName);
        ActivateHitboxForAttack(attackType);
        StartCoroutine(HoldAttackForDuration(duration, attackType));
    }

    private void DoDefensiveFromInput(string triggerName)
    {
        if (animator.GetBool("IsPunching"))
            CancelCurrentAttack();

        if (defensiveResetCoroutine != null)
            StopCoroutine(defensiveResetCoroutine);

        animator.SetBool("IsDefending", true);
        animator.SetTrigger(triggerName);
        defensiveResetCoroutine = StartCoroutine(ResetDefensiveAfterDelay(1.1f));
    }

    private void CheckPlayerAttack()
    {
        if (playerController == null)
            return;

        bool playerIsAttacking = playerController.IsAttacking;
        string currentAttackType = playerController.CurrentAttackType;

        if (playerIsAttacking && !string.IsNullOrEmpty(currentAttackType))
        {
            if (currentAttackType != lastDetectedAttack)
            {
                isReactingToPlayerAttack = true;
                detectedPlayerAttack = currentAttackType;
                lastDetectedAttack = currentAttackType;
                if (reactionCoroutine != null)
                    StopCoroutine(reactionCoroutine);
                reactionCoroutine = StartCoroutine(ReactToPlayerAttack());
            }
        }
        else if (!playerIsAttacking && !isReactingToPlayerAttack)
        {
            lastDetectedAttack = null;
        }
    }

    private IEnumerator ReactToPlayerAttack()
    {
        string attackToReactTo = detectedPlayerAttack;
        yield return new WaitForSeconds(reactionTime);

        if (Random.value > dodgeChance)
        {
            isReactingToPlayerAttack = false;
            detectedPlayerAttack = null;
            reactionCoroutine = null;
            yield break;
        }

        if (attackToReactTo == "Jab")
            PerformDefensiveAction("SlipLeftPressed");
        else if (attackToReactTo == "Straight")
            PerformDefensiveAction("SlipRightPressed");
        else if (attackToReactTo == "LeftHook" || attackToReactTo == "RightHook")
            PerformDefensiveAction("LeanPressed");

        yield return new WaitForSeconds(0.3f);
        isReactingToPlayerAttack = false;
        detectedPlayerAttack = null;
        lastDetectedAttack = null;
        reactionCoroutine = null;
    }

    private void PerformDefensiveAction(string triggerName)
    {
        if (animator == null)
            return;
        if (animator.GetBool("IsPunching"))
            CancelCurrentAttack();
        if (defensiveResetCoroutine != null)
            StopCoroutine(defensiveResetCoroutine);

        animator.SetBool("IsDefending", true);
        animator.SetTrigger(triggerName);
        idleTimer = 0f;
        defensiveResetCoroutine = StartCoroutine(ResetDefensiveAfterDelay(1.1f));
    }

    private IEnumerator ResetDefensiveAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (animator != null)
            animator.SetBool("IsDefending", false);
        defensiveResetCoroutine = null;
    }

    private void AttemptAttack()
    {
        if (animator == null || animator.GetBool("IsDefending"))
            return;
        if (animator.GetBool("IsPunching"))
            CancelCurrentAttack();

        string selectedAttack = SelectWeightedAttack();
        string attackType = GetAttackTypeFromTrigger(selectedAttack);
        float attackDuration = selectedAttack.Contains("Straight") ? LONG_ATTACK_DURATION : SHORT_ATTACK_DURATION;

        animator.SetBool("IsPunching", true);
        attackStartTime = Time.time;
        animator.SetTrigger(selectedAttack);
        ActivateHitboxForAttack(attackType);
        StartCoroutine(HoldAttackForDuration(attackDuration, attackType));
        lastPunchTime = Time.time;
        idleTimer = 0f;
    }

    private void CancelCurrentAttack()
    {
        if (animator == null)
            return;
        animator.SetTrigger("AttackCanceled");
        animator.SetBool("IsPunching", false);
        attackStartTime = 0f;
        if (hitboxes != null)
        {
            foreach (var hitbox in hitboxes)
            {
                if (hitbox != null)
                    hitbox.Deactivate();
            }
        }
        float timeSinceLastPunch = Time.time - lastPunchTime;
        if (timeSinceLastPunch < punchCooldown)
            lastPunchTime = Time.time - (punchCooldown - ATTACK_CANCEL_TRANSITION);
    }

    private IEnumerator HoldAttackForDuration(float duration, string attackType)
    {
        yield return new WaitForSeconds(duration);
        if (hitboxes != null)
        {
            foreach (var hitbox in hitboxes)
            {
                if (hitbox != null)
                    hitbox.Deactivate();
            }
        }
        if (animator != null)
        {
            animator.SetTrigger("AttackCanceled");
            animator.SetBool("IsPunching", false);
            attackStartTime = 0f;
        }
    }

    private string GetAttackTypeFromTrigger(string triggerName)
    {
        if (triggerName.Contains("Jab")) return "Jab";
        if (triggerName.Contains("Straight")) return "Straight";
        if (triggerName.Contains("LeftHook")) return "LeftHook";
        if (triggerName.Contains("RightHook")) return "RightHook";
        if (triggerName.Contains("Uppercut")) return "Uppercut";
        return "Unknown";
    }

    private void ActivateHitboxForAttack(string attackType)
    {
        if (hitboxes == null)
            return;
        foreach (var hitbox in hitboxes)
        {
            if (hitbox != null)
            {
                hitbox.SetAttackType(attackType);
                hitbox.Activate();
            }
        }
    }

    private string SelectWeightedAttack()
    {
        float totalWeight = 0f;
        foreach (float w in attackWeights)
            totalWeight += w;
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        for (int i = 0; i < availableAttacks.Length; i++)
        {
            currentWeight += attackWeights[i];
            if (randomValue <= currentWeight)
                return availableAttacks[i];
        }
        return availableAttacks[0];
    }

    private void FixedUpdate()
    {
        Vector3 targetVelocity = new Vector3(0, rb.linearVelocity.y, moveInput * moveSpeed);

        if (moveInput != 0f)
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        else
        {
            Vector3 stopVelocity = new Vector3(0, rb.linearVelocity.y, 0);
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, stopVelocity, deceleration * Time.fixedDeltaTime);
        }

        Vector3 position = transform.position;
        position.z = Mathf.Clamp(position.z, -3.25f, 4.5f);
        transform.position = position;
    }

    /// <summary>
    /// Called by HitboxController when this enemy is hit by the player's hitbox.
    /// </summary>
    public void OnHitReceived(GameObject attacker, string attackType, Vector3 hitPoint)
    {
        Debug.Log($"[EnemyControllerInput] {gameObject.name} was hit by {attacker.name} with {attackType} at {hitPoint}");
    }

    private IEnumerator TestModeCycle()
    {
        string[] attackActionsArr = { "JabPressed", "StraightPressed", "LeftHookPressed" };
        string[] defensiveActionsArr = { "LeanPressed", "SlipLeftPressed", "SlipRightPressed", "DuckPressed" };

        while (testMode)
        {
            foreach (string attackTrigger in attackActionsArr)
            {
                if (!testMode) break;
                if (animator == null) continue;

                int waitFrames = 0;
                while ((animator.GetBool("IsPunching") || animator.GetBool("IsDefending")) && waitFrames < 60)
                {
                    yield return null;
                    waitFrames++;
                }

                if (!animator.GetBool("IsPunching") && !animator.GetBool("IsDefending"))
                {
                    string attackType = attackTrigger.Replace("Pressed", "");
                    animator.SetBool("IsPunching", true);
                    animator.SetTrigger(attackTrigger);
                    if (hitboxes != null)
                    {
                        foreach (var h in hitboxes)
                        {
                            if (h != null) { h.SetAttackType(attackType); h.Activate(); }
                        }
                    }
                    yield return null;
                    yield return new WaitForSeconds(0.3f);
                    animator.SetTrigger("AttackCanceled");
                    animator.SetBool("IsPunching", false);
                    if (hitboxes != null)
                    {
                        foreach (var h in hitboxes) { if (h != null) h.Deactivate(); }
                    }
                    yield return new WaitForSeconds(0.2f);
                }
            }

            foreach (string defensiveTrigger in defensiveActionsArr)
            {
                if (!testMode) break;
                if (animator == null) continue;

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
                    yield return null;
                    yield return new WaitForSeconds(1.1f);
                    animator.SetBool("IsDefending", false);
                    yield return new WaitForSeconds(0.2f);
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
