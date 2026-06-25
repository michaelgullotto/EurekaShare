using System.Collections.Generic;
using UnityEngine;

public class Console : MonoBehaviour
{
    struct Log
    {
        public string message;
        public string stackTrace;
        public LogType type;
    }

    public KeyCode desktopToggleKey = KeyCode.BackQuote;

    public bool mobileUseFullScreenTap = false;
    public float mobileTapRegionWidth = 220f;
    public float mobileTapRegionHeight = 220f;

    [Header("Font Size")]
    public int logFontSize = 28;
    public int buttonFontSize = 24;
    public int titleFontSize = 30;

    List<Log> logs = new List<Log>();
    Vector2 scrollPosition;
    bool show;
    bool collapse;

    GUIStyle logStyle;
    GUIStyle buttonStyle;
    GUIStyle toggleStyle;
    GUIStyle windowStyle;

    static readonly Dictionary<LogType, Color> logTypeColors = new Dictionary<LogType, Color>()
    {
        { LogType.Assert, Color.white },
        { LogType.Error, Color.red },
        { LogType.Exception, Color.red },
        { LogType.Log, Color.white },
        { LogType.Warning, Color.yellow },
    };

    const int margin = 20;

    Rect windowRect = new Rect(margin, margin, Screen.width - (margin * 2), Screen.height - (margin * 2));
    Rect titleBarRect = new Rect(0, 0, 10000, 40);
    GUIContent clearLabel = new GUIContent("Clear", "Clear the contents of the console.");
    GUIContent collapseLabel = new GUIContent("Collapse", "Hide repeated messages.");

    void OnEnable()
    {
        Application.logMessageReceivedThreaded += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceivedThreaded -= HandleLog;
    }

    void Update()
    {
        if (ShouldToggleConsole())
        {
            show = !show;
        }
    }

    void InitStyles()
    {
        if (logStyle == null)
        {
            logStyle = new GUIStyle(GUI.skin.label);
            logStyle.fontSize = logFontSize;
            logStyle.wordWrap = true;
        }

        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = buttonFontSize;
            buttonStyle.fixedHeight = 50;
        }

        if (toggleStyle == null)
        {
            toggleStyle = new GUIStyle(GUI.skin.toggle);
            toggleStyle.fontSize = buttonFontSize;
            toggleStyle.fixedHeight = 50;
        }

        if (windowStyle == null)
        {
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.fontSize = titleFontSize;
            windowStyle.padding = new RectOffset(12, 12, 40, 12);
        }
    }

    bool ShouldToggleConsole()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (IsQuestTogglePressed())
            return true;

        if (IsMobileTogglePressed())
            return true;

        return false;
#else
        return Input.GetKeyDown(desktopToggleKey);
#endif
    }

    bool IsMobileTogglePressed()
    {
        if (Input.touchCount <= 0)
            return false;

        Touch touch = Input.GetTouch(0);

        if (touch.phase != TouchPhase.Began)
            return false;

        if (mobileUseFullScreenTap)
            return true;

        float guiY = Screen.height - touch.position.y;

        return touch.position.x <= mobileTapRegionWidth &&
               guiY <= mobileTapRegionHeight;
    }

    bool IsQuestTogglePressed()
    {
        return Input.GetKeyDown(KeyCode.JoystickButton12);
    }

    void OnGUI()
    {
        if (!show)
            return;

        InitStyles();
        windowRect = GUILayout.Window(123456, windowRect, ConsoleWindow, "Console", windowStyle);
    }

    void ConsoleWindow(int windowID)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        for (int i = 0; i < logs.Count; i++)
        {
            var log = logs[i];

            if (collapse)
            {
                var messageSameAsPrevious = i > 0 && log.message == logs[i - 1].message;
                if (messageSameAsPrevious)
                    continue;
            }

            GUI.contentColor = logTypeColors[log.type];
            GUILayout.Label(log.message, logStyle);
        }

        GUILayout.EndScrollView();

        GUI.contentColor = Color.white;

        GUILayout.BeginHorizontal();

        if (GUILayout.Button(clearLabel, buttonStyle))
        {
            logs.Clear();
        }

        collapse = GUILayout.Toggle(collapse, collapseLabel, toggleStyle, GUILayout.ExpandWidth(false));

        GUILayout.EndHorizontal();

        GUI.DragWindow(titleBarRect);
    }

    void HandleLog(string message, string stackTrace, LogType type)
    {
        logs.Add(new Log()
        {
            message = message,
            stackTrace = stackTrace,
            type = type,
        });
    }
}