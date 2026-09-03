using UnityEngine;
using UnityEngine.InputSystem; // For the Input System

public class viewpoint_switch : MonoBehaviour
{
    public GameObject XR_origin;
    public GameObject[] viewPositions;  // List of target positions (GameObjects)
    private int currentIndex = 0;           // Track the current index in the list
    public InputActionReference viewpoint_switch_trigger;
    void Start()
    {
        viewpoint_switch_trigger.action.performed += ctx => SwitchView();;
    }
    private void SwitchView()
    {
        currentIndex = (currentIndex + 1) % viewPositions.Length;
        MoveCameraToTarget(viewPositions[currentIndex].transform.position);
    }

    // Method to instantly move the camera to the target position
    private void MoveCameraToTarget(Vector3 targetPosition)
    {
        XR_origin.transform.position = targetPosition;
    }
     void OnDestroy()
    {
        // Unbind input actions to prevent errors on object destruction
        viewpoint_switch_trigger.action.performed -= ctx => SwitchView();
        
    }
}
