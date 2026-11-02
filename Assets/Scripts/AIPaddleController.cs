using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AIPaddleController : MonoBehaviour
{
    [Header("3D Bounds")]
    [SerializeField] private float minX = -2.5f, maxX = 2.5f;
    [SerializeField] private float minY = 0.25f, maxY = 1.35f;
    [SerializeField] private float minZ = 6.8f, maxZ = 8.0f;

    [Header("Behavior")]
    [SerializeField] private Transform ball;
    [SerializeField] private float speed = 9.5f;
    [SerializeField] private float trackingSmoothness = 0.055f;
    [SerializeField] private PaddleVisuals visuals;

    private Rigidbody rb;
    private Rigidbody ballRb;
    private BallController ballCtrl;
    private Vector3 currentVelocity;
    private Vector3 lastPos;
    public Vector3 CurrentVelocity { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        lastPos = transform.position;
        if (ball != null)
        {
            ballRb = ball.GetComponent<Rigidbody>();
            ballCtrl = ball.GetComponent<BallController>();
        }
    }

    private void FixedUpdate()
    {
        if (ball == null) return;
        if (ballRb == null) ballRb = ball.GetComponent<Rigidbody>();
        if (ballCtrl == null) ballCtrl = ball.GetComponent<BallController>();

        Vector3 targetPos = new Vector3(0f, 0.60f, 7.6f);
        bool ballHeadingToAI = ballRb != null && ballRb.linearVelocity.z > 0.1f;

        if (ballHeadingToAI || ball.position.z > -1.0f)
        {
            // Predictive lateral tracking
            targetPos.x = Mathf.Clamp(ball.position.x, minX, maxX);
            targetPos.y = Mathf.Clamp(ball.position.y, minY, maxY);

            // Step forward slightly to meet ball after bounce
            bool readyToStrike = (ballCtrl != null && ballCtrl.HasBouncedOnAISide) || ball.position.z > 6.0f;
            targetPos.z = readyToStrike ? 7.2f : 7.6f;
        }

        Vector3 newPos = Vector3.SmoothDamp(rb.position, targetPos, ref currentVelocity, trackingSmoothness, speed, Time.fixedDeltaTime);
        newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
        newPos.y = Mathf.Clamp(newPos.y, minY, maxY);
        newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);

        rb.MovePosition(newPos);

        CurrentVelocity = (newPos - lastPos) / Time.fixedDeltaTime;
        lastPos = newPos;

        if (visuals != null) visuals.SetVelocity(CurrentVelocity.x);
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
}
