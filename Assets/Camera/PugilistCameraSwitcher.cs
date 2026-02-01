using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class PugilistCameraSwitcher : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Assign the InputSystem_Actions asset (or any asset with Player/SwitchCam1, SwitchCam2, SwitchCam3).")]
    [SerializeField] private InputActionAsset inputActions;

    [SerializeField] private PunchSelectUI punchUI;

    [Header("Assign your CinemachineCameras")]
    public CinemachineCamera Cam1;
    public CinemachineCamera Cam2;
    public CinemachineCamera Cam3;

    [Header("Priority values")]
    public int activePriority = 20;
    public int inactivePriority = 0;

    private InputAction _switchCam1;
    private InputAction _switchCam2;
    private InputAction _switchCam3;

    void Awake()
    {
        if (inputActions != null)
        {
            var map = inputActions.FindActionMap("Player");
            _switchCam1 = map?.FindAction("SwitchCam1");
            _switchCam2 = map?.FindAction("SwitchCam2");
            _switchCam3 = map?.FindAction("SwitchCam3");
        }
    }

    void OnEnable()
    {
        _switchCam1?.Enable();
        _switchCam2?.Enable();
        _switchCam3?.Enable();
        punchUI.OnPunchConfirmed += OnPunchConfirmed; // sub
    }

    void OnDisable()
    {
        _switchCam1?.Disable();
        _switchCam2?.Disable();
        _switchCam3?.Disable();
        punchUI.OnPunchConfirmed -= OnPunchConfirmed;  // unsub
    }

    void OnPunchConfirmed(string punchId)
    {
        Debug.Log($"Confirmed punch: {punchId}");
    }

    void Start()
    {
        ActivateCam1();
    }

    void Update()
    {
        if (_switchCam1 != null && _switchCam1.WasPressedThisFrame()) ActivateCam1();
        if (_switchCam2 != null && _switchCam2.WasPressedThisFrame()) ActivateCam2();
        if (_switchCam3 != null && _switchCam3.WasPressedThisFrame()) ActivateCam3();
    }

    void SetAllInactive()
    {
        if (Cam1 != null) Cam1.Priority = inactivePriority;
        if (Cam2 != null) Cam2.Priority = inactivePriority;
        if (Cam3 != null) Cam3.Priority = inactivePriority;
    }

    public void ActivateCam1()
    {
        SetAllInactive();
        if (Cam1 != null) Cam1.Priority = activePriority;
        if (punchUI != null) punchUI.Close();
    }

    public void ActivateCam2()
    {
        SetAllInactive();
        if (Cam2 != null) Cam2.Priority = activePriority;
        if (punchUI != null) punchUI.Open();
    }

    public void ActivateCam3()
    {
        SetAllInactive();
        if (Cam3 != null) Cam3.Priority = activePriority;
        if (punchUI != null) punchUI.Close();
    }
}
