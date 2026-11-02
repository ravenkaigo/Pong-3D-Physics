using UnityEngine;

public class PaddleVisuals : MonoBehaviour
{
    [SerializeField] private float tiltMultiplier = 1.6f;
    [SerializeField] private float maxTiltAngle = 18f;
    [SerializeField] private float returnSmoothTime = 12f;

    private float targetTiltZ;

    public void SetVelocity(float velocityX)
    {
        // ponytail: Z-axis lateral tilt reflects wrist inertia; upgrade to full 3D quaternions if topspin brush strokes added.
        targetTiltZ = Mathf.Clamp(-velocityX * tiltMultiplier, -maxTiltAngle, maxTiltAngle);
    }

    private void Update()
    {
        Quaternion targetRot = Quaternion.Euler(0f, 0f, targetTiltZ);
        transform.localRotation = Quaternion.Lerp(transform.localRotation, targetRot, Time.deltaTime * returnSmoothTime);
        targetTiltZ = Mathf.MoveTowards(targetTiltZ, 0f, Time.deltaTime * returnSmoothTime * 2.5f);
    }
}
