using UnityEngine;

public class PaddleVisuals : MonoBehaviour
{
    [SerializeField] private float tiltMultiplier = 1.6f;
    [SerializeField] private float maxTiltAngle = 18f;
    [SerializeField] private float returnSmoothTime = 12f;
    [SerializeField] private float strokeRecovery = 9f;

    private float targetTiltZ;
    private float strokeAngleX;
    private float strokePunchZ;
    private Quaternion baseRotation;
    private Vector3 basePosition;

    private void Awake()
    {
        baseRotation = transform.localRotation;
        basePosition = transform.localPosition;
    }

    public void SetVelocity(float velocityX)
    {
        targetTiltZ = Mathf.Clamp(-velocityX * tiltMultiplier, -maxTiltAngle, maxTiltAngle);
    }

    // Positive angle closes the face over the ball (topspin), negative opens it (chop).
    public void PlayStroke(float faceAngleX, float punchZ)
    {
        strokeAngleX = faceAngleX;
        strokePunchZ = punchZ;
    }

    private void Update()
    {
        float decay = 1f - Mathf.Exp(-strokeRecovery * Time.deltaTime);
        strokeAngleX = Mathf.Lerp(strokeAngleX, 0f, decay);
        strokePunchZ = Mathf.Lerp(strokePunchZ, 0f, decay);

        Quaternion targetRot = baseRotation * Quaternion.Euler(strokeAngleX, 0f, targetTiltZ);
        transform.localRotation = Quaternion.Lerp(transform.localRotation, targetRot, Time.deltaTime * returnSmoothTime);
        transform.localPosition = basePosition + new Vector3(0f, 0f, strokePunchZ);
        targetTiltZ = Mathf.MoveTowards(targetTiltZ, 0f, Time.deltaTime * returnSmoothTime * 2.5f);
    }
}
