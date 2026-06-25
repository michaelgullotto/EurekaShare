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
    int captureWidth;
    int captureHeight;

    static string FfmpegPath => Path.Combine(
        Path.GetDirectoryName(Application.dataPath), "ffmpeg.exe");

    static string BuildOutputPath()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EurekaShare");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"Recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4");
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
        string output = BuildOutputPath();

        string args = $"-y -f rawvideo -pixel_format rgb24 -video_size {captureWidth}x{captureHeight} " +
                      $"-framerate 60 -i pipe:0 -vf vflip -c:v libx264 -preset veryfast -pix_fmt yuv420p \"{output}\"";

        ffmpegProcess = new Process();
        ffmpegProcess.StartInfo.FileName = FfmpegPath;
        ffmpegProcess.StartInfo.Arguments = args;
        ffmpegProcess.StartInfo.UseShellExecute = false;
        ffmpegProcess.StartInfo.RedirectStandardInput = true;
        ffmpegProcess.StartInfo.CreateNoWindow = true;
        ffmpegProcess.Start();

        isWriting = true;
        writeThread = new Thread(WriteLoop) { IsBackground = true };
        writeThread.Start();

        isRecording = true;
        SetButtonState(true);
        StartCoroutine(CaptureLoop());
        EventSystem.current.SetSelectedGameObject(null);

        UnityEngine.Debug.Log($"[REC] Recording started: {output}");
    }

    void StopRecording()
    {
        isRecording = false;
        SetButtonState(false);
        EventSystem.current.SetSelectedGameObject(null);
        UnityEngine.Debug.Log("[REC] Stopping — finalising file...");
    }

    IEnumerator CaptureLoop()
    {
        var eof = new WaitForEndOfFrame();
        captureTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);

        while (isRecording)
        {
            yield return eof;
            captureTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
            captureTexture.Apply(false, false);
            byte[] raw = captureTexture.GetRawTextureData();
            byte[] copy = new byte[raw.Length];
            Buffer.BlockCopy(raw, 0, copy, 0, raw.Length);
            frameQueue.Enqueue(copy);
        }

        Destroy(captureTexture);
        captureTexture = null;
    }

    void WriteLoop()
    {
        Stream stdin = ffmpegProcess.StandardInput.BaseStream;

        while (isWriting)
        {
            if (frameQueue.TryDequeue(out byte[] frame))
            {
                try { stdin.Write(frame, 0, frame.Length); }
                catch { break; }
            }
            else if (!isRecording && frameQueue.IsEmpty)
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

        UnityEngine.Debug.Log("[REC] File finalised.");
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
        isWriting = false;

        if (ffmpegProcess != null && !ffmpegProcess.HasExited)
        {
            try { ffmpegProcess.Kill(); } catch { }
            ffmpegProcess.Dispose();
        }

        if (captureTexture != null)
            Destroy(captureTexture);
    }
}
