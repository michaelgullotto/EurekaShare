using UnityEngine;

public class SimpleFpsOverlay : MonoBehaviour
{
    [Header("Display")]
    public bool showFps = true;
    public Vector2 offset = new Vector2(12f, 12f);
    public Vector2 boxSize = new Vector2(140f, 36f);
    public int fontSize = 22;

    [Header("Smoothing")]
    public float updateInterval = 0.5f;

    private float timer;
    private int frames;
    private float currentFps;

    private GUIStyle style;
    private Rect rect;

    private void Awake()
    {
        rect = new Rect(offset.x, offset.y, boxSize.x, boxSize.y);

        style = new GUIStyle
        {
            fontSize = fontSize,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Color.white }
        };
    }

    private void Update()
    {
        if (!showFps)
            return;

        frames++;
        timer += Time.unscaledDeltaTime;

        if (timer >= updateInterval)
        {
            currentFps = frames / timer;
            frames = 0;
            timer = 0f;
        }
    }

    private void OnGUI()
    {
        if (!showFps)
            return;

        GUI.Label(rect, $"FPS: {currentFps:F1}", style);
    }
}