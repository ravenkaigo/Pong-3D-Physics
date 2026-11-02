using System.Collections;
using UnityEngine;

public class BallVisualFX : MonoBehaviour
{
    [SerializeField] private TrailRenderer trail;
    [SerializeField] private Renderer meshRenderer;
    [SerializeField] private Transform shadowDisk;
    [SerializeField] private float squashDuration = 0.06f;
    [SerializeField] private float maxSquashAmount = 0.06f;

    private Vector3 initialScale;
    private Coroutine squashRoutine;

    private void Awake()
    {
        initialScale = transform.localScale;

        // Auto-create runtime ball shadow quad if not assigned in scene
        if (shadowDisk == null)
        {
            GameObject shadowObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadowObj.name = "FX_BallShadow_Runtime";
            Destroy(shadowObj.GetComponent<Collider>());
            shadowDisk = shadowObj.transform;

            Shader spriteShader = Shader.Find("Sprites/Default");
            if (spriteShader == null) spriteShader = Shader.Find("Standard");
            Material shadowMat = new Material(spriteShader);
            shadowMat.color = new Color(0.04f, 0.06f, 0.09f, 0.45f);
            shadowObj.GetComponent<Renderer>().sharedMaterial = shadowMat;
        }
    }

    public void SetShadowDisk(Transform s)
    {
        shadowDisk = s;
    }

    private void LateUpdate()
    {
        // Real-time table drop shadow for instantaneous 3D depth perception
        if (shadowDisk != null)
        {
            Vector3 bPos = transform.position;
            shadowDisk.position = new Vector3(bPos.x, 0.012f, bPos.z);
            shadowDisk.rotation = Quaternion.Euler(90f, 0f, 0f);

            float h = Mathf.Max(0f, bPos.y);
            float s = Mathf.Lerp(0.38f, 0.18f, h / 2.0f);
            shadowDisk.localScale = new Vector3(s, s, 1f);

            bool onTable = Mathf.Abs(bPos.z) <= 8.05f && Mathf.Abs(bPos.x) <= 2.85f && h < 3.5f;
            shadowDisk.gameObject.SetActive(onTable);
        }
    }

    public void PlayImpactSquash(Vector3 normal, float force)
    {
        if (squashRoutine != null) StopCoroutine(squashRoutine);
        squashRoutine = StartCoroutine(SquashRoutine(normal.normalized, Mathf.Clamp01(force / 15f)));
    }

    public void UpdateVelocityFX(float currentSpeed, float maxSpeed)
    {
        float ratio = Mathf.Clamp01(currentSpeed / Mathf.Max(maxSpeed, 1f));
        if (trail != null) trail.startWidth = Mathf.Lerp(0.07f, 0.12f, ratio);
    }

    private IEnumerator SquashRoutine(Vector3 normal, float factor)
    {
        // ponytail: elastic impact squash along impact normal; upgrade to vertex-shader deformation if blade deformation added.
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
