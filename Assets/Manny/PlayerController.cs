using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Assign the InputSystem_Actions asset (or any asset with a Player/Move action).")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float deceleration = 10f;

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
    [Tooltip("Hitboxes for attacks. Will be auto-found if not assigned.")]
    [SerializeField] private Hitbox[] hitboxes;

    private Rigidbody rb;
    private Animator animator;
    private float moveInput;
    private InputAction moveAction;

    // Punch type per input action (JabPressed -> 0, StraightPressed -> 1, ...)
    private Dictionary<InputAction, int> attackActions = new Dictionary<InputAction, int>();

    // Dodge type per input action (LeanPressed -> 0, DuckPressed -> 1, ...)
    private Dictionary<InputAction, int> defensiveActions = new Dictionary<InputAction, int>();


    public string CurrentAttackType { get; private set; }
    public bool IsAttacking { get; private set; }

    private int currentPunchType;
    private bool wasPunching;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();

        if (overrideController == null && animator != null && animator.runtimeAnimatorController is AnimatorOverrideController aoc)
            overrideController = aoc;

        if (hitboxes == null || hitboxes.Length == 0)
            hitboxes = GetComponentsInChildren<Hitbox>();

        foreach (var hitbox in hitboxes)
        {
            if (hitbox != null)
                hitbox.Deactivate();
        }

        if (inputActions != null)
        {
            var playerMap = inputActions.FindActionMap("Player");
            moveAction = playerMap?.FindAction("Move");

            SetupAttackAction(playerMap, "Jab", 0);
            SetupAttackAction(playerMap, "Straight", 1);
            SetupAttackAction(playerMap, "LeftHook", 2);
            SetupAttackAction(playerMap, "RightHook", 3);
            SetupAttackAction(playerMap, "Uppercut", 4);

            SetupDefensiveAction(playerMap, "Duck", 0);
            SetupDefensiveAction(playerMap, "Lean", 1);
            SetupDefensiveAction(playerMap, "SlipLeft", 2);
            SetupDefensiveAction(playerMap, "SlipRight", 3);
        }
    }

    private void SetupAttackAction(InputActionMap playerMap, string actionName, int punchType)
    {
        var action = playerMap?.FindAction(actionName);
        if (action != null)
            attackActions[action] = punchType;
    }

    private void SetupDefensiveAction(InputActionMap playerMap, string actionName, int dodgeType)
    {
        var action = playerMap?.FindAction(actionName);
        if (action != null)
            defensiveActions[action] = dodgeType;
    }

    private void OnEnable()
    {
        if (moveAction != null)
            moveAction.Enable();

        foreach (var kvp in attackActions)
        {
            kvp.Key.Enable();
            kvp.Key.performed += OnAttackPerformed;
            kvp.Key.canceled += OnAttackCanceled;
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
            kvp.Key.canceled -= OnAttackCanceled;
        }

        foreach (var kvp in defensiveActions)
        {
            kvp.Key.Disable();
            kvp.Key.performed -= OnDefensivePerformed;
        }
    }

    private void Update()
    {
        moveInput = 0f;
        if (moveAction != null)
        {
            Vector2 v = moveAction.ReadValue<Vector2>();
            moveInput = Mathf.Clamp(v.x, -1f, 1f);
        }

        if (animator != null && (animator.GetBool("IsPunching") || animator.GetBool("IsDefending")))
            moveInput = 0f;

        if (IsIdling() && animator.GetBool("QueuedAttackExists")){
            int queuedAttack = animator.GetInteger("QueuedAttack");
            if (currentPunchType != queuedAttack) {
                ApplyPunchOverrides(queuedAttack);
            }
            currentPunchType = queuedAttack;
            animator.SetBool("QueuedAttackExists", false);
            animator.SetInteger("QueuedAttack", 0);
            animator.SetTrigger("Attack");
        }

        // Sync hitboxes to IsPunching (set by PunchBehaviour in animator graph)
        bool isPunching = animator.GetBool("IsPunching");
        if (hitboxes != null)
        {
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
        }
        wasPunching = isPunching;
    }

    private bool IsHoldComplete()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsTag("hold") && state.normalizedTime >= 1f;
    }
    
    private bool IsHolding(){
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsTag("hold");
    }

    private bool IsIdling()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        return state.IsTag("idle");
    }

    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        if (animator == null)
            return;

        if (!attackActions.TryGetValue(context.action, out int punchType))
            return;

        HandleAttack(punchType);
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
            return;

        bool isHold = IsHolding();
        bool isHoldComplete = IsHoldComplete();

        if (isHold && !isHoldComplete)
        {
            animator.SetBool("QueuedAttackExists", true);
            animator.SetInteger("QueuedAttack", punchType);
        } else {
            if (currentPunchType != punchType) {
                ApplyPunchOverrides(punchType);
            }
            currentPunchType = punchType;
            //we are not holding, so we trigger the attack
            animator.SetTrigger("Attack");
        }

    }

    private void ApplyPunchOverrides(int punchType)
    {
        if (overrideController == null)
            return;

        AnimationClip back = GetPunchBackClip(punchType);
        AnimationClip finished = GetPunchFinishedClip(punchType);
        if (back != null)
            overrideController["punch_back"] = back;
        if (finished != null)
            overrideController["punch_finished"] = finished;
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

    private void OnAttackCanceled(InputAction.CallbackContext context)
    {
        // if (animator == null)
        //     return;

        // if (!animator.GetBool("IsPunching"))
        //     return;

        // if (!hasFiredCommitForCurrentPunch)
        // {
        //     foreach (var h in hitboxes)
        //     {
        //         if (h != null)
        //             h.Deactivate();
        //     }
        //     IsAttacking = false;
        //     CurrentAttackType = null;
        // }
        // else if (animator.GetBool("QueuedAttackExists"))
        // {
        //     animator.SetTrigger("AttackCanceled");
        // }

        // animator.SetBool("IsPunching", false);
        // hasFiredCommitForCurrentPunch = false;
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

    private void HandleDefensive(int dodgeType)
    {
        if (animator == null)
            return;

        Debug.Log($"Handle defensive called, isDefending: {animator.GetBool("IsDefending")}");

        if (animator.GetBool("IsDefending") || animator.GetBool("IsPunching"))
            return;

        // if (defensiveResetCoroutine != null)
        // {
        //     StopCoroutine(defensiveResetCoroutine);
        // }

        ApplyDodgeOverride(dodgeType);

        //animator.SetInteger("DodgeType", dodgeType);
        //animator.SetBool("IsDefending", true);
        animator.SetTrigger("DodgePressed");

        //defensiveResetCoroutine = StartCoroutine(ResetDefensiveAfterFrames(24));
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

    private void OnDefensivePerformed(InputAction.CallbackContext context)
    {
        if (animator == null)
            return;

        if (defensiveActions.TryGetValue(context.action, out int dodgeType))
            HandleDefensive(dodgeType);
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
}
