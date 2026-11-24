using UnityEngine;

// One flight model shared by the live ball, the AI's read of the ball, and the editor self-check,
// so what the AI predicts and what the solver aims for is exactly what the Rigidbody will do.
public struct BallPhysicsParams
{
    public float gravity;          // m/s^2, positive, already scaled
    public float magnus;           // acceleration per (spin * speed)
    public float drag;             // quadratic air drag: a = -drag * |v| * v
    public float maxSpin;
    public float spinDecay;        // spin units lost per second in flight
    public float bounceEfficiency;
    public float sideKick;         // m/s lateral kick off the table at full sidespin
    public float radius;
    public float maxSpeed;
}

public static class BallPhysics
{
    public const float TableHalfLength = 8.0f;
    public const float TableHalfWidth = 2.8f;
    public const float NetTopY = 0.16f;
    public const float BounceSpinRetention = 0.55f;

    // Serves keep a fixed rebound so they always clear the net; spin only adds sideways kick.
    private const float ServeReboundVy = 2.65f;
    private const float ServeMinVz = 7.4f;
    private const float ServeMaxVz = 8.8f;

    public static Vector3 Acceleration(in BallPhysicsParams p, Vector3 vel, Vector3 spin)
    {
        Vector3 a = new Vector3(0f, -p.gravity, 0f);
        if (vel.sqrMagnitude > 1f) a += p.magnus * Vector3.Cross(spin, vel);
        return a - p.drag * vel.magnitude * vel;
    }

    public static Vector3 DecaySpin(in BallPhysicsParams p, Vector3 vel, Vector3 spin, float dt)
    {
        return vel.sqrMagnitude > 1f ? Vector3.MoveTowards(spin, Vector3.zero, p.spinDecay * dt) : spin;
    }

    // +1 = full topspin relative to the direction of travel, -1 = full backspin (chop).
    public static float Topspin01(in BallPhysicsParams p, Vector3 vel, Vector3 spin)
    {
        return Mathf.Clamp(spin.x * Mathf.Sign(vel.z) / p.maxSpin, -1f, 1f);
    }

    public static float Side01(in BallPhysicsParams p, Vector3 spin)
    {
        return Mathf.Clamp(spin.y / p.maxSpin, -1f, 1f);
    }

    public static bool IsOverTable(Vector3 pos)
    {
        return Mathf.Abs(pos.x) <= TableHalfWidth && Mathf.Abs(pos.z) <= TableHalfLength;
    }

    // Topspin skids through low and fast; backspin checks up short and low; sidespin kicks sideways.
    public static Vector3 Bounce(in BallPhysicsParams p, Vector3 vel, Vector3 spin, bool serve)
    {
        float travel = vel.z >= 0f ? 1f : -1f;
        float top = Topspin01(p, vel, spin);
        float side = Side01(p, spin);

        if (serve)
        {
            vel.y = ServeReboundVy;
            vel.z = travel * Mathf.Clamp(Mathf.Abs(vel.z), ServeMinVz, ServeMaxVz);
        }
        else
        {
            float vyScale = top >= 0f ? 1f - 0.22f * top : 1f + 0.35f * top;
            float vzScale = top >= 0f ? 1f + 0.20f * top : 1f + 0.30f * top;
            vel.y = Mathf.Clamp(Mathf.Abs(vel.y) * p.bounceEfficiency * vyScale, 1.6f, 3.6f);
            vel.z = travel * Mathf.Clamp(Mathf.Abs(vel.z) * vzScale, 4.0f, p.maxSpeed);
        }

        vel.x = Mathf.Clamp(vel.x * 0.92f + side * travel * p.sideKick, -3.4f, 3.4f);
        return vel;
    }

    // Analytic guess with Magnus, then corrected by simulating the real integrator, so the ball lands on target.
    // Shots faster than maxSpeed get a higher arc instead.
    public static Vector3 SolveLaunch(in BallPhysicsParams p, Vector3 start, Vector3 target, float apexY, ref Vector3 spin, float dt)
    {
        Vector3 v = SolveAnalytic(p, start, target, apexY, ref spin);
        for (int i = 0; i < 5 && Mathf.Abs(v.z) > p.maxSpeed; i++)
        {
            apexY += 0.1f;
            v = SolveAnalytic(p, start, target, apexY, ref spin);
        }

        Vector3 aim = target;
        for (int i = 0; i < 3; i++)
        {
            SimulateToLanding(p, start, v, spin, dt, out Vector3 landing, out _);
            float ex = target.x - landing.x;
            float ez = target.z - landing.z;
            if (ex * ex + ez * ez < 0.01f) break;
            aim.x += ex;
            aim.z += ez;
            v = SolveAnalytic(p, start, aim, apexY, ref spin);
        }
        return v;
    }

    private static Vector3 SolveAnalytic(in BallPhysicsParams p, Vector3 start, Vector3 target, float apexY, ref Vector3 spin)
    {
        float travel = target.z >= start.z ? 1f : -1f;
        float apex = Mathf.Max(apexY, start.y + 0.15f, 0.85f);
        float rise = apex - start.y;
        float fall = Mathf.Max(0.05f, apex - p.radius);
        float dz = target.z - start.z;

        float t = FlightTime(rise, fall, p.gravity);
        float vz = dz / t;

        // Cap topspin dip at +60% gravity and backspin lift at -45% gravity.
        float perSpin = p.magnus * Mathf.Max(1f, Mathf.Abs(vz));
        float topspin = spin.x * travel;
        topspin = Mathf.Clamp(topspin, -0.45f * p.gravity / perSpin, 0.60f * p.gravity / perSpin);
        spin.x = topspin * travel;

        float g = p.gravity;
        float avgFactor = 1f;
        for (int i = 0; i < 3; i++)
        {
            float mag = spin.magnitude;
            avgFactor = mag > 0.01f ? Mathf.Max(0f, 1f - p.spinDecay * t * 0.5f / mag) : 0f;
            g = p.gravity + p.magnus * topspin * avgFactor * Mathf.Abs(vz);
            t = FlightTime(rise, fall, g);
            vz = dz / t;
        }

        float vy = Mathf.Sqrt(2f * g * rise);
        float ax = p.magnus * spin.y * avgFactor * vz;
        float vx = Mathf.Clamp((target.x - start.x - 0.5f * ax * t * t) / t, -4.5f, 4.5f);
        return new Vector3(vx, vy, vz);
    }

    private static float FlightTime(float rise, float fall, float g)
    {
        return Mathf.Max(0.2f, Mathf.Sqrt(2f * rise / g) + Mathf.Sqrt(2f * fall / g));
    }

    // Steps the same integrator the Rigidbody uses (semi-implicit Euler) until the ball first
    // touches the table plane. Returns false if it never comes down over the table.
    public static bool SimulateToLanding(in BallPhysicsParams p, Vector3 pos, Vector3 vel, Vector3 spin, float dt,
                                         out Vector3 landing, out float netClearance)
    {
        landing = pos;
        netClearance = float.MaxValue;
        float prevZ = pos.z;
        for (float t = 0f; t < 4f; t += dt)
        {
            vel += Acceleration(p, vel, spin) * dt;
            pos += vel * dt;
            spin = DecaySpin(p, vel, spin, dt);

            if (Mathf.Sign(prevZ) != Mathf.Sign(pos.z))
                netClearance = Mathf.Min(netClearance, pos.y - p.radius - NetTopY);
            prevZ = pos.z;

            if (pos.y <= p.radius && vel.y < 0f)
            {
                landing = pos;
                return IsOverTable(pos);
            }
            if (pos.y < -1f) break;
        }
        landing = pos;
        return false;
    }
}
