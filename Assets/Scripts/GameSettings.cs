using UnityEngine;

public static class GameSettings
{
    private const string ReduceMotionKey = "Pong3D.ReduceMotion";

    // Turns off camera shake, slow motion and FOV punches for players sensitive to motion.
    public static bool ReduceMotion
    {
        get => PlayerPrefs.GetInt(ReduceMotionKey, 0) == 1;
        set
        {
            PlayerPrefs.SetInt(ReduceMotionKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
