using UnityEngine;
using UnityEngine.InputSystem;

public class EndEffectorEnable : MonoBehaviour
{
    public InputActionReference enable_action;
    public RobotEndEffectorControl Control_scripts;

    void Start()
    {
        enable_action.action.performed += EnableActionFunction;
        enable_action.action.canceled += DisableActionFunction; // Handle button release
        Control_scripts.enabled = false; // Start with the script disabled
    }

    private void EnableActionFunction(InputAction.CallbackContext callback)
    {
        Control_scripts.enabled = true; // Enable the script when the button is pressed
        print("grip pressed");
    }

    private void DisableActionFunction(InputAction.CallbackContext callback)
    {
        Control_scripts.enabled = false; // Disable the script when the button is released
    }

    void OnDestroy()
    {
        enable_action.action.performed -= EnableActionFunction;
        enable_action.action.canceled -= DisableActionFunction;
    }
}
