using System;

class Program
{
    static void Main()
    {
        float gEff = 9.81f * 0.38f; // 3.7278
        float y0 = 0.50f;
        float z0 = -7.5f;
        float z1 = 5.0f;
        float yPeak = 1.30f;

        float vy = MathF.Sqrt(2f * gEff * (yPeak - y0));
        float tRise = vy / gEff;
        float tFall = MathF.Sqrt(2f * gEff * yPeak) / gEff;
        float tTotal = tRise + tFall;

        float vz = (z1 - z0) / tTotal;

        // Check height at net (z = 0)
        float tNet = -z0 / vz;
        float yNet = y0 + vy * tNet - 0.5f * gEff * tNet * tNet;

        // Check landing
        float yEnd = y0 + vy * tTotal - 0.5f * gEff * tTotal * tTotal;
        float zEnd = z0 + vz * tTotal;

        // Landing downward velocity
        float vyLand = vy - gEff * tTotal;

        // Bounce apex with e = 0.88
        float vyBounce = MathF.Abs(vyLand) * 0.88f;
        float apexBounce = (vyBounce * vyBounce) / (2f * gEff);

        Console.WriteLine($"vy: {vy:F2} m/s, vz: {vz:F2} m/s");
        Console.WriteLine($"tTotal: {tTotal:F2}s, tNet: {tNet:F2}s");
        Console.WriteLine($"yNet: {yNet:F2}m (net is 0.16m, clearance: {yNet - 0.16f:F2}m)");
        Console.WriteLine($"yEnd: {yEnd:F2}m, zEnd: {zEnd:F2}m");
        Console.WriteLine($"vyLand: {vyLand:F2} m/s, vyBounce: {vyBounce:F2} m/s, apexBounce: {apexBounce:F2}m");
    }
}
