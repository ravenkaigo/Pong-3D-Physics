using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    public enum Hitter { None, Player, AI }

    [Header("1. Flight")]
    [SerializeField] private float serveSpeed = 7.6f;
    [SerializeField] private float maxSpeed = 17.0f;
    [SerializeField] private float gravityScale = 0.38f;
    [SerializeField] private float tableBounceEfficiency = 0.88f;
    [SerializeField] private float ballRadius = 0.19f;

    [Header("2. Spin (Magnus)")]
    [Tooltip("Acceleration per (spin x speed). At 0.018 a full topspin drive drops ~1.6x faster than gravity alone.")]
    [SerializeField] private float magnusStrength = 0.018f;
    [SerializeField] private float maxSpin = 18f;
    [SerializeField] private float spinDecayPerSecond = 3.0f;
    [Tooltip("Quadratic air drag. 0.012 lets a smash leave near 18 m/s and arrive near 15.")]
    [SerializeField] private float airDrag = 0.012f;
    [Tooltip("Lateral m/s added off the table at full sidespin.")]
    [SerializeField] private float bounceSideKick = 1.3f;

    [Header("3. Player Shot Shaping")]
    [Tooltip("Downward swipe speed (screen heights/s) at contact that turns the stroke into a chop.")]
    [SerializeField] private float chopSwipeThreshold = 0.8f;
    [Tooltip("Contact height at or above which a hard swing becomes a flat kill.")]
    [SerializeField] private float smashKillHeight = 0.95f;
    [Tooltip("Contact height below which a hard swing sails long.")]
    [SerializeField] private float lowBallHeight = 0.65f;
    [SerializeField] private float lowBallOvershoot = 3.0f;
    [Tooltip("Rally length at which shots reach their flattest, fastest arcs.")]
    [SerializeField] private int rallyForFullHeat = 12;

    [Header("4. Moments")]
    [Tooltip("Bounce this close to a table edge (m) skids low and kicks outward.")]
    [SerializeField] private float edgeBand = 0.12f;
    [Tooltip("Ball centre this far above the net cord (m) still pops over after clipping it.")]
    [SerializeField] private float cordPopHeight = 0.04f;

    [Header("5. Reach")]
    [SerializeField] private float playerReach = 1.15f;
    [SerializeField] private float aiReach = 0.80f;

    [Header("6. Serve Setup")]
    [SerializeField] private Vector3 servePaddleOffset = new Vector3(0.20f, 0.22f, 0.60f);

    [Header("7. References")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform aiTransform;
    [SerializeField] private BallVisualFX visualFX;

    private Rigidbody rb;
    private PlayerPaddleController playerCtrl;
    private AIPaddleController aiCtrl;
    private bool inPlay;
    private bool isAwaitingPlayerServe;
    private Vector3 hoverOrigin;
    private float lastHitTime = -1f;
    private float stuckTimer = 0f;
    private float simDt = 0.02f;

    private Vector3 ballSpin = Vector3.zero;

    // Tones are generated at runtime, so there are no audio assets.
    private AudioSource audioSource;
    private AudioClip paddleHitSound;
    private AudioClip tableBounceSound;
    private AudioClip netHitSound;

    private Hitter lastHitter = Hitter.None;
    private bool isServeShot = false;
    private int bouncesOnPlayerSide = 0;
    private int bouncesOnAISide = 0;
    private bool netTouchedOnCurrentShot = false;

    public bool HasBouncedOnPlayerSide => bouncesOnPlayerSide >= 1;
    public bool HasBouncedOnAISide => bouncesOnAISide >= 1;
    public bool IsServeShot => isServeShot;
    public Hitter LastHitter => lastHitter;
    public int ShotId { get; private set; }
    public float SpinAmount01 => Mathf.Clamp01(ballSpin.magnitude / maxSpin);
    public float Heat01 => RallyHeat();

    public BallPhysicsParams PhysicsParams => new BallPhysicsParams
    {
        gravity = Mathf.Abs(Physics.gravity.y) * gravityScale,
        magnus = magnusStrength,
        drag = airDrag,
        maxSpin = maxSpin,
        spinDecay = spinDecayPerSecond,
        bounceEfficiency = tableBounceEfficiency,
        sideKick = bounceSideKick,
        radius = ballRadius,
        maxSpeed = maxSpeed
    };

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = false;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Captured before any slow motion shrinks fixedDeltaTime, so predictions use one step size.
        simDt = Time.fixedDeltaTime;

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
        if (visualFX != null)
        {
            visualFX.UpdateVelocityFX(rb.linearVelocity.magnitude, maxSpeed);
            visualFX.SetHeat(inPlay ? RallyHeat() : 0f);
        }

        // Visible rotation follows the real spin axis so topspin, chop and curve read differently.
        if (inPlay && ballSpin.sqrMagnitude > 0.25f)
        {
            transform.Rotate(ballSpin.normalized, ballSpin.magnitude * 20f * Time.deltaTime, Space.World);
        }

        if (isAwaitingPlayerServe)
        {
            Vector3 anchorPos = (playerTransform != null) ? (playerTransform.position + servePaddleOffset) : hoverOrigin;
            float hoverY = Mathf.Sin(Time.time * 5f) * 0.03f;
            transform.position = anchorPos + new Vector3(0f, hoverY, 0f);
            rb.linearVelocity = Vector3.zero;

            bool strokeActive = playerCtrl != null && playerCtrl.IsStrokingForward;
            bool pointerDown = Input.GetMouseButtonDown(0)
                            || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
            Vector2 pointer = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
            bool onHud = GameManager.Instance != null && GameManager.Instance.IsHudTap(pointer);
            bool tapped = (pointerDown && !onHud) || Input.GetKeyDown(KeyCode.Space);

            if (strokeActive || tapped)
            {
                ExecutePlayerServeHit();
            }
            return;
        }

        // Proximity sweep catches fast balls that would tunnel past the paddle collider in one frame.
        if (inPlay && Time.time - lastHitTime > 0.12f)
        {
            Vector3 bPos = transform.position;
            Vector3 bVel = rb.linearVelocity;

            if (bVel.z < 0f && playerTransform != null)
            {
                Vector3 pPos = playerTransform.position;
                if (bPos.z <= pPos.z + 0.45f && bPos.z >= pPos.z - 0.35f
                    && Mathf.Abs(bPos.x - pPos.x) < playerReach && Mathf.Abs(bPos.y - pPos.y) < 1.05f)
                {
                    HandlePaddleContact(Hitter.Player);
                    return;
                }
            }

            if (bVel.z > 0f && aiTransform != null)
            {
                Vector3 aPos = aiTransform.position;
                if (bPos.z >= aPos.z - 0.45f && bPos.z <= aPos.z + 0.35f
                    && Mathf.Abs(bPos.x - aPos.x) < aiReach && Mathf.Abs(bPos.y - aPos.y) < 0.9f)
                {
                    HandlePaddleContact(Hitter.AI);
                    return;
                }
            }
        }

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
            BallPhysicsParams p = PhysicsParams;
            Vector3 vel = rb.linearVelocity;
            rb.AddForce(BallPhysics.Acceleration(p, vel, ballSpin), ForceMode.Acceleration);
            ballSpin = BallPhysics.DecaySpin(p, vel, ballSpin, Time.fixedDeltaTime);
        }
    }

    public void PreparePlayerServe(Vector3 servePos)
    {
        inPlay = false;
        isAwaitingPlayerServe = true;
        hoverOrigin = servePos;
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
        Vector2 swipe = playerCtrl != null ? playerCtrl.SwipeVelocity : Vector2.zero;
        float forwardSpeed = Mathf.Max(0f, paddleVel.z);
        float speedFactor = Mathf.Clamp01(forwardSpeed / 5.0f);

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
        float vy = -1.1f; // Angles down onto player half at z ~ -5.7m
        float vx = Mathf.Clamp(paddleVel.x * 0.20f, -1.2f, 1.2f);

        rb.linearVelocity = new Vector3(vx, vy, vz);
        // Sideways swipe on the serve = sidespin serve that kicks off the opponent's half.
        float side = Mathf.Clamp(swipe.x / 2.0f, -1f, 1f);
        ballSpin = new Vector3(Mathf.Lerp(5f, 12f, speedFactor), side * maxSpin * 0.6f, 0f);
        if (visualFX != null) visualFX.SetSpinTint(Mathf.Lerp(0.3f, 0.7f, speedFactor));

        TriggerImpactFeedback(transform.position, Vector3.back, Mathf.Lerp(0.04f, 0.12f, speedFactor));
        PlayHit(Mathf.Lerp(0.95f, 1.20f, speedFactor));
    }

    public void LaunchAIServe(Vector3 startPos, Vector3 targetDir)
    {
        isAwaitingPlayerServe = false;
        rb.isKinematic = false;
        transform.position = startPos;
        inPlay = true;
        lastHitTime = Time.time;

        ResetShotState(Hitter.AI, isServe: true);

        float vz = -serveSpeed;
        float vy = -1.1f;
        float vx = Random.Range(-0.35f, 0.35f);

        rb.linearVelocity = new Vector3(vx, vy, vz);

        // Better AI mixes in sidespin serves the player has to read off the bounce.
        float skill = aiCtrl != null ? aiCtrl.Skill : 0.5f;
        float side = Random.value < skill * 0.7f ? Random.Range(-1f, 1f) : 0f;
        ballSpin = new Vector3(-6f, side * maxSpin * 0.6f, 0f);
        if (visualFX != null) visualFX.SetSpinTint(0.35f);

        TriggerImpactFeedback(startPos, Vector3.forward, 0.05f);
        PlayHit(1.0f);
    }

    public void ResetBall(Vector3 resetPosition)
    {
        inPlay = false;
        isAwaitingPlayerServe = false;
        ballSpin = Vector3.zero;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        transform.position = resetPosition;
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
        ShotId++;
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
            if (Time.time - lastHitTime > 0.12f) HandlePaddleContact(Hitter.Player);
        }
        else if (collision.gameObject.CompareTag("AI"))
        {
            if (Time.time - lastHitTime > 0.12f) HandlePaddleContact(Hitter.AI);
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

    // The net is a trigger, so the cord outcome is decided from how high the ball meets it:
    // clipped on its underside it pops up and dribbles over, hit lower it dies on the hitter's side.
    private void HandleNetTouch(Vector3 hitPoint)
    {
        if (netTouchedOnCurrentShot) return;
        netTouchedOnCurrentShot = true;

        Vector3 vel = rb.linearVelocity;
        float travel = vel.z >= 0f ? 1f : -1f;
        float aboveCord = transform.position.y - BallPhysics.NetTopY;
        bool popsOver = aboveCord > cordPopHeight || (aboveCord > -cordPopHeight && Random.value < 0.5f);

        if (popsOver)
        {
            vel.z = travel * Mathf.Clamp(Mathf.Abs(vel.z) * 0.45f, 2.5f, 5f);
            vel.y = Mathf.Max(vel.y, 1.6f);
        }
        else
        {
            vel.z = -travel * Mathf.Abs(vel.z) * 0.12f;
            vel.y = Mathf.Min(vel.y, 0.5f);
            transform.position = new Vector3(transform.position.x, transform.position.y, -travel * 0.25f);
        }
        vel.x *= 0.6f;
        rb.linearVelocity = vel;
        ballSpin *= 0.3f;

        if (GameManager.Instance != null) GameManager.Instance.ShowCallout("NET!");
        if (HitStopManager.Instance != null) HitStopManager.Instance.PlayMoment(0f, 0.35f, 0.3f);
        TriggerImpactFeedback(hitPoint, Vector3.up, 0.06f);
        if (audioSource != null && netHitSound != null) audioSource.PlayOneShot(netHitSound);
    }

    // A ball that reaches a paddle without bouncing on that side first is judged by where it was going:
    // heading off the table = the sender's error, so it is never "rescued" by the paddle standing there.
    private void HandlePaddleContact(Hitter receiver)
    {
        if (!inPlay || isAwaitingPlayerServe) return;

        bool bounced = receiver == Hitter.Player ? HasBouncedOnPlayerSide : HasBouncedOnAISide;
        if (!bounced)
        {
            BallPhysicsParams p = PhysicsParams;
            bool landsIn = BallPhysics.SimulateToLanding(p, rb.position, rb.linearVelocity, ballSpin,
                                                         simDt, out Vector3 landing, out _);
            bool landsOnReceiverSide = receiver == Hitter.Player ? landing.z < 0f : landing.z > 0f;
            if (!landsIn || !landsOnReceiverSide)
            {
                inPlay = false;
                if (GameManager.Instance != null) GameManager.Instance.OnPointScored(receiver == Hitter.Player, receiver == Hitter.Player ? "CPU hit it long" : "You hit it long");
                return;
            }
        }

        if (receiver == Hitter.Player) ExecutePlayerHitLogic();
        else ExecuteAIHitLogic();
    }

    private float RallyHeat()
    {
        int rally = GameManager.Instance != null ? GameManager.Instance.CurrentRallyCount : 0;
        return Mathf.Clamp01(rally / (float)Mathf.Max(1, rallyForFullHeat));
    }

    private void ExecutePlayerHitLogic()
    {
        // Forgiving volley rule: only a ball taken well before the player's end is a fault.
        if (bouncesOnPlayerSide == 0 && transform.position.z >= -4.5f && transform.position.y > 0.40f)
        {
            inPlay = false;
            if (GameManager.Instance != null) GameManager.Instance.OnPointScored(false, "Volley before the bounce");
            return;
        }

        float heat = RallyHeat();
        lastHitTime = Time.time;
        ResetShotState(Hitter.Player, isServe: false);
        if (GameManager.Instance != null) GameManager.Instance.IncrementRally();

        if (playerCtrl == null && playerTransform != null)
            playerCtrl = playerTransform.GetComponent<PlayerPaddleController>();

        Vector3 paddleVel = playerCtrl != null ? playerCtrl.CurrentVelocity : Vector3.zero;
        Vector2 swipe = playerCtrl != null ? playerCtrl.SwipeVelocity : Vector2.zero;
        bool stroking = playerCtrl != null && playerCtrl.IsStrokingForward;

        float power = Mathf.Clamp01(Mathf.Max(0f, paddleVel.z) / 6.0f);
        if (stroking && power < 0.35f) power = 0.45f;

        Vector3 paddlePos = playerTransform != null ? playerTransform.position : new Vector3(0f, 0.55f, -7.6f);
        Vector3 contact = new Vector3(
            Mathf.Clamp(transform.position.x, paddlePos.x - 0.48f, paddlePos.x + 0.48f),
            Mathf.Clamp(transform.position.y, paddlePos.y - 0.48f, paddlePos.y + 0.48f),
            paddlePos.z + 0.19f
        );
        transform.position = contact;

        // Swipe direction at contact decides the spin:
        //   up / forward flick -> topspin drive, down -> chop (backspin), sideways -> curve.
        bool chop = swipe.y < -chopSwipeThreshold;
        float topspin = chop
            ? -Mathf.Clamp01(0.5f + (-swipe.y - chopSwipeThreshold) / 2.0f)
            : Mathf.Lerp(0.15f, 1.0f, power);
        float side = Mathf.Clamp(swipe.x / 2.0f, -1f, 1f);

        float hitFactor = Mathf.Clamp((contact.x - paddlePos.x) / 0.7f, -1f, 1f);
        float targetX = Mathf.Clamp(hitFactor * 1.8f + paddleVel.x * 0.20f, -2.3f, 2.3f);

        float targetZ = Mathf.Lerp(4.0f, 6.4f, power);
        float apex = Mathf.Lerp(1.35f, 1.00f, power) - 0.15f * heat;
        string callout = null;
        float trauma = Mathf.Lerp(0.04f, 0.12f, power);
        bool kill = false;

        if (chop)
        {
            // Short, floating, low-bouncing ball that drags the opponent forward.
            targetZ = 3.4f;
            apex = 1.10f;
            power = 0.2f;
            callout = "CHOP";
            trauma = 0.04f;
        }
        else if (power > 0.65f)
        {
            if (contact.y >= smashKillHeight)
            {
                // Ball met at the top of its bounce: flat, deep, heavy topspin.
                kill = true;
                targetZ = 6.8f;
                apex = 0.92f - 0.07f * heat;
                topspin = 1f;
                callout = "SMASH!";
                trauma = 0.2f;
            }
            else if (contact.y < lowBallHeight)
            {
                // Hard swing at a low ball has nothing to dip into: it sails long.
                float lowness = Mathf.Clamp01((lowBallHeight - contact.y) / 0.3f);
                float overSwing = (power - 0.65f) / 0.35f;
                targetZ += lowness * overSwing * lowBallOvershoot;
                if (targetZ > BallPhysics.TableHalfLength) callout = "TOO LOW!";
            }
        }
        if (callout == null && Mathf.Abs(side) > 0.5f) callout = "CURVE";

        Vector3 spin = new Vector3(topspin * maxSpin, side * maxSpin * 0.6f, 0f);
        Vector3 launchVel = BallPhysics.SolveLaunch(PhysicsParams, contact, new Vector3(targetX, 0f, targetZ), apex, ref spin, simDt);
        rb.linearVelocity = launchVel;
        ballSpin = spin;

        if (visualFX != null) visualFX.SetSpinTint(topspin);
        if (callout != null && GameManager.Instance != null) GameManager.Instance.ShowCallout(callout);

        TriggerImpactFeedback(contact, Vector3.back, trauma);
        if (kill && HitStopManager.Instance != null) HitStopManager.Instance.PlayMoment(0.05f, 0.2f, 0.35f);
        if (kill && CameraShake.Instance != null) CameraShake.Instance.PunchFov();
        if (visualFX != null) visualFX.PlayHitBurst(contact, Vector3.forward, kill ? 1f : power * 0.6f);
        if (playerCtrl != null) playerCtrl.PlayStrokeVisual(topspin, power);
        PlayHit(Mathf.Lerp(0.92f, 1.25f, power));
    }

    private void ExecuteAIHitLogic()
    {
        BallPhysicsParams p = PhysicsParams;
        float incomingTop = BallPhysics.Topspin01(p, rb.linearVelocity, ballSpin);
        float heat = RallyHeat();

        lastHitTime = Time.time;
        ResetShotState(Hitter.AI, isServe: false);
        if (GameManager.Instance != null) GameManager.Instance.IncrementRally();

        float skill = aiCtrl != null ? aiCtrl.Skill : 0.5f;
        float pressure = Mathf.Clamp01((aiCtrl != null ? aiCtrl.Pressure : 0.3f) + 0.2f * heat);

        Vector3 aiPos = aiTransform != null ? aiTransform.position : new Vector3(0f, 0.65f, 7.6f);
        Vector3 contact = new Vector3(
            Mathf.Clamp(transform.position.x, aiPos.x - 0.48f, aiPos.x + 0.48f),
            Mathf.Clamp(transform.position.y, aiPos.y - 0.48f, aiPos.y + 0.48f),
            aiPos.z - 0.19f
        );
        transform.position = contact;

        // Placement: a skilled AI aims away from where the player is standing.
        float playerX = playerTransform != null ? playerTransform.position.x : 0f;
        float targetX = Random.value < 0.35f + 0.45f * skill
            ? -Mathf.Sign(playerX + Random.Range(-0.3f, 0.3f)) * Random.Range(0.9f, 2.1f)
            : Random.Range(-1.8f, 1.8f);
        float targetZ = Random.Range(-5.4f, -3.6f) - 0.6f * heat;
        float apex = 1.25f - 0.2f * heat;
        float topspin = Random.Range(0.3f, 0.7f);
        float side = Random.value < 0.15f + 0.3f * skill ? Random.Range(-1f, 1f) : 0f;
        float trauma = 0.05f;

        if (incomingTop < -0.3f)
        {
            // Has to lift a chop: loopy and slow, sits up for the player to attack.
            apex = 1.45f;
            topspin = 0.35f;
            targetZ = -4.2f;
            side = 0f;
        }
        else if (contact.y >= smashKillHeight && Random.value < 0.25f + 0.6f * skill)
        {
            apex = 0.95f;
            topspin = 1f;
            targetZ = -6.4f;
            trauma = 0.14f;
            if (GameManager.Instance != null) GameManager.Instance.ShowCallout("AI SMASH");
        }
        else if (Random.value < 0.18f)
        {
            apex = 1.10f;
            topspin = -0.8f;
            targetZ = -3.2f;
        }

        // Errors come from pressure: fast, spinny, wide balls make the AI miss long or wide.
        float errorChance = Mathf.Clamp01(pressure * (1.1f - skill) * 0.6f);
        if (Random.value < errorChance)
        {
            if (Random.value < 0.5f) targetZ = -(BallPhysics.TableHalfLength + Random.Range(0.6f, 1.2f));
            else targetX = Mathf.Sign(targetX) * (BallPhysics.TableHalfWidth + Random.Range(0.4f, 0.8f));
        }

        // Travel is -z, so topspin is negative spin.x.
        Vector3 spin = new Vector3(-topspin * maxSpin, side * maxSpin * 0.6f, 0f);
        Vector3 launchVel = BallPhysics.SolveLaunch(p, contact, new Vector3(targetX, 0f, targetZ), apex, ref spin, simDt);
        rb.linearVelocity = launchVel;
        ballSpin = spin;

        if (visualFX != null)
        {
            visualFX.SetSpinTint(topspin);
            visualFX.PlayHitBurst(contact, Vector3.back, trauma > 0.1f ? 1f : 0.4f);
        }
        if (aiCtrl != null) aiCtrl.PlayStrokeVisual(topspin);
        TriggerImpactFeedback(contact, Vector3.forward, trauma);
        PlayHit(1.0f);
    }

    // Where the AI should meet the ball: after it bounces on the AI half, at a comfortable height,
    // no further forward than forwardLimitZ. False if the ball is going out or will die before reach.
    public bool PredictAIIntercept(float strikeZ, float forwardLimitZ, float backLimitZ, out Vector3 point, out float time)
    {
        BallPhysicsParams p = PhysicsParams;
        Vector3 pos = rb.position;
        Vector3 vel = rb.linearVelocity;
        Vector3 spin = ballSpin;
        bool serve = isServeShot;
        int aiBounces = bouncesOnAISide;
        float dt = simDt;
        point = pos;
        time = 0f;
        if (!inPlay || vel.z <= 0f) return false;

        for (float t = 0f; t < 3f; t += dt)
        {
            vel += BallPhysics.Acceleration(p, vel, spin) * dt;
            pos += vel * dt;
            spin = BallPhysics.DecaySpin(p, vel, spin, dt);

            if (pos.y <= p.radius && vel.y < 0f)
            {
                if (!BallPhysics.IsOverTable(pos)) return false;
                if (pos.z > 0f)
                {
                    aiBounces++;
                    if (aiBounces >= 2) return false;
                }
                vel = BallPhysics.Bounce(p, vel, spin, serve);
                if (pos.z > 0f) serve = false;
                spin *= BallPhysics.BounceSpinRetention;
                pos.y = p.radius;
            }

            if (aiBounces >= 1)
            {
                bool comfortable = pos.z >= strikeZ && (pos.y >= 0.45f || pos.z >= backLimitZ - 0.3f);
                bool mustTakeEarly = vel.y < 0f && pos.y < 0.40f && pos.z >= forwardLimitZ;
                if (comfortable || mustTakeEarly)
                {
                    point = pos;
                    time = t;
                    return true;
                }
            }
            if (pos.z > 10f || pos.y < -1f) return false;
        }
        return false;
    }

    private void HandleTableBounce(ContactPoint contact)
    {
        bool isPlayerHalf = contact.point.z < 0f;

        Vector3 vel = BallPhysics.Bounce(PhysicsParams, rb.linearVelocity, ballSpin, isServeShot);
        ballSpin *= BallPhysics.BounceSpinRetention;

        // Edge balls stay out of BallPhysics.Bounce on purpose: the AI's prediction misses them, as a player would.
        float sideGap = BallPhysics.TableHalfWidth - Mathf.Abs(contact.point.x);
        float endGap = BallPhysics.TableHalfLength - Mathf.Abs(contact.point.z);
        bool edge = !isServeShot && Mathf.Min(sideGap, endGap) <= edgeBand;
        if (edge)
        {
            vel.y *= 0.45f;
            if (sideGap <= edgeBand) vel.x += Mathf.Sign(contact.point.x) * 1.4f;
            else vel.z *= 1.1f;
            if (GameManager.Instance != null) GameManager.Instance.ShowCallout("EDGE!");
        }
        rb.linearVelocity = vel;

        if (visualFX != null) visualFX.MarkBounce(contact.point);
        TriggerImpactFeedback(contact.point, contact.normal, edge ? 0.08f : 0.02f);
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
                    GameManager.Instance.OnPointScored(false, "Serve bounced twice on your side");
                }
                else if (lastHitter == Hitter.AI)
                {
                    if (bouncesOnAISide == 0)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(true, "CPU serve missed its own side");
                    }
                    else if (bouncesOnPlayerSide == 1 && netTouchedOnCurrentShot)
                    {
                        inPlay = false;
                        GameManager.Instance.OnServeLet();
                    }
                    else if (bouncesOnPlayerSide > 1)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(false, "Bounced twice on your side");
                    }
                    else isServeShot = false;
                }
            }
            else
            {
                if (lastHitter == Hitter.Player)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(false, netTouchedOnCurrentShot ? "Into the net" : "Hit your own side");
                }
                else if (bouncesOnPlayerSide > 1)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(false, "Bounced twice on your side");
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
                    GameManager.Instance.OnPointScored(true, "CPU serve bounced twice");
                }
                else if (lastHitter == Hitter.Player)
                {
                    if (bouncesOnPlayerSide == 0)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(false, "Serve has to bounce on your side first");
                    }
                    else if (bouncesOnAISide == 1 && netTouchedOnCurrentShot)
                    {
                        inPlay = false;
                        GameManager.Instance.OnServeLet();
                    }
                    else if (bouncesOnAISide > 1)
                    {
                        inPlay = false;
                        GameManager.Instance.OnPointScored(true, "Bounced twice on the CPU side");
                    }
                    else isServeShot = false;
                }
            }
            else
            {
                if (lastHitter == Hitter.AI)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(true, netTouchedOnCurrentShot ? "CPU hit the net" : "CPU hit its own side");
                }
                else if (bouncesOnAISide > 1)
                {
                    inPlay = false;
                    GameManager.Instance.OnPointScored(true, "Bounced twice on the CPU side");
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
            GameManager.Instance.OnPointScored(bouncesOnAISide >= 1 && transform.position.z > 0f, "Dead ball at the net");
        else
            GameManager.Instance.OnPointScored(!(bouncesOnPlayerSide >= 1 && transform.position.z < 0f), "Dead ball at the net");
    }

    private void HandleOutOfBoundsPoint()
    {
        inPlay = false;
        ballSpin = Vector3.zero;
        if (GameManager.Instance == null) return;

        if (lastHitter == Hitter.Player)
            GameManager.Instance.OnPointScored(bouncesOnAISide >= 1, bouncesOnAISide >= 1 ? "CPU missed it" : "Out");
        else
            GameManager.Instance.OnPointScored(!(bouncesOnPlayerSide >= 1), bouncesOnPlayerSide >= 1 ? "You missed it" : "CPU hit it out");
    }

    private void PlayHit(float pitch)
    {
        if (audioSource == null || paddleHitSound == null) return;
        audioSource.pitch = pitch * (1f + 0.12f * RallyHeat());
        audioSource.PlayOneShot(paddleHitSound);
    }

    private void TriggerImpactFeedback(Vector3 pos, Vector3 normal, float trauma)
    {
        if (visualFX != null) visualFX.PlayImpactSquash(normal, rb.linearVelocity.magnitude);
        if (CameraShake.Instance != null) CameraShake.Instance.AddTrauma(trauma);
    }
}
