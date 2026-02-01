using UnityEngine;

public class PerpendicularToCharacterUI : MonoBehaviour
{
    [SerializeField] private Transform anchor;        // PunchUI_Anchor
    [SerializeField] private Transform characterRoot; // Player root
    [SerializeField] private Vector3 localOffset = Vector3.zero;

    [Header("Orientation")]
    [SerializeField] private bool faceAwayFromCharacter = false;

    void LateUpdate()
    {
        if (anchor == null || characterRoot == null) return;

        // Position
        transform.position = anchor.TransformPoint(localOffset);

        // Make the UI plane perpendicular to character forward:
        // "Forward" of the UI should match character forward (or the opposite),
        // so its plane normal points forward/back along character forward.
        Vector3 fwd = characterRoot.forward;
        if (faceAwayFromCharacter) fwd = -fwd;

        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }
}
