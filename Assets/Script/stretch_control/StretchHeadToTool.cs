using UnityEngine;
using UnityEngine.InputSystem;

public class StretchHeadToTool : MonoBehaviour
{
    public InputActionReference track_tools;    // Reference to primary button action (e.g., Lefthand Primary Button)
    public InputActionReference reset_to_normal; // Reference to secondary button action (e.g., Lefthand Secondary Button)
    
    public GameObject panObject;   // The head's pan object (horizontal movement)
    public GameObject tiltObject;  // The head's tilt object (vertical movement)
    public GameObject tool;        // The tool that needs to be looked at

    private Quaternion initialPanlocalRotation;   // Stores initial local rotation of pan object
    private Quaternion initialTiltlocalRotation;  // Stores initial local rotation of tilt object

    public bool debug_message = false;
    void Start()
    {
        // Store the initial local rotations
        if (panObject != null)
        {
            initialPanlocalRotation = panObject.transform.localRotation;
        }
        if (tiltObject != null)
        {
            initialTiltlocalRotation = tiltObject.transform.localRotation;
        }

        // Bind input actions to methods
        track_tools.action.performed += ctx => LookAtTool();
        reset_to_normal.action.performed += ctx => ResetPosition();
    }

    void OnDestroy()
    {
        // Unbind input actions to prevent errors on object destruction
        track_tools.action.performed -= ctx => LookAtTool();
        reset_to_normal.action.performed -= ctx => ResetPosition();
    }

    private void LookAtTool()
    {
        if (panObject != null && tiltObject != null && tool != null) 
        {
            // Adjust pan object to look at the tool on the horizontal plane (XZ plane)
            panObject.transform.LookAt(new Vector3(tool.transform.position.x, panObject.transform.position.y, tool.transform.position.z));
            
            // Calculate the direction from the tilt object to the tool
            Vector3 directionToTool = tool.transform.position - tiltObject.transform.position;
            
            // Calculate the tilt angle 
            float angleX = Mathf.Atan2(directionToTool.y, Mathf.Sqrt(directionToTool.x * directionToTool.x + directionToTool.z * directionToTool.z)) * Mathf.Rad2Deg;
            if(debug_message)
            {
                print($"direction:{directionToTool}, angle:{angleX}");
            }
            // 
            // Apply the tilt rotation, modifying only the local X rotation
            Vector3 currentRotation = tiltObject.transform.localEulerAngles;
            tiltObject.transform.localEulerAngles = new Vector3(initialTiltlocalRotation.eulerAngles.x + (-angleX), currentRotation.y, currentRotation.z);

        }
        else
        {
            Debug.LogWarning("One or more objects are not assigned.");
        }
    }

    private void ResetPosition()
    {
        if (panObject != null)
        {
            panObject.transform.localRotation = initialPanlocalRotation; // Reset pan object
        }

        if (tiltObject != null)
        {
            tiltObject.transform.localRotation = initialTiltlocalRotation; // Reset tilt object
        }
    }
}
