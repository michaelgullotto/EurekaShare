using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ScreenRecorder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] Button recordButton;
    [SerializeField] Image buttonBackground;
    [SerializeField] Image buttonIcon;
    [SerializeField] Sprite iconRecord;
    [SerializeField] Sprite iconStop;

    [Header("Colors")]
    [SerializeField] Color colorReady = new Color(0.18f, 0.65f, 0.18f);
    [SerializeField] Color colorRecording = new Color(0.75f, 0.12f, 0.12f);

    bool isRecording;
    Texture2D captureTexture;
    Process ffmpegProcess;
    Thread writeThread;
    readonly ConcurrentQueue<byte[]> frameQueue = new();
    volatile bool isWriting;
    volatile bool drainAndStop;
    int captureWidth;
    int captureHeight;
    int targetFPS = 60;
    int segmentNumber;
    string sessionTimestamp;

    static string FfmpegPath => Path.Combine(
        Path.GetDirectoryName(Application.dataPath), "ffmpeg.exe");

    string BuildSegmentPath()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EurekaShare");
        Directory.CreateDirectory(folder);
        segmentNumber++;
        return Path.Combine(folder, $"Recording_{sessionTimestamp}_part{segmentNumber}.mp4");
    }

    void Start()
    {
        recordButton.onClick.AddListener(ToggleRecording);
        SetButtonState(false);
    }

    void ToggleRecording()
    {
        if (isRecording) StopRecording();
        else StartRecording();
    }

    void StartRecording()
    {
        if (!File.Exists(FfmpegPath))
        {
            UnityEngine.Debug.LogError($"[REC] ffmpeg.exe not found at {FfmpegPath}");
            return;
        }

        captureWidth = Screen.width;
        captureHeight = Screen.height;
        targetFPS = 60;
        segmentNumber = 0;
        sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        StartFfmpegProcess();
        isRecording = true;
        SetButtonState(true);
        StartCoroutine(CaptureLoop());
        EventSystem.current.SetSelectedGameObject(null);
        UnityEngine.Debug.Log("[REC] Recording started");
    }

    void StartFfmpegProcess()
    {
        string output = BuildSegmentPath();
        string args = $"-y -f rawvideo -pixel_format rgb24 -video_size {captureWidth}x{captureHeight} " +
                      $"-framerate {targetFPS} -i pipe:0 -vf vflip -c:v libx264 -preset veryfast -pix_fmt yuv420p \"{output}\"";

        ffmpegProcess = new Process();
        ffmpegProcess.StartInfo.FileName = FfmpegPath;
        ffmpegProcess.StartInfo.Arguments = args;
        ffmpegProcess.StartInfo.UseShellExecute = false;
        ffmpegProcess.StartInfo.RedirectStandardInput = true;
        ffmpegProcess.StartInfo.CreateNoWindow = true;
        ffmpegProcess.Start();

        isWriting = true;
        drainAndStop = false;
        writeThread = new Thread(WriteLoop) { IsBackground = true };
        writeThread.Start();

        UnityEngine.Debug.Log($"[REC] Segment {segmentNumber} started at {targetFPS}fps: {output}");
    }

    void StopRecording()
    {
        isRecording = false;
        SetButtonState(false);
        EventSystem.current.SetSelectedGameObject(null);
        UnityEngine.Debug.Log("[REC] Stopping...");
    }

    IEnumerator CaptureLoop()
    {
        var eof = new WaitForEndOfFrame();
        captureTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);

        float captureInterval = 1f / targetFPS;
        float nextCaptureTime = 0f;
        float segmentStartTime = Time.realtimeSinceStartup;
        float lastGrowthCheck = Time.realtimeSinceStartup;
        int prevQueueCount = 0;

        while (isRecording)
        {
            yield return eof;
            float now = Time.realtimeSinceStartup;

            // Check queue growth rate every second
            if (now - lastGrowthCheck >= 1f)
            {
                int currentCount = frameQueue.Count;
                int growth = currentCount - prevQueueCount;
                prevQueueCount = currentCount;
                lastGrowthCheck = now;

                if (growth > 15 && targetFPS > 30)
                {
                    UnityEngine.Debug.LogWarning($"[REC] Queue growing at {growth} frames/s, dropping to 30fps and splitting segment");
                    targetFPS = 30;
                    captureInterval = 1f / 30f;
                    yield return StartCoroutine(SplitSegment());
                    segmentStartTime = Time.realtimeSinceStartup;
                    nextCaptureTime = 0f;
                    prevQueueCount = 0;
                    continue;
                }
            }

            // 60-minute segment split
            if (now - segmentStartTime >= 3600f)
            {
                UnityEngine.Debug.Log("[REC] 60 minute limit reached, splitting segment");
                yield return StartCoroutine(SplitSegment());
                segmentStartTime = Time.realtimeSinceStartup;
                nextCaptureTime = 0f;
                prevQueueCount = 0;
                continue;
            }

            // Framerate limiting
            if (now < nextCaptureTime) continue;
            nextCaptureTime = now + captureInterval;

            captureTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
            captureTexture.Apply(false, false);
            byte[] raw = captureTexture.GetRawTextureData();
            byte[] copy = new byte[raw.Length];
            Buffer.BlockCopy(raw, 0, copy, 0, raw.Length);
            frameQueue.Enqueue(copy);
        }

        drainAndStop = true;
        while (isWriting) yield return null;

        Destroy(captureTexture);
        captureTexture = null;
        UnityEngine.Debug.Log("[REC] All segments finalised.");
    }

    IEnumerator SplitSegment()
    {
        drainAndStop = true;
        while (isWriting) yield return null;
        StartFfmpegProcess();
    }

    void WriteLoop()
    {
        Stream stdin = ffmpegProcess.StandardInput.BaseStream;

        while (true)
        {
            if (frameQueue.TryDequeue(out byte[] frame))
            {
                try { stdin.Write(frame, 0, frame.Length); }
                catch { break; }
            }
            else if ((drainAndStop || !isRecording) && frameQueue.IsEmpty)
            {
                break;
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        try
        {
            stdin.Close();
            ffmpegProcess.WaitForExit(8000);
        }
        catch { }

        ffmpegProcess.Dispose();
        ffmpegProcess = null;
        isWriting = false;

        UnityEngine.Debug.Log($"[REC] Segment {segmentNumber} finalised.");
    }

    void SetButtonState(bool recording)
    {
        if (buttonBackground != null)
            buttonBackground.color = recording ? colorRecording : colorReady;
        if (buttonIcon != null)
            buttonIcon.sprite = recording ? iconStop : iconRecord;
    }

    void OnDestroy()
    {
        isRecording = false;
        drainAndStop = true;

        if (ffmpegProcess != null && !ffmpegProcess.HasExited)
        {
            try { ffmpegProcess.Kill(); } catch { }
            ffmpegProcess.Dispose();
        }

        if (captureTexture != null)
            Destroy(captureTexture);
    }
}
