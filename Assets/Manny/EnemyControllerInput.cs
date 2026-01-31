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

    [Header("Animator Override")]
    [Tooltip("Override controller used for punch and dodge clips. Base controller must have clips named 'punch_back', 'punch_finished', and 'dodge'.")]
    [SerializeField] private AnimatorOverrideController overrideController;

    [Header("Punch Overrides - Back")]
    [SerializeField] private AnimationClip jabBack;
    [SerializeField] private AnimationClip straightBack;
    [SerializeField] private AnimationClip leftHookBack;
    [SerializeField] private AnimationClip rightHookBack;
    [SerializeField] private AnimationClip uppercutBack;

    [Header("Punch Overrides - Finished")]
    [SerializeField] private AnimationClip jabFinished;
    [SerializeField] private AnimationClip straightFinished;
    [SerializeField] private AnimationClip leftHookFinished;
    [SerializeField] private AnimationClip rightHookFinished;
    [SerializeField] private AnimationClip uppercutFinished;

    [Header("Dodge Overrides")]
    [Tooltip("Clips for dodge state override. Order: Duck, Lean, SlipLeft, SlipRight.")]
    [SerializeField] private AnimationClip leanClip;
    [SerializeField] private AnimationClip duckClip;
    [SerializeField] private AnimationClip slipLeftClip;
    [SerializeField] private AnimationClip slipRightClip;

    [Header("Hitboxes")]
    [SerializeField] private Hitbox[] hitboxes;

    private Rigidbody rb;
    private Animator animator;
    private float moveInput;
    private PlayerController playerController;

    // Punch type per input action (0=Jab, 1=Straight, 2=LeftHook, 3=RightHook, 4=Uppercut)
    private Dictionary<InputAction, int> attackActions = new Dictionary<InputAction, int>();
    // Dodge type per input action (0=Duck, 1=Lean, 2=SlipLeft, 3=SlipRight)
    private Dictionary<InputAction, int> defensiveActions = new Dictionary<InputAction, int>();

    private InputAction moveAction;

    // Shared: animator flow (like PlayerController — behaviours set IsPunching/IsDefending)
    private int currentPunchType;
    private bool wasPunching;

    // AI state (only used when disableAI == false)
    private float lastPunchTime;
    private bool isReactingToPlayerAttack;
    private string detectedPlayerAttack;
    private string lastDetectedAttack;
    private Coroutine reactionCoroutine;
    private float idleTimer;
    private Coroutine testModeCoroutine;

    private const float ATTACK_CANCEL_TRANSITION = 0.1f;

    // AI: punch types 0, 1, 2 (Jab, Straight, LeftHook)
    private readonly int[] availablePunchTypes = { 0, 1, 2 };
    private readonly float[] attackWeights = new float[3];

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();

        if (overrideController == null && animator != null && animator.runtimeAnimatorController is AnimatorOverrideController aoc)
            overrideController = aoc;

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

        if (inputActions != null)
        {
            // Prefer "Enemy" map; fallback to "Player" (Enemy_* actions may live in Player map in shared asset)
            var enemyMap = inputActions.FindActionMap("Enemy") ?? inputActions.FindActionMap("Player");
            if (enemyMap != null)
            {
                moveAction = enemyMap.FindAction("Enemy_Move");

                SetupAttackAction(enemyMap, "Enemy_Jab", 0);
                SetupAttackAction(enemyMap, "Enemy_Straight", 1);
                SetupAttackAction(enemyMap, "Enemy_LeftHook", 2);
                SetupAttackAction(enemyMap, "Enemy_RightHook", 3);
                SetupAttackAction(enemyMap, "Enemy_Uppercut", 4);

                SetupDefensiveAction(enemyMap, "Enemy_Duck", 0);
                SetupDefensiveAction(enemyMap, "Enemy_Lean", 1);
                SetupDefensiveAction(enemyMap, "Enemy_SlipLeft", 2);
                SetupDefensiveAction(enemyMap, "Enemy_SlipRight", 3);

                LogInputSetup(enemyMap);
            }
            else
                Debug.Log("[EnemyInput] No 'Enemy' or 'Player' action map found in assigned InputActionAsset. Input will not work.");
        }
        else
            Debug.Log("[EnemyInput] InputActionAsset is not assigned on EnemyControllerInput. Assign it in the Inspector.");
    }

    private void LogInputSetup(InputActionMap map)
    {
        int attackCount = attackActions.Count;
        int defCount = defensiveActions.Count;
        Debug.Log($"[EnemyInput] Input setup: map='{map.name}', attackActions={attackCount}, defensiveActions={defCount}, disableAI={disableAI}. " +
            (attackCount == 0 ? "No attack actions found — check action names (e.g. Enemy_Jab)." : ""));
    }

    private void SetupAttackAction(InputActionMap enemyMap, string actionName, int punchType)
    {
        var action = enemyMap?.FindAction(actionName);
        if (action != null)
            attackActions[action] = punchType;
    }

    private void SetupDefensiveAction(InputActionMap enemyMap, string actionName, int dodgeType)
    {
        var action = enemyMap?.FindAction(actionName);
        if (action != null)
            defensiveActions[action] = dodgeType;
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
            moveInput = 0f;
            if (moveAction != null)
            {
                Vector2 v = moveAction.ReadValue<Vector2>();
                moveInput = Mathf.Clamp(v.y, -1f, 1f);
            }

            if (animator != null && (animator.GetBool("IsPunching") || animator.GetBool("IsDefending")))
                moveInput = 0f;

            // Same as PlayerController: when idle and queued attack exists, apply overrides and fire Attack
            if (animator != null && IsIdling() && animator.GetBool("QueuedAttackExists"))
            {
                int queuedAttack = animator.GetInteger("QueuedAttack");
                Debug.Log($"[EnemyInput] Update: firing queued Attack punchType={queuedAttack} ({GetAttackTypeFromPunchType(queuedAttack)})");
                if (currentPunchType != queuedAttack)
                    ApplyPunchOverrides(queuedAttack);
                currentPunchType = queuedAttack;
                animator.SetBool("QueuedAttackExists", false);
                animator.SetInteger("QueuedAttack", 0);
                animator.SetTrigger("Attack");
            }

            // Sync hitboxes to IsPunching (set by PunchBehaviour in animator graph)
            if (animator != null && hitboxes != null)
            {
                bool isPunching = animator.GetBool("IsPunching");
                if (isPunching && !wasPunching)
                    ActivateHitboxForAttack(GetAttackTypeFromPunchType(currentPunchType));
                else if (!isPunching && wasPunching)
                {
                    foreach (var hitbox in hitboxes)
                    {
                        if (hitbox != null)
                            hitbox.Deactivate();
                    }
                }
                wasPunching = isPunching;
            }

            return;
        }

        // ---------- AI behaviour below ----------
        if (playerTarget == null || animator == null)
            return;

        if (testMode)
            return;

        CheckPlayerAttack();

        float distanceToPlayer = Vector3.Distance(transform.position, playerTarget.position);
        bool aiPunching = animator.GetBool("IsPunching");
        bool isDefending = animator.GetBool("IsDefending");
        bool cooldownReady = Time.time >= lastPunchTime + punchCooldown;

        if (!aiPunching && !isDefending)
            idleTimer += Time.deltaTime;
        else
            idleTimer = 0f;

        if (aiPunching || isDefending)
            moveInput = 0f;

        bool canAttack = distanceToPlayer <= punchRange &&
                         cooldownReady &&
                         !isDefending &&
                         !aiPunching &&
                         !isReactingToPlayerAttack &&
                         idleTimer > 0.05f;

        if (canAttack)
            AttemptAttack();

        // Sync hitboxes to IsPunching (set by PunchBehaviour)
        if (hitboxes != null)
        {
            if (aiPunching && !wasPunching)
                ActivateHitboxForAttack(GetAttackTypeFromPunchType(currentPunchType));
            else if (!aiPunching && wasPunching)
            {
                foreach (var hitbox in hitboxes)
                {
                    if (hitbox != null)
                        hitbox.Deactivate();
                }
            }
            wasPunching = aiPunching;
        }

        if (!aiPunching && !isDefending)
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
        Debug.Log($"[EnemyInput] OnAttackPerformed: action={context.action?.name}, disableAI={disableAI}, animator={animator != null}");
        if (!disableAI)
        {
            Debug.Log("[EnemyInput] Ignored: AI is enabled (input only when disableAI=true).");
            return;
        }
        if (animator == null)
        {
            Debug.Log("[EnemyInput] Ignored: no Animator.");
            return;
        }
        if (!attackActions.TryGetValue(context.action, out int punchType))
        {
            Debug.Log($"[EnemyInput] Ignored: action '{context.action?.name}' not in attackActions map.");
            return;
        }
        string attackName = GetAttackTypeFromPunchType(punchType);
        Debug.Log($"[EnemyInput] HandleAttack({punchType}) -> {attackName}");
        HandleAttack(punchType);
    }

    private void OnDefensivePerformed(InputAction.CallbackContext context)
    {
        if (!disableAI || animator == null)
            return;
        if (defensiveActions.TryGetValue(context.action, out int dodgeType))
            HandleDefensive(dodgeType);
    }

    private bool IsHoldComplete()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsTag("hold") && state.normalizedTime >= 1f;
    }

    private bool IsHolding()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsTag("hold");
    }

    private bool IsIdling()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsTag("idle");
    }

    private void HandleAttack(int punchType)
    {
        if (animator == null)
            return;

        bool isDefending = animator.GetBool("IsDefending");
        bool isPunching = animator.GetBool("IsPunching");
        bool canCounter = animator.GetBool("CanCounter");
        bool isCounterScenario = isDefending && !isPunching && canCounter;
        if ((isDefending || isPunching) && !isCounterScenario)
        {
            Debug.Log($"[EnemyInput] HandleAttack blocked: isDefending={isDefending}, isPunching={isPunching}, canCounter={canCounter}");
            return;
        }

        bool isHold = IsHolding();
        bool isHoldComplete = IsHoldComplete();

        if (isHold && !isHoldComplete)
        {
            Debug.Log($"[EnemyInput] Queuing attack {punchType} ({GetAttackTypeFromPunchType(punchType)}) until hold complete.");
            animator.SetBool("QueuedAttackExists", true);
            animator.SetInteger("QueuedAttack", punchType);
        }
        else
        {
            if (currentPunchType != punchType)
                ApplyPunchOverrides(punchType);
            currentPunchType = punchType;
            Debug.Log($"[EnemyInput] Firing Attack trigger, punchType={punchType} ({GetAttackTypeFromPunchType(punchType)}), overrideController={overrideController != null}");
            animator.SetTrigger("Attack");
        }
    }

    private void ApplyPunchOverrides(int punchType)
    {
        if (overrideController == null)
        {
            Debug.Log($"[EnemyInput] ApplyPunchOverrides skipped: overrideController is null (animator may not have override clips for punch {punchType}).");
            return;
        }

        AnimationClip back = GetPunchBackClip(punchType);
        AnimationClip finished = GetPunchFinishedClip(punchType);
        if (back != null)
            overrideController["punch_back"] = back;
        if (finished != null)
            overrideController["punch_finished"] = finished;
        Debug.Log($"[EnemyInput] Applied overrides for punchType={punchType}: back={back != null}, finished={finished != null}");
    }

    private AnimationClip GetPunchBackClip(int punchType)
    {
        return punchType switch
        {
            0 => jabBack,
            1 => straightBack,
            2 => leftHookBack,
            3 => rightHookBack,
            4 => uppercutBack,
            _ => null
        };
    }

    private AnimationClip GetPunchFinishedClip(int punchType)
    {
        return punchType switch
        {
            0 => jabFinished,
            1 => straightFinished,
            2 => leftHookFinished,
            3 => rightHookFinished,
            4 => uppercutFinished,
            _ => null
        };
    }

    private static string GetAttackTypeFromPunchType(int punchType)
    {
        return punchType switch
        {
            0 => "Jab",
            1 => "Straight",
            2 => "LeftHook",
            3 => "RightHook",
            4 => "Uppercut",
            _ => "Unknown"
        };
    }

    private void HandleDefensive(int dodgeType)
    {
        if (animator == null)
            return;

        if (animator.GetBool("IsDefending") || animator.GetBool("IsPunching"))
            return;

        ApplyDodgeOverride(dodgeType);
        animator.SetTrigger("DodgePressed");
    }

    private void ApplyDodgeOverride(int dodgeType)
    {
        if (overrideController == null)
            return;

        AnimationClip clip = dodgeType switch
        {
            0 => duckClip,
            1 => leanClip,
            2 => slipLeftClip,
            3 => slipRightClip,
            _ => null
        };
        if (clip != null)
            overrideController["dodge"] = clip;
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
            PerformDefensiveAction(2); // SlipLeft
        else if (attackToReactTo == "Straight")
            PerformDefensiveAction(3); // SlipRight
        else if (attackToReactTo == "LeftHook" || attackToReactTo == "RightHook")
            PerformDefensiveAction(1); // Lean

        yield return new WaitForSeconds(0.3f);
        isReactingToPlayerAttack = false;
        detectedPlayerAttack = null;
        lastDetectedAttack = null;
        reactionCoroutine = null;
    }

    private void PerformDefensiveAction(int dodgeType)
    {
        if (animator == null)
            return;
        if (animator.GetBool("IsPunching"))
            CancelCurrentAttack();
        HandleDefensive(dodgeType);
        idleTimer = 0f;
    }

    private void AttemptAttack()
    {
        if (animator == null || animator.GetBool("IsDefending"))
            return;
        if (animator.GetBool("IsPunching"))
            CancelCurrentAttack();

        int punchType = SelectWeightedPunchType();
        if (currentPunchType != punchType)
            ApplyPunchOverrides(punchType);
        currentPunchType = punchType;
        animator.SetTrigger("Attack");
        lastPunchTime = Time.time;
        idleTimer = 0f;
    }

    private void CancelCurrentAttack()
    {
        if (animator == null)
            return;
        animator.SetTrigger("AttackCanceled");
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

    private int SelectWeightedPunchType()
    {
        float totalWeight = 0f;
        foreach (float w in attackWeights)
            totalWeight += w;
        float randomValue = Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        for (int i = 0; i < availablePunchTypes.Length; i++)
        {
            currentWeight += attackWeights[i];
            if (randomValue <= currentWeight)
                return availablePunchTypes[i];
        }
        return availablePunchTypes[0];
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
        int[] attackPunchTypes = { 0, 1, 2 }; // Jab, Straight, LeftHook
        int[] defensiveDodgeTypes = { 1, 2, 3, 0 }; // Lean, SlipLeft, SlipRight, Duck

        while (testMode)
        {
            foreach (int punchType in attackPunchTypes)
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
                    HandleAttack(punchType);
                    yield return null;
                    yield return new WaitForSeconds(0.5f);
                    yield return new WaitForSeconds(0.2f);
                }
            }

            foreach (int dodgeType in defensiveDodgeTypes)
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
                    HandleDefensive(dodgeType);
                    yield return null;
                    yield return new WaitForSeconds(1.1f);
                    yield return new WaitForSeconds(0.2f);
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
