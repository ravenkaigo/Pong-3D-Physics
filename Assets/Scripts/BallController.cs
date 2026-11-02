using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    public enum Hitter { None, Player, AI }

    [Header("1. Calibrated Speed & Parabolic Flight")]
    [SerializeField] private float serveSpeed = 7.6f;
    [SerializeField] private float initialRallySpeed = 8.5f;
    [SerializeField] private float maxSpeed = 15.0f;
    [SerializeField] private float speedIncreasePerHit = 1.025f;
    [SerializeField] private float smashMultiplier = 1.25f;
    [SerializeField] private float gravityScale = 0.38f;
    [SerializeField] private float tableBounceEfficiency = 0.88f;

    [Header("2. Aerodynamics & Spin")]
    [SerializeField] private float magnusCoefficient = 0.0008f;
    [SerializeField] private float spinDecayRate = 4.0f;

    [Header("3. Serve Setup")]
    [SerializeField] private Vector3 servePaddleOffset = new Vector3(0.20f, 0.22f, 0.60f);

    [Header("4. References")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform aiTransform;
    [SerializeField] private BallVisualFX visualFX;
    [SerializeField] private ParticleSystem impactSparks;

    private Rigidbody rb;
    private PlayerPaddleController playerCtrl;
    private AIPaddleController aiCtrl;
    private float currentForwardSpeed;
    private bool inPlay;
    private bool isAwaitingPlayerServe;
    private Vector3 hoverOrigin;
    private float lastHitTime = -1f;
    private float stuckTimer = 0f;

    private Vector3 ballSpin = Vector3.zero;

    // Procedural Audio (Zero external asset dependencies)
    private AudioSource audioSource;
    private AudioClip paddleHitSound;
    private AudioClip tableBounceSound;
    private AudioClip netHitSound;

    // Rules & Scoring State
    private Hitter lastHitter = Hitter.None;
    private bool isServeShot = false;
    private int bouncesOnPlayerSide = 0;
    private int bouncesOnAISide = 0;
    private bool netTouchedOnCurrentShot = false;

    public bool HasBouncedOnPlayerSide => bouncesOnPlayerSide >= 1;
    public bool HasBouncedOnAISide => bouncesOnAISide >= 1;
    public bool IsServeShot => isServeShot;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        if (playerTransform != null) playerCtrl = playerTransform.GetComponent<PlayerPaddleController>();
        if (aiTransform != null) aiCtrl = aiTransform.GetComponent<AIPaddleController>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.20f;

        paddleHitSound = GenerateTone("PaddleHit", 820f, 0.045f, 55f);
        tableBounceSound = GenerateTone("TableBounce", 460f, 0.055f, 40f);
        netHitSound = GenerateTone("NetHit", 220f, 0.060f, 32f);
    }

    private AudioClip GenerateTone(string name, float freq, float duration, float decay)
    {
        int sampleRate = 44100;
        int count = (int)(sampleRate * duration);
        float[] samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Exp(-t * decay);
            samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.45f;
        }
        AudioClip clip = AudioClip.Create(name, count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    public void SetPlayerTransform(Transform p)
    {
        playerTransform = p;
        if (playerTransform != null) playerCtrl = playerTransform.GetComponent<PlayerPaddleController>();
    }

    public void SetAITransform(Transform a)
    {
        aiTransform = a;
        if (aiTransform != null) aiCtrl = aiTransform.GetComponent<AIPaddleController>();
    }

    private void Update()
    {
        if (visualFX != null) visualFX.UpdateVelocityFX(rb.linearVelocity.magnitude, maxSpeed);

        if (inPlay && rb.linearVelocity.sqrMagnitude > 0.5f)
        {
            Vector3 spinAxis = Vector3.Cross(Vector3.up, rb.linearVelocity.normalized);
            transform.Rotate(spinAxis, rb.linearVelocity.magnitude * 24f * Time.deltaTime, Space.World);
        }

        // 1. Player Serve Handling: Click, Tap, Space, or Forward Swing
        if (isAwaitingPlayerServe)
        {
            Vector3 anchorPos = (playerTransform != null) ? (playerTransform.position + servePaddleOffset) : hoverOrigin;
            float hoverY = Mathf.Sin(Time.time * 5f) * 0.03f;
            transform.position = anchorPos + new Vector3(0f, hoverY, 0f);
            rb.linearVelocity = Vector3.zero;

            bool strokeActive = playerCtrl != null && (playerCtrl.IsStrokingForward || playerCtrl.CurrentVelocity.z > 0.8f);
            bool tapped = Input.GetMouseButtonDown(0)
                       || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
                       || Input.GetKeyDown(KeyCode.Space);

            if (strokeActive || tapped)
            {
                ExecutePlayerServeHit();
            }
            return;
        }

        // 2. Proximity Hit Sweep (Eliminates frame tunneling on fast returns, aligned close to paddle)
        if (inPlay && Time.time - lastHitTime > 0.12f)
        {
            Vector3 bPos = transform.position;
            Vector3 bVel = rb.linearVelocity;

            // Player strike zone sweep: only triggers when ball is physically close in front of paddle
            if (bVel.z < 0f && playerTransform != null)
            {
                Vector3 pPos = playerTransform.position;
                if (bPos.z <= pPos.z + 0.45f && bPos.z >= pPos.z - 0.35f)
                {
                    float dx = Mathf.Abs(bPos.x - pPos.x);
                    float dy = Mathf.Abs(bPos.y - pPos.y);
                    if (dx < 1.15f && dy < 1.05f && (HasBouncedOnPlayerSide || bPos.z < -7.4f))
                    {
                        ExecutePlayerHitLogic(bPos);
                        return;
                    }
                }
            }

            // AI strike zone sweep
            if (bVel.z > 0f && aiTransform != null)
            {
                Vector3 aPos = aiTransform.position;
                if (bPos.z >= aPos.z - 0.45f && bPos.z <= aPos.z + 0.35f)
                {
                    float dx = Mathf.Abs(bPos.x - aPos.x);
                    float dy = Mathf.Abs(bPos.y - aPos.y);
                    if (dx < 1.15f && dy < 1.05f && (HasBouncedOnAISide || bPos.z > 7.4f))
                    {
                        ExecuteAIHitLogic(bPos);
                        return;
                    }
                }
            }
        }

        // 3. Dead ball & Out-of-bounds Safeguards
        if (inPlay && GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Play)
        {
            if (rb.linearVelocity.magnitude < 0.85f)
            {
                stuckTimer += Time.deltaTime;
                float timeLimit = Mathf.Abs(transform.position.z) < 2.0f ? 0.35f : 0.8f;
                if (stuckTimer >= timeLimit)
                {
                    HandleStuckBallPoint();
                    return;
                }
            }
            else stuckTimer = 0f;

            if (transform.position.y < -0.4f || Mathf.Abs(transform.position.z) > 10.8f || (Mathf.Abs(transform.position.x) > 4.6f && transform.position.y < 0.2f))
            {
                HandleOutOfBoundsPoint();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!rb.isKinematic && (inPlay || !isAwaitingPlayerServe))
        {
            Vector3 gravForce = Physics.gravity * gravityScale;
            rb.AddForce(gravForce, ForceMode.Acceleration);

            if (rb.linearVelocity.sqrMagnitude > 1.0f)
            {
                Vector3 magnusAccel = magnusCoefficient * Vector3.Cross(ballSpin, rb.linearVelocity);
                rb.AddForce(magnusAccel, ForceMode.Acceleration);
                ballSpin = Vector3.MoveTowards(ballSpin, Vector3.zero, spinDecayRate * Time.fixedDeltaTime);
            }
        }
    }

    public void PreparePlayerServe(Vector3 servePos)
    {
        inPlay = false;
        isAwaitingPlayerServe = true;
        hoverOrigin = servePos;
        currentForwardSpeed = initialRallySpeed;
        ballSpin = Vector3.zero;

        ResetShotState(Hitter.Player, isServe: true);

        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        Vector3 anchorPos = (playerTransform != null) ? (playerTransform.position + servePaddleOffset) : hoverOrigin;
        transform.position = anchorPos;
    }

    public void ExecutePlayerServeHit()
    {
        if (!isAwaitingPlayerServe) return;

        isAwaitingPlayerServe = false;
        inPlay = true;
        lastHitTime = Time.time;
        ResetShotState(Hitter.Player, isServe: true);

        if (GameManager.Instance != null) GameManager.Instance.OnPlayerServed();

        Vector3 paddleVel = playerCtrl != null ? playerCtrl.CurrentVelocity : Vector3.zero;
        float forwardSpeed = Mathf.Max(0f, paddleVel.z);
        float speedFactor = Mathf.Clamp01(forwardSpeed / 5.0f);

        // Visual contact alignment: snap flush to front rubber face
        if (playerTransform != null)
        {
            Vector3 pPos = playerTransform.position;
            transform.position = new Vector3(
                Mathf.Clamp(transform.position.x, pPos.x - 0.45f, pPos.x + 0.45f),
                Mathf.Clamp(transform.position.y, pPos.y - 0.45f, pPos.y + 0.45f),
                pPos.z + 0.19f
            );
        }

        float vz = Mathf.Lerp(7.4f, 9.2f, speedFactor);
        float vy = -1.1f; // Angles smoothly down onto player half at z = -4.5m
        float vx = Mathf.Clamp(paddleVel.x * 0.20f, -1.2f, 1.2f);

        rb.linearVelocity = new Vector3(vx, vy, vz);
        ballSpin = new Vector3(Mathf.Lerp(5f, 12f, speedFactor), -paddleVel.x * 1.5f, 0f);

        TriggerImpactFeedback(transform.position, Vector3.back, Mathf.Lerp(0.04f, 0.12f, speedFactor));
        if (audioSource != null && paddleHitSound != null)
        {
            audioSource.pitch = Mathf.Lerp(0.95f, 1.20f, speedFactor);
            audioSource.PlayOneShot(paddleHitSound);
        }
    }

    public void LaunchAIServe(Vector3 startPos, Vector3 targetDir)
    {
        isAwaitingPlayerServe = false;
        rb.isKinematic = false;
        transform.position = startPos;
        currentForwardSpeed = initialRallySpeed;
        ballSpin = Vector3.zero;
        inPlay = true;
        lastHitTime = Time.time;

        ResetShotState(Hitter.AI, isServe: true);

        float vz = -serveSpeed;
        float vy = -1.1f;
        float vx = Random.Range(-0.35f, 0.35f);

        rb.linearVelocity = new Vector3(vx, vy, vz);
        ballSpin = new Vector3(-6f, 0f, 0f);

        TriggerImpactFeedback(startPos, Vector3.forward, 0.05f);
        if (audioSource != null && paddleHitSound != null)
        {
            audioSource.pitch = 1.0f;
            audioSource.PlayOneShot(paddleHitSound);
        }
    }

    public void ResetBall(Vector3 resetPosition)
    {
        inPlay = false;
        isAwaitingPlayerServe = false;
        ballSpin = Vector3.zero;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        transform.position = resetPosition;
        currentForwardSpeed = initialRallySpeed;
        ResetShotState(Hitter.None, isServe: false);
    }

    private void ResetShotState(Hitter hitter, bool isServe)
    {
        lastHitter = hitter;
        isServeShot = isServe;
        bouncesOnPlayerSide = 0;
        bouncesOnAISide = 0;
        netTouchedOnCurrentShot = false;
        stuckTimer = 0f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Net"))
        {
            HandleNetTouch(other.ClosestPoint(transform.position));
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        ContactPoint contact = collision.contacts[0];

        if (collision.gameObject.CompareTag("Player"))
        {
            if (isAwaitingPlayerServe)
            {
                ExecutePlayerServeHit();
                return;
            }

            if (Time.time - lastHitTime > 0.12f)
            {
                ExecutePlayerHitLogic(contact.point);
            }
        }
        else if (collision.gameObject.CompareTag("AI"))
        {
            if (Time.time - lastHitTime > 0.12f)
            {
                ExecuteAIHitLogic(contact.point);
            }
        }
        else if (collision.gameObject.CompareTag("Table"))
        {
            if (contact.normal.y > 0.65f) HandleTableBounce(contact);
            else HandleOutOfBoundsPoint();
        }
        else if (collision.gameObject.CompareTag("Net"))
        {
            HandleNetTouch(contact.point);
        }
        else
        {
            HandleOutOfBoundsPoint();
        }
    }

    private void HandleNetTouch(Vector3 hitPoint)
    {
        netTouchedOnCurrentShot = true;
        Vector3 vel = rb.linearVelocity;
        // Glide gently over the net cord rather than dead-stopping
        vel.z = Mathf.Sign(vel.z) * Mathf.Max(Mathf.Abs(vel.z) * 0.85f, 4.5f);
        vel.y = Mathf.Max(vel.y, 1.8f);
        rb.linearVelocity = vel;

        TriggerImpactFeedback(hitPoint, Vector3.up, 0.04f);
        if (audioSource != null && netHitSound != null) audioSource.PlayOneShot(netHitSound);
    }

    private void ExecutePlayerHitLogic(Vector3 contactPos)
    {
        if (!inPlay || isAwaitingPlayerServe) return;

        // Arcade forgiveness: only fault if ball was struck unreasonably early before crossing table
        if (bouncesOnPlayerSide == 0 && transform.position.z >= -4.5f && transform.position.y > 0.40f)
        {
            inPlay = false;
            if (GameManager.Instance != null) GameManager.Instance.OnPointScored(false, "VOLLEY FAULT");
            return;
        }

        lastHitTime = Time.time;
        ResetShotState(Hitter.Player, isServe: false);
        if (GameManager.Instance != null) GameManager.Instance.IncrementRally();

        if (playerCtrl == null && playerTransform != null)
            playerCtrl = playerTransform.GetComponent<PlayerPaddleController>();

        Vector3 paddleVel = playerCtrl != null ? playerCtrl.CurrentVelocity : Vector3.zero;
        float forwardSwingSpeed = Mathf.Max(0f, paddleVel.z);
        bool stroking = playerCtrl != null && playerCtrl.IsStrokingForward;

        // Dynamic speed factor: forward fast -> smash/fast return, forward slow -> high soft arc
        float speedFactor = Mathf.Clamp01(forwardSwingSpeed / 6.0f);
        if (stroking && speedFactor < 0.35f) speedFactor = 0.45f;

        // Visual contact alignment ("Nempel Mukul"):
        // Snap ball position flush against paddle front rubber (paddle.z + 0.19m)
        Vector3 paddlePos = playerTransform != null ? playerTransform.position : new Vector3(0f, 0.55f, -7.6f);
        Vector3 alignedPos = new Vector3(
            Mathf.Clamp(transform.position.x, paddlePos.x - 0.48f, paddlePos.x + 0.48f),
            Mathf.Clamp(transform.position.y, paddlePos.y - 0.48f, paddlePos.y + 0.48f),
            paddlePos.z + 0.19f
        );
        transform.position = alignedPos;

        // Fast swing pushes deep (z ~ 6.3m) with flatter piercing arc (apex 1.00m)
        // Slow block drops mid-court (z ~ 4.0m) with soft lofty arc (apex 1.35m)
        float targetZ = Mathf.Lerp(4.0f, 5.04f * smashMultiplier, speedFactor);
        float targetApexY = Mathf.Lerp(1.35f, 1.00f, speedFactor);

        float paddleWidth = 1.4f;
        float hitFactor = Mathf.Clamp((alignedPos.x - paddlePos.x) / (paddleWidth * 0.5f), -1f, 1f);
        float targetX = Mathf.Clamp((hitFactor * 1.8f) + (paddleVel.x * 0.20f), -2.2f, 2.2f);

        Vector3 launchVel = CalculateParabolicTrajectory(alignedPos, new Vector3(targetX, 0f, targetZ), targetApexY);
        rb.linearVelocity = launchVel;

        ballSpin = new Vector3(Mathf.Lerp(4f, 16f, speedFactor), -launchVel.x * 1.8f, 0f);

        float hitTrauma = Mathf.Lerp(0.04f, 0.16f, speedFactor);
        TriggerImpactFeedback(alignedPos, Vector3.back, hitTrauma);

        if (speedFactor > 0.65f && HitStopManager.Instance != null)
        {
            HitStopManager.Instance.TriggerHitStop(0.024f);
        }

        if (audioSource != null && paddleHitSound != null)
        {
            audioSource.pitch = Mathf.Lerp(0.92f, 1.25f, speedFactor);
            audioSource.PlayOneShot(paddleHitSound);
        }
    }

    private void ExecuteAIHitLogic(Vector3 contactPos)
    {
        if (!inPlay || isAwaitingPlayerServe) return;

        lastHitTime = Time.time;
        ResetShotState(Hitter.AI, isServe: false);
        if (GameManager.Instance != null) GameManager.Instance.IncrementRally();

        currentForwardSpeed = Mathf.Clamp(currentForwardSpeed * speedIncreasePerHit, initialRallySpeed, maxSpeed);

        // Target landing mid-court on Player side (z in [-5.2m, -3.5m] gives readable reaction arc)
        float targetZ = Random.Range(-5.2f, -3.5f);
        float targetX = Random.Range(-1.8f, 1.8f);

        Vector3 aiPos = aiTransform != null ? aiTransform.position : new Vector3(0f, 0.65f, 7.6f);
        Vector3 alignedPos = new Vector3(
            Mathf.Clamp(transform.position.x, aiPos.x - 0.48f, aiPos.x + 0.48f),
            Mathf.Clamp(transform.position.y, aiPos.y - 0.48f, aiPos.y + 0.48f),
            aiPos.z - 0.19f
        );
        transform.position = alignedPos;

        Vector3 launchVel = CalculateParabolicTrajectory(alignedPos, new Vector3(targetX, 0f, targetZ), 1.25f);
        rb.linearVelocity = launchVel;

        ballSpin = new Vector3(-6f, -launchVel.x * 1.6f, 0f);
        TriggerImpactFeedback(alignedPos, Vector3.forward, 0.05f);
        if (audioSource != null && paddleHitSound != null)
        {
            audioSource.pitch = 1.0f;
            audioSource.PlayOneShot(paddleHitSound);
        }
    }

    private Vector3 CalculateParabolicTrajectory(Vector3 start, Vector3 target, float targetApexY)
    {
        // Guaranteed lofty parabolic arc over net, landing precisely on table surface (y = 0)
        float gEff = Mathf.Abs(Physics.gravity.y) * gravityScale;
        float apexY = Mathf.Max(targetApexY, start.y + 0.15f, 0.85f);

        float vy = Mathf.Sqrt(2f * gEff * (apexY - start.y));
        float tRise = vy / gEff;
        float tFall = Mathf.Sqrt(2f * gEff * apexY) / gEff;
        float tTotal = Mathf.Max(0.2f, tRise + tFall);

        float vz = (target.z - start.z) / tTotal;
        float vx = (target.x - start.x) / tTotal;
        vx = Mathf.Clamp(vx, -3.2f, 3.2f);

        return new Vector3(vx, vy, vz);
    }

    private void HandleTableBounce(ContactPoint contact)
    {
        bool isPlayerHalf = contact.point.z < 0f;

        Vector3 vel = rb.linearVelocity;
        if (isServeShot)
        {
            // Guaranteed net clearance (center y > 0.90m at net) on serve rebound
            vel.y = 2.65f;
            vel.z = Mathf.Sign(vel.z) * Mathf.Clamp(Mathf.Abs(vel.z), 7.4f, 8.8f);
        }
        else
        {
            // High, energetic table rebound
            vel.y = Mathf.Clamp(Mathf.Abs(vel.y) * tableBounceEfficiency, 2.4f, 3.2f);
            vel.z = Mathf.Sign(vel.z) * Mathf.Clamp(Mathf.Abs(vel.z), initialRallySpeed * 0.85f, maxSpeed);
        }
        vel.x = Mathf.Clamp(vel.x * 0.90f + ballSpin.y * 0.015f, -2.8f, 2.8f);
        rb.linearVelocity = vel;

        ballSpin *= 0.55f;
        TriggerImpactFeedback(contact.point, contact.normal, 0.02f);
        if (audioSource != null && tableBounceSound != null) audioSource.PlayOneShot(tableBounceSound);

        if (!inPlay || GameManager.Instance == null) return;

        if (isPlayerHalf)
        {
            bouncesOnPlayerSide++;
            if (isServeShot)
            {
                if (lastHitter == Hitter.Player && bouncesOnPlayerSide > 1)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(false, "SERVE DOUBLE BOUNCE");
                }
                else if (lastHitter == Hitter.AI)
                {
                    if (bouncesOnAISide == 0)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(true, "AI SERVE FAULT");
                    }
                    else if (bouncesOnPlayerSide == 1 && netTouchedOnCurrentShot)
                    {
                        inPlay = false;
                        GameManager.Instance.OnServeLet();
                    }
                    else if (bouncesOnPlayerSide > 1)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(false, "DOUBLE BOUNCE");
                    }
                    else isServeShot = false;
                }
            }
            else
            {
                if (lastHitter == Hitter.Player)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(false, "HIT OWN TABLE");
                }
                else if (bouncesOnPlayerSide > 1)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(false, "DOUBLE BOUNCE");
                }
            }
        }
        else
        {
            bouncesOnAISide++;
            if (isServeShot)
            {
                if (lastHitter == Hitter.AI && bouncesOnAISide > 1)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(true, "AI SERVE DOUBLE BOUNCE");
                }
                else if (lastHitter == Hitter.Player)
                {
                    if (bouncesOnPlayerSide == 0)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(false, "SERVE MISSED TABLE");
                    }
                    else if (bouncesOnAISide == 1 && netTouchedOnCurrentShot)
                    {
                        inPlay = false;
                        GameManager.Instance.OnServeLet();
                    }
                    else if (bouncesOnAISide > 1)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(true, "DOUBLE BOUNCE");
                    }
                    else isServeShot = false;
                }
            }
            else
            {
                if (lastHitter == Hitter.AI)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(true, "HIT OWN TABLE");
                }
                else if (bouncesOnAISide > 1)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(true, "DOUBLE BOUNCE");
                }
            }
        }
    }

    public void HandleGoalZoneTrigger(bool isPlayerGoal)
    {
        if (inPlay && GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Play)
        {
            HandleOutOfBoundsPoint();
        }
    }

    private void HandleStuckBallPoint()
    {
        inPlay = false;
        rb.linearVelocity = Vector3.zero;
        ballSpin = Vector3.zero;
        if (GameManager.Instance == null) return;

        if (lastHitter == Hitter.Player)
            GameManager.Instance.OnPointScored(bouncesOnAISide >= 1 && transform.position.z > 0f, "NET STUCK");
        else
            GameManager.Instance.OnPointScored(!(bouncesOnPlayerSide >= 1 && transform.position.z < 0f), "NET STUCK");
    }

    private void HandleOutOfBoundsPoint()
    {
        inPlay = false;
        ballSpin = Vector3.zero;
        if (GameManager.Instance == null) return;

        if (lastHitter == Hitter.Player)
            GameManager.Instance.OnPointScored(bouncesOnAISide >= 1, "OUT");
        else
            GameManager.Instance.OnPointScored(!(bouncesOnPlayerSide >= 1), "OUT");
    }

    private void TriggerImpactFeedback(Vector3 pos, Vector3 normal, float trauma)
    {
        if (impactSparks != null)
        {
            impactSparks.transform.position = pos;
            impactSparks.transform.rotation = Quaternion.LookRotation(normal);
            impactSparks.Play();
        }

        if (visualFX != null) visualFX.PlayImpactSquash(normal, rb.linearVelocity.magnitude);
        if (CameraShake.Instance != null) CameraShake.Instance.AddTrauma(trauma);
    }
}

