# Enemy AI Combat Instructions

## Reading Player Actions

The enemy AI should monitor the `PlayerController` component to detect player attack and defensive actions. Access the player's state through the following public properties:

### PlayerController Public Properties

- **`IsAttacking`** (bool): Indicates whether the player is currently performing an attack
- **`CurrentAttackType`** (string): The type of attack the player is currently executing. Possible values:
  - `"Jab"` - A quick straight punch
  - `"Straight"` - A powerful straight punch
  - `"LeftHook"` - A left hook punch
  - `"RightHook"` - A right hook punch
  - `"Uppercut"` - An uppercut punch

### Detection Pattern

```csharp
if (playerController.IsAttacking && !string.IsNullOrEmpty(playerController.CurrentAttackType))
{
    string attackType = playerController.CurrentAttackType;
    // React to the attack type
}
```

The `CurrentAttackType` is set when the attack begins and cleared when the attack completes (after ~1 second delay). Monitor these properties in `Update()` to react in real-time.

---

## Defensive Reactions

When the player initiates an attack, the enemy should respond with the appropriate defensive maneuver based on the attack type:

### Defensive Reaction Rules

1. **Player throws a Jab** → Enemy should **Slip Left**
   - Trigger: `"SlipLeftPressed"`
   - This defensive action moves the enemy's head to the left, avoiding the jab coming from the player's right hand

2. **Player throws a Straight** → Enemy should **Slip Right**
   - Trigger: `"SlipRightPressed"`
   - This defensive action moves the enemy's head to the right, avoiding the straight punch

3. **Player throws a Hook** (LeftHook or RightHook) → Enemy should **Lean Back**
   - Trigger: `"LeanPressed"`
   - This defensive action causes the enemy to lean backward, avoiding the hook's arc

### Implementation Notes

- Defensive actions should be triggered immediately upon detecting the player's attack (with optional reaction time delay for realism)
- Only one defensive action should be active at a time
- Defensive actions cannot be performed while the enemy is already attacking (`IsPunching` must be false)
- The defensive state (`IsDefending`) should be set to `true` when performing defensive actions
- Defensive state should reset after 24 frames

---

## Offensive Actions

The enemy can perform the following attacks:

### Available Attacks

1. **Jab** - Trigger: `"JabPressed"`
2. **Straight** - Trigger: `"StraightPressed"`
3. **Left Hook** - Trigger: `"LeftHookPressed"`

**Note:** Right Hook and Uppercut are not currently implemented and should not be used.

### Attack Selection

- Attacks should be selected based on weighted probabilities or strategic decision-making
- Attacks can only be performed when:
  - Enemy is not currently defending (`IsDefending` must be false)
  - Enemy is within punch range of the player
  - Attack cooldown has elapsed
  - Enemy is not already attacking (`IsPunching` must be false)

### Feinting

The enemy can perform **feints** to deceive the player, but feints must be more than just "tapping" the attack inputs. A feint should:

- Begin the attack animation/trigger
- Cancel the attack before it fully commits (using `"AttackCanceled"` trigger)
- Not activate hitboxes or cause damage
- Be used strategically to bait player reactions or create openings

**Feint Implementation:**
- Trigger the attack animation normally
- Immediately (or very quickly) trigger `"AttackCanceled"` to cancel the attack
- Ensure hitboxes are not activated during feints
- Set `IsPunching` to `false` after canceling

**Important:** Do not simply "tap" inputs - feints should involve proper animation triggers that are then canceled, creating a believable feint motion.

---

## State Management

### Attack State
- Set `IsPunching` to `true` when initiating an attack
- Activate appropriate hitbox for the attack type
- Reset `IsPunching` to `false` after attack completes (~1 second)
- Deactivate all hitboxes when attack ends

### Defensive State
- Set `IsDefending` to `true` when performing defensive actions
- Reset `IsDefending` to `false` after 24 frames
- Defensive actions block offensive actions (and vice versa)

### Movement Restrictions
- Movement should be disabled while `IsPunching` or `IsDefending` is `true`
- Normal movement logic should resume after these states clear

---

## Strategic Considerations

1. **Reaction Timing**: Consider implementing a small reaction delay (`reactionTime`) to make the AI feel more realistic rather than instantly reacting to every attack.

2. **Dodge Probability**: Not every attack needs to be dodged - consider using a `dodgeChance` probability to make some attacks connect, creating more dynamic combat.

3. **Attack Variety**: Use weighted selection or strategic logic to vary attack patterns rather than always using the same attack.

4. **Range Management**: The enemy should maintain appropriate distance - move in for attacks, step back when too close, and react defensively when the player attacks.

5. **Feint Usage**: Use feints strategically:
   - After the player has been defensive
   - To create openings in the player's guard
   - To bait the player into attacking (then counter-attack)
   - Not too frequently (feints should feel meaningful)
