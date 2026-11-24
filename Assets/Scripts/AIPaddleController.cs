using UnityEngine;

// The AI reads the ball like a player: it reacts after a delay, predicts the intercept with the
// ball's own physics (with a skill-based misread), re-reads after the bounce, and moves with
// limited speed and acceleration. Wide, fast, spinny shots are therefore genuinely hard for it.
[RequireComponent(typeof(Rigidbody))]
public class AIPaddleController : MonoBehaviour
{
    [Header("Workspace")]
    [SerializeField] private float minX = -2.5f, maxX = 2.5f;
    [SerializeField] private float minY = 0.25f, maxY = 1.35f;
    [SerializeField] private float homeZ = 7.6f;
    [SerializeField] private float strikeZ = 7.2f;
    [SerializeField] private float forwardLimitZ = 5.8f;
    [SerializeField] private float backLimitZ = 8.2f;

    [Header("Athleticism")]
    [SerializeField] private float maxMoveSpeed = 5.5f;
    [SerializeField] private float acceleration = 30f;
    [Tooltip("Seconds before the AI starts moving after the player hits (at skill 0.5).")]
    [SerializeField] private float reactionTime = 0.18f;

    [Header("Skill")]
    [Range(0f, 1f)] [SerializeField] private float baseSkill = 0.6f;
    [Tooltip("Skill gained per point the AI is behind (and lost per point ahead) so games stay close.")]
    [SerializeField] private float skillPerPointBehind = 0.04f;
    [Tooltip("Lateral misread in metres at zero skill under full pressure.")]
    [SerializeField] private float maxReadError = 1.4f;

    [Header("References")]
    [SerializeField] private Transform ball;
    [SerializeField] private PaddleVisuals visuals;

    private Rigidbody rb;
    private Rigidbody ballRb;
    private BallController ballCtrl;

    private Vector3 moveVelocity;
    private Vector3 targetPos;
    private int seenShotId = -1;
    private float readTimer = -1f;
    private bool readAfterBounceDone;
    private float misread;

    public Vector3 CurrentVelocity => moveVelocity;
    public float Pressure { get; private set; }

    public float Skill
    {
        get
        {
            GameManager gm = GameManager.Instance;
            int behind = gm != null ? gm.PlayerScore - gm.AIScore : 0;
            return Mathf.Clamp(baseSkill + behind * skillPerPointBehind, 0.25f, 0.95f);
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        targetPos = new Vector3(0f, 0.65f, homeZ);
        SetBall(ball);
    }

    public void SetBall(Transform b)
    {
        ball = b;
        if (ball != null)
        {
            ballRb = ball.GetComponent<Rigidbody>();
            ballCtrl = ball.GetComponent<BallController>();
        }
    }

    private void FixedUpdate()
    {
        if (ballCtrl == null || ballRb == null) return;
        float dt = Time.fixedDeltaTime;

        UpdateReads(dt);
        Move(dt);

        if (visuals != null) visuals.SetVelocity(moveVelocity.x);
    }

    private void UpdateReads(float dt)
    {
        if (ballCtrl.ShotId != seenShotId)
        {
            seenShotId = ballCtrl.ShotId;
            readAfterBounceDone = false;

            if (ballCtrl.LastHitter == BallController.Hitter.Player)
            {
                readTimer = reactionTime * Mathf.Lerp(1.4f, 0.6f, Skill);
                misread = Random.Range(-1f, 1f);
            }
            else
            {
                // Own shot or reset: recover toward the middle, shading to the side the ball is on.
                readTimer = -1f;
                targetPos = new Vector3(Mathf.Clamp(ball.position.x * 0.3f, minX, maxX), 0.65f, homeZ);
            }
        }

        if (readTimer >= 0f)
        {
            readTimer -= dt;
            if (readTimer < 0f) ReadBall();
        }

        // Second look once the bounce reveals the spin kick: much more accurate, but late.
        if (!readAfterBounceDone && ballCtrl.LastHitter == BallController.Hitter.Player && ballCtrl.HasBouncedOnAISide)
        {
            readAfterBounceDone = true;
            readTimer = reactionTime * 0.5f;
            misread *= 0.3f;
        }
    }

    private void ReadBall()
    {
        if (!ballCtrl.PredictAIIntercept(strikeZ, forwardLimitZ, backLimitZ, out Vector3 point, out float timeToPoint))
        {
            // Going out or dying short: don't chase it.
            targetPos = new Vector3(Mathf.Clamp(rb.position.x * 0.5f, minX, maxX), 0.65f, homeZ);
            Pressure = 0f;
            return;
        }

        float lateralGap = Mathf.Abs(point.x - rb.position.x);
        float urgency = lateralGap / Mathf.Max(0.05f, timeToPoint * maxMoveSpeed);
        float speed01 = Mathf.InverseLerp(7f, 16f, ballRb.linearVelocity.magnitude);
        Pressure = Mathf.Clamp01(urgency * 0.6f + ballCtrl.SpinAmount01 * 0.25f + speed01 * 0.3f);

        float error = misread * maxReadError * (1f - Skill * 0.7f) * (0.25f + Pressure);
        targetPos = new Vector3(
            Mathf.Clamp(point.x + error, minX, maxX),
            Mathf.Clamp(point.y, minY, maxY),
            Mathf.Clamp(point.z, forwardLimitZ, backLimitZ));
    }

    private void Move(float dt)
    {
        Vector3 to = targetPos - rb.position;
        Vector3 desired = Vector3.ClampMagnitude(to / 0.12f, maxMoveSpeed);
        moveVelocity = Vector3.MoveTowards(moveVelocity, desired, acceleration * dt);

        Vector3 newPos = rb.position + moveVelocity * dt;
        newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
        newPos.y = Mathf.Clamp(newPos.y, minY, maxY);
        newPos.z = Mathf.Clamp(newPos.z, forwardLimitZ, backLimitZ);
        rb.MovePosition(newPos);
    }
}
