using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerPaddleController : MonoBehaviour
{
    [Header("1. Movement Workspace")]
    [SerializeField] private float minX = -2.6f;
    [SerializeField] private float maxX = 2.6f;
    [SerializeField] private float minY = 0.25f;
    [SerializeField] private float maxY = 1.40f;
    [SerializeField] private float baseZ = -7.6f;

    [Header("2. Physical Damped Movement")]
    [SerializeField] private float smoothTime = 0.040f;
    [SerializeField] private float maxPaddleSpeed = 16.0f;
    [SerializeField] private float strokeForwardDist = 0.65f;
    [SerializeField] private float strokeSpeed = 16.0f;
    [SerializeField] private float returnSpeed = 8.5f;

    [Header("3. Swipe Reading")]
    [Tooltip("Smoothing window (s) for the swipe velocity that decides spin at contact.")]
    [SerializeField] private float swipeSmoothing = 0.06f;

    [Header("4. References")]
    [SerializeField] private PaddleVisuals visuals;
    [SerializeField] private Transform ballTransform;

    private Rigidbody rb;
    private Camera mainCam;
    private Vector3 targetPos;
    private Vector3 physicalVelocity;
    private Vector3 linearVelocity;
    private Vector2 swipeVelocity;
    private float strokeTimer = 0f;
    private float strokeIntensity = 1f;
    private float depthBias = 0f;
    private Vector3 prevPointerPos;

    public Vector3 CurrentVelocity => linearVelocity;
    public bool IsStrokingForward => strokeTimer > 0.02f || linearVelocity.z > 0.8f;
    // Pointer velocity in screen-heights per second: +y = upward flick (topspin), -y = chop, x = sidespin.
    public Vector2 SwipeVelocity => swipeVelocity;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        mainCam = Camera.main;

        targetPos = new Vector3(0.50f, 0.55f, baseZ);
        transform.position = targetPos;
        transform.rotation = Quaternion.Euler(-6f, 0f, 0f);
        prevPointerPos = Input.mousePosition;
    }

    private void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        Vector3 pointerPos = Input.mousePosition;
        if (Input.touchCount > 0)
        {
            pointerPos = Input.GetTouch(0).position;
            // A new touch lands wherever the finger goes down; don't read that jump as a flick.
            if (Input.GetTouch(0).phase == TouchPhase.Began) prevPointerPos = pointerPos;
        }

        float normX = Mathf.Clamp((pointerPos.x / (float)Screen.width - 0.5f) * 2.2f, -1f, 1f);
        float normY = Mathf.Clamp01(pointerPos.y / (float)Screen.height);

        targetPos.x = Mathf.Clamp(normX * maxX, minX, maxX);
        targetPos.y = Mathf.Lerp(minY, maxY, normY * 1.25f);

        // A fast upward flick, tap, click or Space starts a forward stroke; scroll nudges depth.
        float deltaY = pointerPos.y - prevPointerPos.y;
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
        Vector2 rawSwipe = (Vector2)(pointerPos - prevPointerPos) / (Screen.height * dt);
        swipeVelocity = Vector2.Lerp(swipeVelocity, rawSwipe, 1f - Mathf.Exp(-dt / swipeSmoothing));
        prevPointerPos = pointerPos;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.05f)
        {
            depthBias = Mathf.Clamp(depthBias + scroll * 0.20f, -0.20f, 0.50f);
        }

        bool pointerDown = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        bool onHud = GameManager.Instance != null && GameManager.Instance.IsHudTap(pointerPos);
        bool clickOrTap = (pointerDown && !onHud) || Input.GetKeyDown(KeyCode.Space);
        bool fastFlick = deltaY > 15f;

        if (fastFlick || clickOrTap)
        {
            strokeTimer = 0.18f;
            strokeIntensity = fastFlick ? Mathf.Clamp(deltaY / 22f, 1.0f, 2.5f) : 1.4f;
        }

        if (strokeTimer > 0f)
        {
            strokeTimer -= Time.deltaTime;
            float targetZ = baseZ + (strokeForwardDist * strokeIntensity) + depthBias;
            targetPos.z = Mathf.MoveTowards(targetPos.z, targetZ, strokeSpeed * strokeIntensity * Time.deltaTime);
        }
        else
        {
            depthBias = Mathf.MoveTowards(depthBias, 0f, 2.0f * Time.deltaTime);
            targetPos.z = Mathf.MoveTowards(targetPos.z, baseZ + depthBias, returnSpeed * Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        Vector3 currentPos = rb.position;
        Vector3 newPos = Vector3.SmoothDamp(currentPos, targetPos, ref physicalVelocity, smoothTime, maxPaddleSpeed, dt);
        linearVelocity = (newPos - currentPos) / dt;
        rb.MovePosition(newPos);

        // Blade opens slightly at rest and banks into lateral movement.
        float tiltZ = Mathf.Clamp(-linearVelocity.x * 1.4f, -14f, 14f);
        float tiltX = (strokeTimer > 0f) ? 3f : -6f;
        Quaternion targetRot = Quaternion.Euler(tiltX, 0f, tiltZ);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, 18f * dt));

        if (visuals != null) visuals.SetVelocity(linearVelocity.x);
    }

    public void PlayStrokeVisual(float topspin, float power)
    {
        if (visuals != null) visuals.PlayStroke(topspin >= 0f ? 22f * topspin : 30f * topspin, 0.25f * power);
    }

    public void SetBallTransform(Transform b)
    {
        ballTransform = b;
    }
}
