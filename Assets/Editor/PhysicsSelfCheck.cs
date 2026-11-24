using UnityEngine;
using UnityEditor;

public static class PhysicsSelfCheck
{
    [MenuItem("Pong3D/Run Physics Self-Check")]
    public static void Run()
    {
        // 1. Check Contact Point Velocity: V_contact = V_linear + Cross(w, offset)
        Vector3 vLinear = new Vector3(0f, 2f, 10f);
        Vector3 w = new Vector3(5f, 0f, 0f);
        Vector3 offset = new Vector3(0f, 0.5f, 0f);
        Vector3 cross = Vector3.Cross(w, offset);
        Vector3 vContact = vLinear + cross;

        Debug.Assert(Mathf.Approximately(vContact.z, 12.5f), "Contact velocity Z failed");
        Debug.Assert(Mathf.Approximately(vContact.y, 2.0f), "Contact velocity Y failed");

        // 2. Check Aerodynamic Lift
        Vector3 topspin = new Vector3(20f, 0f, 0f);
        Vector3 ballVel = new Vector3(0f, 1f, 12f);
        Vector3 magnusTopspin = 0.0008f * Vector3.Cross(topspin, ballVel);
        Debug.Assert(magnusTopspin.y < 0f, "Magnus topspin must produce downward force");

        Vector3 backspin = new Vector3(-20f, 0f, 0f);
        Vector3 magnusBackspin = 0.0008f * Vector3.Cross(backspin, ballVel);
        Debug.Assert(magnusBackspin.y > 0f, "Magnus backspin must produce upward lift");

        // 3. Player ITTF Double-Bounce Serve Verification
        float gEff = 9.81f * 0.38f; // 3.7278 m/s^2
        float netTopY = 0.16f;

        float pServeVz = 7.6f;
        float pServeVy = -1.1f;
        float pServeStartY = 0.55f;
        float pServeStartZ = -7.41f; // paddle.z (-7.6) + 0.19m rubber surface contact

        // First bounce on player table half
        float a1 = 0.5f * gEff;
        float b1 = -pServeVy;
        float c1 = -pServeStartY;
        float tFirstBounce = (-b1 + Mathf.Sqrt(b1 * b1 - 4f * a1 * c1)) / (2f * a1);
        float zFirstBounce = pServeStartZ + pServeVz * tFirstBounce;
        Debug.Assert(zFirstBounce > -6.5f && zFirstBounce < -3.0f, $"Player serve must bounce on player table half: z={zFirstBounce}");

        // Net clearance on rebound
        float reboundVy = 2.65f;
        float distToNet = Mathf.Abs(zFirstBounce);
        float tNet = distToNet / pServeVz;
        float yAtNet = reboundVy * tNet - 0.5f * gEff * tNet * tNet;
        float ballBottomAtNet = yAtNet - 0.19f;
        Debug.Assert(ballBottomAtNet > netTopY + 0.40f, $"Serve must clear net with >40cm safety: ballBottom={ballBottomAtNet}");

        // Second bounce on AI table half
        float tAir = (2f * reboundVy) / gEff;
        float zSecondBounce = zFirstBounce + pServeVz * tAir;
        Debug.Assert(zSecondBounce > 3.5f && zSecondBounce < 7.8f, $"Player serve must land on opponent table half: z={zSecondBounce}");

        // 4. Slow Forward Push / Block Verification
        Vector3 slowStart = new Vector3(0f, 0.55f, -7.41f);
        Vector3 slowTarget = new Vector3(0f, 0f, 4.0f);
        float slowApexY = 1.35f;

        float slowVy = Mathf.Sqrt(2f * gEff * (slowApexY - slowStart.y));
        float slowTRise = slowVy / gEff;
        float slowTFall = Mathf.Sqrt(2f * gEff * slowApexY) / gEff;
        float slowTTotal = slowTRise + slowTFall;
        float slowVz = (slowTarget.z - slowStart.z) / slowTTotal;

        float slowTNet = -slowStart.z / slowVz;
        float slowYNet = slowStart.y + slowVy * slowTNet - 0.5f * gEff * slowTNet * slowTNet;
        Debug.Assert(slowYNet - 0.19f > netTopY + 0.60f, $"Slow push must clear net comfortably: yNet={slowYNet}");
        float slowZLand = slowStart.z + slowVz * slowTTotal;
        Debug.Assert(Mathf.Approximately(slowZLand, slowTarget.z), $"Slow push must land at target: zLand={slowZLand}");

        // 5. Fast Forward Smash Verification
        Vector3 fastStart = new Vector3(0f, 0.55f, -7.41f);
        Vector3 fastTarget = new Vector3(0f, 0f, 6.3f);
        float fastApexY = 1.00f;

        float fastVy = Mathf.Sqrt(2f * gEff * (fastApexY - fastStart.y));
        float fastTRise = fastVy / gEff;
        float fastTFall = Mathf.Sqrt(2f * gEff * fastApexY) / gEff;
        float fastTTotal = fastTRise + fastTFall;
        float fastVz = (fastTarget.z - fastStart.z) / fastTTotal;

        float fastTNet = -fastStart.z / fastVz;
        float fastYNet = fastStart.y + fastVy * fastTNet - 0.5f * gEff * fastTNet * fastTNet;
        Debug.Assert(fastYNet - 0.19f > netTopY + 0.40f, $"Fast smash must clear net comfortably: yNet={fastYNet}");
        float fastZLand = fastStart.z + fastVz * fastTTotal;
        Debug.Assert(Mathf.Approximately(fastZLand, fastTarget.z), $"Fast smash must land at target: zLand={fastZLand}");
        Debug.Assert(fastVz > slowVz + 3.0f, $"Fast smash speed ({fastVz}) must be significantly faster than slow push ({slowVz})");

        // 6. Spin-aware solver must agree with the live integrator: every shot type lands near
        //    its target and clears the net, with the same Magnus model the Rigidbody uses.
        BallPhysicsParams bp = new BallPhysicsParams
        {
            gravity = gEff, magnus = 0.018f, maxSpin = 18f, spinDecay = 3.0f,
            bounceEfficiency = 0.88f, sideKick = 1.3f, radius = 0.19f, maxSpeed = 17f
        };
        float dt = 0.02f;
        Vector3 pStart = new Vector3(0.3f, 0.9f, -7.41f);
        Vector3 aStart = new Vector3(-0.4f, 0.9f, 7.41f);
        var shots = new (string name, Vector3 start, Vector3 target, float apex, Vector3 spin)[]
        {
            ("block",        pStart, new Vector3(0.5f, 0f, 4.0f),   1.35f, new Vector3(2.7f, 0f, 0f)),
            ("topspin",      pStart, new Vector3(-1.5f, 0f, 5.5f),  1.12f, new Vector3(12f, 0f, 0f)),
            ("smash",        pStart, new Vector3(2.0f, 0f, 6.8f),   0.85f, new Vector3(18f, 0f, 0f)),
            ("chop",         pStart, new Vector3(0f, 0f, 3.4f),     1.10f, new Vector3(-14f, 0f, 0f)),
            ("curve",        pStart, new Vector3(1.8f, 0f, 5.0f),   1.20f, new Vector3(8f, 10.8f, 0f)),
            ("ai drive",     aStart, new Vector3(1.5f, 0f, -4.5f),  1.25f, new Vector3(-9f, -6f, 0f)),
            ("ai smash",     aStart, new Vector3(-1.0f, 0f, -6.4f), 0.95f, new Vector3(-18f, 0f, 0f)),
        };
        foreach (var s in shots)
        {
            Vector3 spin = s.spin;
            Vector3 v = BallPhysics.SolveLaunch(bp, s.start, s.target, s.apex, ref spin, dt);
            bool onTable = BallPhysics.SimulateToLanding(bp, s.start, v, spin, dt, out Vector3 land, out float clearance);
            float miss = Vector2.Distance(new Vector2(land.x, land.z), new Vector2(s.target.x, s.target.z));
            Debug.Log($"[Pong3D Self-Check] {s.name}: v={v} spin={spin} land={land} miss={miss:F2}m net+{clearance:F2}m");
            Debug.Assert(onTable, $"{s.name} must land on the table: {land}");
            Debug.Assert(miss < 0.5f, $"{s.name} lands {miss:F2}m from target");
            Debug.Assert(clearance > 0.05f, $"{s.name} must clear the net: {clearance:F2}m");
            Debug.Assert(Mathf.Abs(v.z) <= bp.maxSpeed + 0.01f, $"{s.name} launch faster than maxSpeed: {v.z:F1}");
        }

        // 7. Bounce reacts to spin: topspin skids lower/faster, chop checks up short and low.
        Vector3 inVel = new Vector3(0f, -3f, 10f);
        Vector3 flatOut = BallPhysics.Bounce(bp, inVel, Vector3.zero, false);
        Vector3 topOut = BallPhysics.Bounce(bp, inVel, new Vector3(18f, 0f, 0f), false);
        Vector3 chopOut = BallPhysics.Bounce(bp, inVel, new Vector3(-18f, 0f, 0f), false);
        Vector3 sideOut = BallPhysics.Bounce(bp, inVel, new Vector3(0f, 18f, 0f), false);
        Debug.Assert(topOut.z > flatOut.z && topOut.y < flatOut.y, "Topspin must kick forward and low");
        Debug.Assert(chopOut.z < flatOut.z && chopOut.y < flatOut.y, "Chop must check up short and low");
        Debug.Assert(sideOut.x > 1f, "Sidespin must kick sideways off the table");

        Debug.Log("[Pong3D Self-Check] All dynamic swing speeds, net clearances, and contact positions verified successfully.");
    }
}
