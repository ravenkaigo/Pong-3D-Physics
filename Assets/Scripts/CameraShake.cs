using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Paddle Follow")]
    [SerializeField] private Transform targetPaddle;
    [SerializeField] private float followSharpness = 4.5f;
    [SerializeField] private float lateralTrackRatio = 0.38f;
    [SerializeField] private float maxCamYaw = 4.5f;
    [SerializeField] private float maxCamRoll = 1.2f;

    [Header("Impact")]
    [SerializeField] private float maxTranslationShake = 0.035f;
    [SerializeField] private float decayRate = 4.5f;
    [SerializeField] private float noiseFrequency = 22f;
    [Tooltip("Degrees the FOV narrows on a kill shot before springing back.")]
    [SerializeField] private float fovPunch = 4f;
    [SerializeField] private float fovRecovery = 6f;

    private float trauma = 0f;
    private Vector3 initialBasePos;
    private Quaternion initialBaseRot;
    private Vector3 currentFollowOffset = Vector3.zero;
    private Quaternion currentFollowRot = Quaternion.identity;
    private Camera cam;
    private float baseFov;
    private float fovOffset;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        initialBasePos = new Vector3(0f, 3.50f, -10.8f);
        initialBaseRot = Quaternion.Euler(20f, 0f, 0f);
        transform.position = initialBasePos;
        transform.rotation = initialBaseRot;
        currentFollowRot = initialBaseRot;

        cam = GetComponentInChildren<Camera>();
        if (cam != null) baseFov = cam.fieldOfView;
    }

    public void SetTargetPaddle(Transform paddle)
    {
        targetPaddle = paddle;
    }

    public void AddTrauma(float amount)
    {
        if (GameSettings.ReduceMotion) return;
        trauma = Mathf.Clamp01(trauma + amount * 0.35f);
    }

    public void PunchFov()
    {
        if (GameSettings.ReduceMotion) return;
        fovOffset = -fovPunch;
    }

    private void LateUpdate()
    {
        Vector3 desiredOffset = Vector3.zero;
        Quaternion desiredRot = initialBaseRot;

        if (targetPaddle != null)
        {
            float paddleX = targetPaddle.position.x;
            float normX = Mathf.Clamp(paddleX / 2.6f, -1f, 1f);

            desiredOffset = new Vector3(paddleX * lateralTrackRatio, 0f, 0f);
            desiredRot = Quaternion.Euler(20f, normX * maxCamYaw, -normX * maxCamRoll);
        }

        float dt = Time.deltaTime;
        currentFollowOffset = Vector3.Lerp(currentFollowOffset, desiredOffset, followSharpness * dt);
        currentFollowRot = Quaternion.Slerp(currentFollowRot, desiredRot, followSharpness * dt);

        Vector3 shakeOffset = Vector3.zero;
        if (trauma > 0f)
        {
            float shakePower = trauma * trauma;
            float time = Time.time * noiseFrequency;
            shakeOffset = new Vector3(
                (Mathf.PerlinNoise(time, 0f) * 2f - 1f) * maxTranslationShake * shakePower,
                (Mathf.PerlinNoise(0f, time) * 2f - 1f) * maxTranslationShake * shakePower,
                0f);
            trauma = Mathf.Max(0f, trauma - decayRate * dt);
        }

        // Unscaled so the punch still recovers while a slow-motion moment is playing.
        if (cam != null)
        {
            fovOffset = Mathf.Lerp(fovOffset, 0f, 1f - Mathf.Exp(-fovRecovery * Time.unscaledDeltaTime));
            cam.fieldOfView = baseFov + fovOffset;
        }

        transform.position = initialBasePos + currentFollowOffset + shakeOffset;
        transform.rotation = currentFollowRot;
    }
}
