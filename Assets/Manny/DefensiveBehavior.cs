using UnityEngine;

public class DefensiveBehaviour : StateMachineBehaviour
{
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        //Debug.Log($"Setting IsPunching to true");
        animator.SetBool("IsDefending", true);
        //animator.SetInteger("QueuedAttack", 0);
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetBool("IsDefending", false);
        //Debug.Log($"PunchBackResetBehaviour: OnStateEnter");
        //animator.SetInteger("QueuedAttack", 0);
       
    }
}
