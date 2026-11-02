using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Dynamic Paddle Tracking & Angling")]
    [SerializeField] private Transform targetPaddle;
    [SerializeField] private float followSharpness = 4.5f;
    [SerializeField] private float lateralTrackRatio = 0.38f;
    [SerializeField] private float maxCamYaw = 4.5f;
    [SerializeField] private float maxCamRoll = 1.2f;

    [Header("Subtle Impact Feedback")]
    [SerializeField] private float maxTranslationShake = 0.035f;
    [SerializeField] private float decayRate = 4.5f;
    [SerializeField] private float noiseFrequency = 22f;

    private float trauma = 0f;
    private Vector3 initialBasePos;
    private Quaternion initialBaseRot;
    private Vector3 currentFollowOffset = Vector3.zero;
    private Quaternion currentFollowRot = Quaternion.identity;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        initialBasePos = new Vector3(0f, 3.50f, -10.8f);
        initialBaseRot = Quaternion.Euler(20f, 0f, 0f);
        transform.position = initialBasePos;
        transform.rotation = initialBaseRot;
        currentFollowRot = initialBaseRot;
    }

    public void SetTargetPaddle(Transform paddle)
    {
        targetPaddle = paddle;
    }

    public void AddTrauma(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount * 0.35f);
    }

    private void LateUpdate()
    {
        // 1. Dynamic lateral movement and angled yaw/roll tracking paddle
        Vector3 desiredOffset = Vector3.zero;
        Quaternion desiredRot = initialBaseRot;

        if (targetPaddle != null)
        {
            float paddleX = targetPaddle.position.x;
            float normX = Mathf.Clamp(paddleX / 2.6f, -1f, 1f);

            desiredOffset = new Vector3(paddleX * lateralTrackRatio, 0f, 0f);
            desiredRot = Quaternion.Euler(20f, normX * maxCamYaw, -normX * maxCamRoll);
        }

        // ponytail: dynamic framing tracks lateral paddle travel and angle; upgrade to multi-camera TV broadcast view if spectator mode added.
        float dt = Time.deltaTime;
        currentFollowOffset = Vector3.Lerp(currentFollowOffset, desiredOffset, followSharpness * dt);
        currentFollowRot = Quaternion.Slerp(currentFollowRot, desiredRot, followSharpness * dt);

        Vector3 camPos = initialBasePos + currentFollowOffset;

        // 2. Subtle impact trauma
        Vector3 shakeOffset = Vector3.zero;
        if (trauma > 0f)
        {
            float shakePower = trauma * trauma;
            float time = Time.time * noiseFrequency;

            float offsetX = (Mathf.PerlinNoise(time, 0f) * 2f - 1f) * maxTranslationShake * shakePower;
            float offsetY = (Mathf.PerlinNoise(0f, time) * 2f - 1f) * maxTranslationShake * shakePower;

            shakeOffset = new Vector3(offsetX, offsetY, 0f);
            trauma = Mathf.Max(0f, trauma - decayRate * dt);
        }

        transform.position = camPos + shakeOffset;
        transform.rotation = currentFollowRot;
    }
}
