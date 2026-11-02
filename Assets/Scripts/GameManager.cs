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

    private bool currentServerIsPlayer = true;
    private bool initialServerIsPlayer = true;
    private string statusMessage = "";

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

    public void OnPlayerServed()
    {
        if (State == GameState.Serve)
        {
            State = GameState.Play;
            CurrentRallyCount = 0;
            statusMessage = "RALLY!";
            onRallyCountChanged?.Invoke(CurrentRallyCount);
            onStateChanged?.Invoke(statusMessage);
        }
    }

    public void IncrementRally()
    {
        if (State != GameState.Play) return;
        CurrentRallyCount++;
        onRallyCountChanged?.Invoke(CurrentRallyCount);

        if (CurrentRallyCount == 5) statusMessage = "GREAT RALLY!";
        else if (CurrentRallyCount == 10) statusMessage = "SUPER RALLY! 🔥";
        else if (CurrentRallyCount >= 15 && CurrentRallyCount % 5 == 0) statusMessage = "INSANE RALLY! ⚡";
        onStateChanged?.Invoke(statusMessage);
    }

    public void OnServeLet()
    {
        if (State != GameState.Play && State != GameState.Serve) return;

        State = GameState.ScoreRound;
        statusMessage = "LET - NET REPLAY";
        onStateChanged?.Invoke(statusMessage);

        if (CameraShake.Instance != null) CameraShake.Instance.AddTrauma(0.08f);
        StartCoroutine(ReplayServeRoutine());
    }

    public void OnPointScored(bool playerWonPoint, string reason)
    {
        if (State != GameState.Play && State != GameState.Serve) return;

        State = GameState.ScoreRound;

        if (playerWonPoint)
        {
            PlayerScore++;
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
        else
        {
            AIScore++;
        }

        onScoreChanged?.Invoke(PlayerScore, AIScore);

        statusMessage = playerWonPoint ? $"POINT PLAYER! ({reason})" : $"POINT AI! ({reason})";
        onStateChanged?.Invoke(statusMessage);

        if (CameraShake.Instance != null) CameraShake.Instance.AddTrauma(0.24f);
        if (HitStopManager.Instance != null) HitStopManager.Instance.TriggerHitStop(0.030f);

        bool playerWonMatch = PlayerScore >= targetScore && (PlayerScore - AIScore >= 2);
        bool aiWonMatch = AIScore >= targetScore && (AIScore - PlayerScore >= 2);

        if (playerWonMatch || aiWonMatch)
        {
            State = GameState.GameOver;
            statusMessage = playerWonMatch ? "VICTORY! 🏆" : "DEFEAT! TRY AGAIN";
            onStateChanged?.Invoke(statusMessage);
        }
        else
        {
            int totalPoints = PlayerScore + AIScore;
            if (PlayerScore >= 10 && AIScore >= 10)
            {
                statusMessage = "DEUCE";
                currentServerIsPlayer = (totalPoints % 2 == 0) ? initialServerIsPlayer : !initialServerIsPlayer;
            }
            else
            {
                currentServerIsPlayer = ((totalPoints / 2) % 2 == 0) ? initialServerIsPlayer : !initialServerIsPlayer;
            }

            StartCoroutine(SetupRound(currentServerIsPlayer));
        }
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

        if (isPlayerServe)
        {
            statusMessage = "CLICK / SWIPE TO SERVE";
            onStateChanged?.Invoke(statusMessage);
            if (ball != null) ball.PreparePlayerServe(playerServePos);
        }
        else
        {
            statusMessage = "AI SERVING...";
            onStateChanged?.Invoke(statusMessage);
            if (ball != null) ball.ResetBall(aiServePos);
            yield return new WaitForSeconds(aiServeDelay);
            if (State == GameState.Serve)
            {
                State = GameState.Play;
                if (ball != null) ball.LaunchAIServe(aiServePos, Vector3.back);
            }
        }
    }

    private void OnGUI()
    {
        // Arcade HUD Header (Ping Pong 3D Style)
        int w = Screen.width;
        GUIStyle scoreStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(w / 28, 22, 42),
            fontStyle = FontStyle.Bold
        };
        scoreStyle.normal.textColor = Color.white;

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(w / 42, 14, 24),
            fontStyle = FontStyle.Normal
        };
        subStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);

        GUI.Label(new Rect(0, 16, w, 44), $"PLAYER {PlayerScore}  -  {AIScore} AI", scoreStyle);

        if (CurrentRallyCount > 1 && State == GameState.Play)
        {
            GUI.Label(new Rect(0, 62, w, 32), $"RALLY: {CurrentRallyCount} 🔥", subStyle);
        }
        else if (!string.IsNullOrEmpty(statusMessage))
        {
            GUI.Label(new Rect(0, 62, w, 32), statusMessage, subStyle);
        }
    }
}
