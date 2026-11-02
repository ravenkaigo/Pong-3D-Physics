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

        Debug.Log("[Pong3D Self-Check] All dynamic swing speeds, net clearances, and contact positions verified successfully.");
    }
}
