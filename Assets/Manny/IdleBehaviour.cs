using UnityEngine;

public class IdleBehaviour : StateMachineBehaviour
{
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        //Debug.Log($"IdleResetBehaviour: OnStateEnter");
        //animator.SetBool("IsPunching", false);
    }
}
