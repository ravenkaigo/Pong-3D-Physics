using System.Collections;
using UnityEngine;

public class HitStopManager : MonoBehaviour
{
    public static HitStopManager Instance { get; private set; }

    private Coroutine momentRoutine;
    private float baseFixedDeltaTime;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        baseFixedDeltaTime = Time.fixedDeltaTime;
    }

    private void OnDisable()
    {
        SetTimeScale(1f);
    }

    // Short freeze, then slow motion easing back to normal speed. A newer moment replaces an older one.
    public void PlayMoment(float freezeSeconds, float slowScale, float slowSeconds)
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (freezeSeconds > 0f) Handheld.Vibrate();
#endif
        if (GameSettings.ReduceMotion) return;
        if (momentRoutine != null) StopCoroutine(momentRoutine);
        momentRoutine = StartCoroutine(MomentRoutine(freezeSeconds, slowScale, slowSeconds));
    }

    private IEnumerator MomentRoutine(float freezeSeconds, float slowScale, float slowSeconds)
    {
        if (freezeSeconds > 0f)
        {
            SetTimeScale(0.05f);
            yield return new WaitForSecondsRealtime(freezeSeconds);
        }

        float t = 0f;
        while (t < slowSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = t / slowSeconds;
            SetTimeScale(Mathf.Lerp(slowScale, 1f, k * k));
            yield return null;
        }
        SetTimeScale(1f);
        momentRoutine = null;
    }

    // Physics steps shrink with time scale so the ball stays smooth in slow motion.
    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        if (baseFixedDeltaTime > 0f) Time.fixedDeltaTime = baseFixedDeltaTime * Mathf.Max(scale, 0.05f);
    }
}
