using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { Serve, Play, ScoreRound, GameOver }

    [Header("Match Settings")]
    [SerializeField] private int targetScore = 11;
    [SerializeField] private float aiServeDelay = 0.65f;
    [SerializeField] private float roundResetDelay = 0.75f;

    [Header("References")]
    [SerializeField] private BallController ball;

    [Header("Serve Positions")]
    [SerializeField] private Vector3 playerServePos = new Vector3(0.30f, 0.55f, -7.0f);
    [SerializeField] private Vector3 aiServePos = new Vector3(0f, 0.70f, 7.0f);

    [Header("Events (UI Hook)")]
    public UnityEvent<int, int> onScoreChanged;
    public UnityEvent<string> onStateChanged;
    public UnityEvent<int> onRallyCountChanged;

    public GameState State { get; private set; }
    public int PlayerScore { get; private set; }
    public int AIScore { get; private set; }
    public int CurrentRallyCount { get; private set; }

    private const float CalloutSeconds = 0.8f;

    private bool currentServerIsPlayer = true;
    private bool initialServerIsPlayer = true;
    private string headline = "";
    private string detail = "";
    private string callout = "";
    private float calloutStart = -10f;

    private HudStyles hud;
    private Rect motionButtonRect;
    private Rect replayButtonRect;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        currentServerIsPlayer = initialServerIsPlayer;
        StartCoroutine(SetupRound(currentServerIsPlayer));
    }

    private void Update()
    {
        if (State == GameState.GameOver && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
            RestartMatch();
    }

    // Taps on HUD buttons must not also swing the paddle or serve.
    public bool IsHudTap(Vector2 screenPos)
    {
        Vector2 gui = new Vector2(screenPos.x, Screen.height - screenPos.y);
        return motionButtonRect.Contains(gui) || (State == GameState.GameOver && replayButtonRect.Contains(gui));
    }

    public void OnPlayerServed()
    {
        if (State != GameState.Serve) return;
        State = GameState.Play;
        CurrentRallyCount = 0;
        SetStatus("", "");
        onRallyCountChanged?.Invoke(CurrentRallyCount);
    }

    public void IncrementRally()
    {
        if (State != GameState.Play) return;
        CurrentRallyCount++;
        onRallyCountChanged?.Invoke(CurrentRallyCount);

        if (CurrentRallyCount == 8) ShowCallout("8 in a row");
        else if (CurrentRallyCount >= 12 && CurrentRallyCount % 4 == 0) ShowCallout(CurrentRallyCount + " in a row");
    }

    public void ShowCallout(string text)
    {
        callout = text;
        calloutStart = Time.unscaledTime;
    }

    public void OnServeLet()
    {
        if (State != GameState.Play && State != GameState.Serve) return;

        State = GameState.ScoreRound;
        SetStatus("Let", "Serve clipped the net. Serve again.");
        StartCoroutine(ReplayServeRoutine());
    }

    public void OnPointScored(bool playerWonPoint, string reason)
    {
        if (State != GameState.Play && State != GameState.Serve) return;

        State = GameState.ScoreRound;
        if (playerWonPoint) PlayerScore++;
        else AIScore++;
        onScoreChanged?.Invoke(PlayerScore, AIScore);

        if (CameraShake.Instance != null) CameraShake.Instance.AddTrauma(0.24f);

        bool playerWonMatch = PlayerScore >= targetScore && PlayerScore - AIScore >= 2;
        bool aiWonMatch = AIScore >= targetScore && AIScore - PlayerScore >= 2;

        if (playerWonMatch || aiWonMatch)
        {
            State = GameState.GameOver;
            SetStatus(playerWonMatch ? "You win" : "CPU wins", $"{PlayerScore} to {AIScore}");
            if (HitStopManager.Instance != null) HitStopManager.Instance.PlayMoment(0.08f, 0.25f, 0.6f);
            return;
        }

        SetStatus(playerWonPoint ? "Your point" : "CPU point", reason);

        int totalPoints = PlayerScore + AIScore;
        bool deuce = PlayerScore >= targetScore - 1 && AIScore >= targetScore - 1;
        currentServerIsPlayer = deuce
            ? (totalPoints % 2 == 0) == initialServerIsPlayer
            : ((totalPoints / 2) % 2 == 0) == initialServerIsPlayer;

        StartCoroutine(SetupRound(currentServerIsPlayer));
    }

    public void RestartMatch()
    {
        StopAllCoroutines();
        PlayerScore = 0;
        AIScore = 0;
        CurrentRallyCount = 0;
        onScoreChanged?.Invoke(0, 0);
        currentServerIsPlayer = initialServerIsPlayer;
        State = GameState.ScoreRound;
        SetStatus("", "");
        if (ball != null) ball.ResetBall(aiServePos);
        StartCoroutine(SetupRound(currentServerIsPlayer));
    }

    private IEnumerator ReplayServeRoutine()
    {
        yield return new WaitForSeconds(0.6f);
        StartCoroutine(SetupRound(currentServerIsPlayer));
    }

    private IEnumerator SetupRound(bool isPlayerServe)
    {
        yield return new WaitForSeconds(roundResetDelay);

        State = GameState.Serve;
        CurrentRallyCount = 0;
        onRallyCountChanged?.Invoke(0);

        string pressure = PressureLine();
        if (isPlayerServe)
        {
            SetStatus("Your serve", pressure ?? "Swipe up or tap to serve. Swipe sideways to add curve.");
            if (ball != null) ball.PreparePlayerServe(playerServePos);
        }
        else
        {
            SetStatus("CPU serves", pressure ?? "");
            if (ball != null) ball.ResetBall(aiServePos);
            yield return new WaitForSeconds(aiServeDelay);
            if (State == GameState.Serve)
            {
                State = GameState.Play;
                SetStatus("", "");
                if (ball != null) ball.LaunchAIServe(aiServePos, Vector3.back);
            }
        }
    }

    private string PressureLine()
    {
        bool deuce = PlayerScore >= targetScore - 1 && AIScore >= targetScore - 1;
        if (deuce && PlayerScore == AIScore) return "Deuce. Two clear points to win.";
        bool playerMatchPoint = PlayerScore >= targetScore - 1 && PlayerScore > AIScore;
        bool aiMatchPoint = AIScore >= targetScore - 1 && AIScore > PlayerScore;
        if (playerMatchPoint) return "Match point for you";
        if (aiMatchPoint) return "Match point for the CPU";
        return null;
    }

    private void SetStatus(string head, string sub)
    {
        headline = head;
        detail = sub;
        onStateChanged?.Invoke(string.IsNullOrEmpty(sub) ? head : $"{head}. {sub}");
    }

    private void OnGUI()
    {
        Rect safe = Screen.safeArea;
        safe.y = Screen.height - safe.yMax;
        if (hud == null) hud = new HudStyles();
        hud.Resize(safe.height);
        float u = safe.height / 100f;
        float dp = (Screen.dpi > 0f ? Screen.dpi : 160f) / 160f;
        float minTap = 48f * dp;

        DrawMotionToggle(safe, u, minTap);

        // Score: the two numbers are the HUD's one focal element; labels sit under them in a muted tone.
        float scoreTop = motionButtonRect.yMax + 1f * u;
        float colW = Mathf.Min(safe.width * 0.22f, 14f * u);
        Rect youCol = new Rect(safe.center.x - colW - 1.5f * u, scoreTop, colW, 7f * u);
        Rect cpuCol = new Rect(safe.center.x + 1.5f * u, scoreTop, colW, 7f * u);
        GUI.Label(youCol, PlayerScore.ToString(), hud.score);
        GUI.Label(cpuCol, AIScore.ToString(), hud.score);
        GUI.Label(new Rect(youCol.x, youCol.yMax, colW, 3f * u), "YOU", hud.label);
        GUI.Label(new Rect(cpuCol.x, cpuCol.yMax, colW, 3f * u), "CPU", hud.label);

        float statusTop = youCol.yMax + 4f * u;
        if (State == GameState.Play && CurrentRallyCount > 1)
        {
            GUI.Label(new Rect(safe.x, statusTop, safe.width, 3.5f * u), $"Rally {CurrentRallyCount}", hud.label);
        }
        else if (State != GameState.GameOver && !string.IsNullOrEmpty(headline))
        {
            GUI.Label(new Rect(safe.x, statusTop, safe.width, 4f * u), headline, hud.headline);
            if (!string.IsNullOrEmpty(detail))
                GUI.Label(new Rect(safe.x + 4f * u, statusTop + 4f * u, safe.width - 8f * u, 6f * u), detail, hud.detail);
        }

        DrawCallout(safe, u);
        if (State == GameState.GameOver) DrawGameOver(safe, u, minTap);
    }

    // A short scale pop so the moment registers, then a fade; static when reduce motion is on.
    private void DrawCallout(Rect safe, float u)
    {
        float age = Time.unscaledTime - calloutStart;
        if (age >= CalloutSeconds || string.IsNullOrEmpty(callout)) return;

        float pop = GameSettings.ReduceMotion ? 1f : Mathf.Lerp(1.35f, 1f, Mathf.Clamp01(age / 0.12f));
        float alpha = Mathf.Clamp01((CalloutSeconds - age) / 0.25f);
        int size = Mathf.RoundToInt(hud.calloutBaseSize * pop);
        hud.callout.fontSize = size;
        Color c = hud.accent;
        c.a = alpha;
        hud.callout.normal.textColor = c;
        GUI.Label(new Rect(safe.x, safe.y + safe.height * 0.3f, safe.width, size * 1.6f), callout, hud.callout);
    }

    private void DrawMotionToggle(Rect safe, float u, float minTap)
    {
        float h = Mathf.Max(minTap, 4f * u);
        float w = Mathf.Max(h * 2.6f, 14f * u);
        motionButtonRect = new Rect(safe.xMax - w - 1.5f * u, safe.y + 1f * u, w, h);
        string label = GameSettings.ReduceMotion ? "Motion: reduced" : "Motion: full";
        if (GUI.Button(motionButtonRect, label, hud.smallButton))
            GameSettings.ReduceMotion = !GameSettings.ReduceMotion;
    }

    private void DrawGameOver(Rect safe, float u, float minTap)
    {
        Rect panel = new Rect(safe.x + 6f * u, safe.y + safe.height * 0.32f, safe.width - 12f * u, 30f * u);
        GUI.Box(panel, GUIContent.none, hud.panel);
        GUI.Label(new Rect(panel.x, panel.y + 3f * u, panel.width, 8f * u), headline, hud.score);
        GUI.Label(new Rect(panel.x, panel.y + 11f * u, panel.width, 4f * u), detail, hud.detail);

        float bh = Mathf.Max(minTap, 7f * u);
        replayButtonRect = new Rect(panel.center.x - 18f * u, panel.yMax - bh - 4f * u, 36f * u, bh);
        if (GUI.Button(replayButtonRect, "Play again", hud.button)) RestartMatch();
    }

    // Styles are built once and resized only when the safe area changes, so OnGUI does not allocate every frame.
    private class HudStyles
    {
        public readonly Color accent = new Color(1f, 0.62f, 0.12f);
        public GUIStyle score, label, headline, detail, callout, button, smallButton, panel;
        public int calloutBaseSize;
        private float lastHeight = -1f;

        public HudStyles()
        {
            Color text = new Color(0.96f, 0.97f, 1f);
            Color muted = new Color(0.72f, 0.76f, 0.84f);
            score = Make(TextAnchor.MiddleCenter, FontStyle.Bold, text);
            label = Make(TextAnchor.MiddleCenter, FontStyle.Bold, muted);
            headline = Make(TextAnchor.MiddleCenter, FontStyle.Bold, text);
            detail = Make(TextAnchor.UpperCenter, FontStyle.Normal, muted);
            detail.wordWrap = true;
            callout = Make(TextAnchor.MiddleCenter, FontStyle.Bold, accent);

            Texture2D panelTex = Solid(new Color(0.03f, 0.04f, 0.07f, 0.92f));
            panel = new GUIStyle { normal = { background = panelTex } };

            button = Make(TextAnchor.MiddleCenter, FontStyle.Bold, new Color(0.06f, 0.05f, 0.03f));
            button.normal.background = Solid(accent);
            button.hover.background = Solid(new Color(1f, 0.72f, 0.3f));
            button.active.background = Solid(new Color(0.85f, 0.5f, 0.08f));
            button.hover.textColor = button.active.textColor = button.normal.textColor;

            smallButton = Make(TextAnchor.MiddleCenter, FontStyle.Normal, muted);
            smallButton.normal.background = Solid(new Color(1f, 1f, 1f, 0.08f));
            smallButton.hover.background = Solid(new Color(1f, 1f, 1f, 0.16f));
            smallButton.active.background = Solid(new Color(1f, 1f, 1f, 0.24f));
            smallButton.hover.textColor = smallButton.active.textColor = text;
        }

        public void Resize(float safeHeight)
        {
            if (Mathf.Approximately(safeHeight, lastHeight)) return;
            lastHeight = safeHeight;
            float u = safeHeight / 100f;
            score.fontSize = Mathf.RoundToInt(6f * u);
            label.fontSize = Mathf.RoundToInt(1.8f * u);
            headline.fontSize = Mathf.RoundToInt(2.8f * u);
            detail.fontSize = Mathf.RoundToInt(2.0f * u);
            calloutBaseSize = Mathf.RoundToInt(5f * u);
            button.fontSize = Mathf.RoundToInt(2.6f * u);
            smallButton.fontSize = Mathf.RoundToInt(1.7f * u);
        }

        private static GUIStyle Make(TextAnchor anchor, FontStyle style, Color color)
        {
            GUIStyle s = new GUIStyle { alignment = anchor, fontStyle = style };
            s.normal.textColor = color;
            return s;
        }

        private static Texture2D Solid(Color c)
        {
            Texture2D t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
