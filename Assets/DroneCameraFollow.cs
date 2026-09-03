using UnityEngine;

public class DroneCameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("Camera Offset")]
    public Vector3 localOffset = new Vector3(0f, 18f, -45f);

    [Header("Look At Offset")]
    public Vector3 lookAtOffset = new Vector3(0f, 5f, 0f);

    [Header("Smooth")]
    public float positionSmooth = 3f;
    public float rotationSmooth = 3f;

    [Header("Stability")]
    public float snapDistance = 1000f;
    public bool disableLegacyMouseEvents = true;

    void Awake()
    {
        if (!disableLegacyMouseEvents)
            return;

        Camera childCamera = GetComponentInChildren<Camera>(true);
        if (childCamera != null)
            childCamera.eventMask = 0;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        // 跟著無人機座標系移動
        Vector3 desiredPosition = target.TransformPoint(localOffset);

        float distance = Vector3.Distance(transform.position, desiredPosition);
        if (!IsFinite(desiredPosition) || !IsFinite(transform.position))
            return;

        transform.position = distance > Mathf.Max(1f, snapDistance)
            ? desiredPosition
            : Vector3.Lerp(
                transform.position,
                desiredPosition,
                Mathf.Clamp01(Time.deltaTime * positionSmooth));

        // 看向無人機
        Vector3 lookTarget = target.position + lookAtOffset;
        Vector3 direction = lookTarget - transform.position;

        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(direction, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                desiredRotation,
                Mathf.Clamp01(Time.deltaTime * rotationSmooth)
            );
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
