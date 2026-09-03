using UnityEngine;

public class DroneDemoMove : MonoBehaviour
{
    public float radius = 20f;
    public float angularSpeed = 0.2f;
    public float height = 100f;
    public float hoverAmplitude = 1f;
    public float hoverSpeed = 1.5f;

    private Vector3 center;

    void Start()
    {
        center = transform.position;
    }

    void Update()
    {
        float angle = Time.time * angularSpeed;

        float x = center.x + Mathf.Cos(angle) * radius;
        float z = center.z + Mathf.Sin(angle) * radius;
        float y = height + Mathf.Sin(Time.time * hoverSpeed) * hoverAmplitude;

        transform.position = new Vector3(x, y, z);

        Vector3 direction = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle));
        if (direction != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }
    }
}