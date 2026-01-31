using UnityEngine;

public class PunchBackBehaviour : StateMachineBehaviour
{
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        //Debug.Log($"PunchBackResetBehaviour: OnStateEnter");
        //animator.SetBool("QueuedAttackExists", false);
        //animator.SetInteger("QueuedAttack", 0);
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        //Debug.Log($"PunchBackResetBehaviour: OnStateEnter");
        //animator.SetInteger("QueuedAttack", 0);
       
    }
}
