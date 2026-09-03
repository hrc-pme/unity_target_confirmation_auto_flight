using UnityEngine;
using UnityEngine.InputSystem;
public class RobotArmController : MonoBehaviour
{
    public enum Axis { X, Y, Z }
    public InputActionReference Arm_moving_stretching;
    public bool debug_message = false;

    [Header("Vertical Movement Settings")]
    public Axis moveAxis = Axis.X;
    public float minY = 0f;
    public float maxY = 1.1f;
    public float moveSpeed = 0.5f;
    public GameObject lift;

    [Header("Segment Movement Settings")]
    public Axis segmentAxis = Axis.Y;
    public float minSegment = 0f;
    public float maxSegment = 0.13f;
    public float segmentSpeed = 0.2f;
    public GameObject[] segments;

    private Vector3 liftInitialLocalPosition;
    private Vector3[] segmentInitialLocalPositions;

    private float vertical_move = 0;
    private float segment_move = 0;
    private void Awake()
    {
        liftInitialLocalPosition = lift.transform.localPosition;
        segmentInitialLocalPositions = new Vector3[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            segmentInitialLocalPositions[i] = segments[i].transform.localPosition;
        }
    }

    void Start()
    {
        Arm_moving_stretching.action.performed += arm_moving_actions;
    }

    private void arm_moving_actions(InputAction.CallbackContext callback)
    {
        Vector2 value = callback.ReadValue<Vector2>();
        if (debug_message)
        {
            print($"righthand stick value: {value}");
        }
        HandleVerticalMovement(value);
        HandleSegmentMovement(value);
    }

    private void HandleVerticalMovement(Vector2 joy)
    {
        float moveInput = joy.y;
        Vector3 currentLocalPosition = lift.transform.localPosition;

        vertical_move = vertical_move + moveInput * moveSpeed * Time.deltaTime;

        vertical_move = Mathf.Clamp(vertical_move, minY, maxY);
        currentLocalPosition = SetAxisValue(currentLocalPosition, moveAxis, GetAxisValue(liftInitialLocalPosition, moveAxis) - vertical_move);
        lift.transform.localPosition = currentLocalPosition;
    }

    private void HandleSegmentMovement(Vector2 joy)
    {
        float moveInput = joy.x;
        if (moveInput != 0)
        {
            for (int i = 0; i < segments.Length; i++)
            {
                Vector3 segmentInitialLocalPosition = segmentInitialLocalPositions[i];
                segment_move = Mathf.Clamp(segment_move + (moveInput * segmentSpeed * Time.deltaTime), minSegment, maxSegment);
                Vector3 currentLocalPosition = SetAxisValue(segmentInitialLocalPosition, segmentAxis, GetAxisValue(segmentInitialLocalPosition, segmentAxis) + segment_move);
                segments[i].transform.localPosition = currentLocalPosition;
            }
        }
    }

    private void OnDestroy()
    {
        Arm_moving_stretching.action.performed -= arm_moving_actions;
    }

    private float GetAxisValue(Vector3 vector, Axis axis)
    {
        return axis switch
        {
            Axis.X => vector.x,
            Axis.Y => vector.y,
            Axis.Z => vector.z,
            _ => 0f,
        };
    }

    private Vector3 SetAxisValue(Vector3 vector, Axis axis, float value)
    {
        switch (axis)
        {
            case Axis.X: vector.x = value; break;
            case Axis.Y: vector.y = value; break;
            case Axis.Z: vector.z = value; break;
        }
        return vector;
    }
}
