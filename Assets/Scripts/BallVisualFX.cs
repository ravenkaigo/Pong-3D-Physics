using System.Collections;
using UnityEngine;

// The ball is the one focal point of the arena: it carries the only accent colour,
// heats up over a rally, and leaves marks that make depth readable on a small screen.
public class BallVisualFX : MonoBehaviour
{
    [SerializeField] private TrailRenderer trail;
    [SerializeField] private Renderer meshRenderer;
    [SerializeField] private Transform shadowDisk;
    [SerializeField] private float squashDuration = 0.06f;
    [SerializeField] private float maxSquashAmount = 0.06f;

    [Header("Heat")]
    [SerializeField] private Color heatEmission = new Color(1f, 0.45f, 0.08f);
    [SerializeField] private float maxEmission = 1.6f;

    [Header("Spin tint (hot = topspin, cold = chop)")]
    [SerializeField] private Color flatTrail = new Color(1f, 0.86f, 0.55f);
    [SerializeField] private Color topspinTrail = new Color(1f, 0.50f, 0.12f);
    [SerializeField] private Color chopTrail = new Color(0.35f, 0.85f, 1f);

    private const int MarkerCount = 4;
    private const float MarkerLife = 0.4f;

    private Vector3 initialScale;
    private Coroutine squashRoutine;
    private Material ballMaterial;
    private float heat;
    private float targetHeat;
    private Color trailColor;
    private Color lastTrailKey = Color.clear;
    private readonly Gradient trailGradient = new Gradient();
    private readonly GradientColorKey[] colorKeys = new GradientColorKey[2];
    private readonly GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];

    private ParticleSystem burst;
    private Transform[] markers;
    private Material[] markerMats;
    private float[] markerAge;
    private int nextMarker;

    private void Awake()
    {
        initialScale = transform.localScale;
        trailColor = flatTrail;

        if (meshRenderer != null)
        {
            ballMaterial = meshRenderer.material;
            ballMaterial.EnableKeyword("_EMISSION");
        }

        Material spriteMat = new Material(Shader.Find("Sprites/Default"));

        if (shadowDisk == null)
        {
            GameObject shadowObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadowObj.name = "FX_BallShadow_Runtime";
            Destroy(shadowObj.GetComponent<Collider>());
            shadowDisk = shadowObj.transform;
            Material shadowMat = new Material(spriteMat) { mainTexture = MakeDiscTexture(64, 0f, 1f) };
            shadowMat.color = new Color(0.01f, 0.02f, 0.04f, 0.7f);
            shadowObj.GetComponent<Renderer>().sharedMaterial = shadowMat;
        }

        BuildBounceMarkers(spriteMat);
        BuildBurst(spriteMat);
    }

    private void BuildBounceMarkers(Material spriteMat)
    {
        Texture2D ring = MakeDiscTexture(64, 0.72f, 1f);
        markers = new Transform[MarkerCount];
        markerMats = new Material[MarkerCount];
        markerAge = new float[MarkerCount];
        for (int i = 0; i < MarkerCount; i++)
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "FX_BounceMarker";
            Destroy(q.GetComponent<Collider>());
            markerMats[i] = new Material(spriteMat) { mainTexture = ring };
            q.GetComponent<Renderer>().sharedMaterial = markerMats[i];
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.SetActive(false);
            markers[i] = q.transform;
            markerAge[i] = MarkerLife;
        }
    }

    private void BuildBurst(Material spriteMat)
    {
        GameObject go = new GameObject("FX_HitBurst");
        burst = go.AddComponent<ParticleSystem>();
        burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = burst.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.gravityModifier = 0.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;
        var emission = burst.emission;
        emission.enabled = false;
        var shape = burst.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.05f;
        var col = burst.colorOverLifetime;
        col.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = fade;
        Material dot = new Material(spriteMat) { mainTexture = MakeDiscTexture(32, 0f, 1f) };
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dot;
    }

    // Soft-edged disc (inner = 0) or ring (inner > 0) with alpha falloff, generated so no texture asset is needed.
    private static Texture2D MakeDiscTexture(int size, float inner, float outer)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
            float a = inner > 0f
                ? Mathf.Clamp01(1f - Mathf.Abs(r - (inner + outer) * 0.5f) / ((outer - inner) * 0.5f))
                : Mathf.Clamp01((1f - r) * 2.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    public void SetShadowDisk(Transform s)
    {
        shadowDisk = s;
    }

    public void SetHeat(float heat01)
    {
        targetHeat = Mathf.Clamp01(heat01);
    }

    public void SetSpinTint(float topspin)
    {
        trailColor = topspin >= 0f
            ? Color.Lerp(flatTrail, topspinTrail, topspin)
            : Color.Lerp(flatTrail, chopTrail, -topspin);
    }

    public void MarkBounce(Vector3 point)
    {
        int i = nextMarker;
        nextMarker = (nextMarker + 1) % MarkerCount;
        markers[i].position = new Vector3(point.x, 0.015f, point.z);
        markerAge[i] = 0f;
        markers[i].gameObject.SetActive(true);
        EmitBurst(point, Vector3.up, 4, new Color(0.75f, 0.8f, 0.9f), 0.5f);
    }

    public void PlayHitBurst(Vector3 point, Vector3 direction, float intensity01)
    {
        intensity01 = Mathf.Clamp01(intensity01);
        Color c = Color.Lerp(new Color(1f, 0.95f, 0.85f), topspinTrail, intensity01 * 0.6f + heat * 0.4f);
        EmitBurst(point, direction, Mathf.RoundToInt(Mathf.Lerp(5f, 30f, intensity01)), c, Mathf.Lerp(0.6f, 1.6f, intensity01));
    }

    private void EmitBurst(Vector3 point, Vector3 direction, int count, Color color, float speedScale)
    {
        if (burst == null) return;
        burst.transform.SetPositionAndRotation(point, Quaternion.LookRotation(direction));
        var main = burst.main;
        main.startColor = color;
        main.startSpeedMultiplier = speedScale;
        burst.Emit(count);
    }

    public void UpdateVelocityFX(float currentSpeed, float maxSpeed)
    {
        float ratio = Mathf.Clamp01(currentSpeed / Mathf.Max(maxSpeed, 1f));
        if (trail != null) trail.startWidth = Mathf.Lerp(0.07f, 0.12f, ratio) * (1f + 0.5f * heat);
    }

    private void Update()
    {
        heat = Mathf.MoveTowards(heat, targetHeat, Time.deltaTime * 0.8f);

        if (ballMaterial != null)
            ballMaterial.SetColor("_EmissionColor", heatEmission * (maxEmission * heat * heat));

        UpdateTrail();

        for (int i = 0; i < MarkerCount; i++)
        {
            if (markerAge[i] >= MarkerLife) continue;
            markerAge[i] += Time.deltaTime;
            float k = Mathf.Clamp01(markerAge[i] / MarkerLife);
            float size = Mathf.Lerp(0.3f, 0.85f, 1f - (1f - k) * (1f - k));
            markers[i].localScale = new Vector3(size, size, 1f);
            markerMats[i].color = new Color(0.85f, 0.92f, 1f, 0.7f * (1f - k));
            if (k >= 1f) markers[i].gameObject.SetActive(false);
        }
    }

    // Gradient is reassigned only when it changes; the setter copies and allocates.
    private void UpdateTrail()
    {
        if (trail == null) return;
        trail.time = Mathf.Lerp(0.12f, 0.3f, heat);
        Color c = Color.Lerp(trailColor, topspinTrail, heat * 0.5f);
        c.a = heat;
        if (c == lastTrailKey) return;
        lastTrailKey = c;
        c.a = 1f;
        colorKeys[0] = new GradientColorKey(c, 0f);
        colorKeys[1] = new GradientColorKey(c * 0.8f, 1f);
        alphaKeys[0] = new GradientAlphaKey(Mathf.Lerp(0.45f, 0.8f, heat), 0f);
        alphaKeys[1] = new GradientAlphaKey(0f, 1f);
        trailGradient.SetKeys(colorKeys, alphaKeys);
        trail.colorGradient = trailGradient;
    }

    private void LateUpdate()
    {
        if (shadowDisk == null) return;
        Vector3 bPos = transform.position;
        shadowDisk.position = new Vector3(bPos.x, 0.012f, bPos.z);
        shadowDisk.rotation = Quaternion.Euler(90f, 0f, 0f);

        float h = Mathf.Max(0f, bPos.y);
        float s = Mathf.Lerp(0.42f, 0.2f, h / 2.0f);
        shadowDisk.localScale = new Vector3(s, s, 1f);

        bool onTable = Mathf.Abs(bPos.z) <= 8.05f && Mathf.Abs(bPos.x) <= 2.85f && h < 3.5f;
        shadowDisk.gameObject.SetActive(onTable);
    }

    public void PlayImpactSquash(Vector3 normal, float force)
    {
        if (squashRoutine != null) StopCoroutine(squashRoutine);
        squashRoutine = StartCoroutine(SquashRoutine(normal.normalized, Mathf.Clamp01(force / 15f)));
    }

    private IEnumerator SquashRoutine(Vector3 normal, float factor)
    {
        float squash = maxSquashAmount * factor;
        Vector3 targetScale = new Vector3(
            1f + (Mathf.Abs(normal.x) > 0.5f ? -squash : squash * 0.5f),
            1f + (Mathf.Abs(normal.y) > 0.5f ? -squash : squash * 0.5f),
            1f + (Mathf.Abs(normal.z) > 0.5f ? -squash : squash * 0.5f)
        );

        float elapsed = 0f;
        while (elapsed < squashDuration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(initialScale, targetScale, elapsed / squashDuration);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < squashDuration * 1.2f)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(targetScale, initialScale, elapsed / (squashDuration * 1.2f));
            yield return null;
        }

        transform.localScale = initialScale;
        squashRoutine = null;
    }
}
