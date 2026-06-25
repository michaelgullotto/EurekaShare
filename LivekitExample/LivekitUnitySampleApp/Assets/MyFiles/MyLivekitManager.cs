using LiveKit;
using LiveKit.Proto;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.UI;
using RoomOptions = LiveKit.RoomOptions;

[Serializable]
public class LivekitAppConfig
{
    public string mode = "pub";
    public string identity = "Client";
    public string participantName = "";
    public string url = "ws://192.168.20.24:7880";
    public string tokenServerUrl = "http://192.168.20.24:3000/token";
    public string token = "";
    public bool autoStart = true;
    public string roomName = "room";
    public string password = "password";

    public int frameRate = 30;
    public ulong maxVideoBitrate = 1800000;
    public bool enableSimulcast = false;

    public int captureWidth = 960;
    public int captureHeight = 960;
    public float streamFrameRate = 30f;
    public bool showLocalPreview = false;
}

[Serializable]
public class QualityCommandPacket
{
    public string type = "quality";
    public string targetIdentity;
    public string state;
}

public enum StreamQualityState
{
    Idle,
    Small,
    Big
}

public class MyLivekitManager : MonoBehaviour
{
    [Header("LiveKit")]
    public string url = "ws://127.0.0.1:7880";
    public string token = "";
    public string tokenServerUrl = "http://127.0.0.1:3000/token";

    [Header("Runtime Args / Identity")]
    public string identity = "unity";
    public string participantName = "";
    public string mode = "view"; // "pub" or "view"
    public string roomName = "room";
    public string password = "password";
    public bool autoStart = true;

    [Header("Streaming")]
    public Camera captureCamera;
    public int frameRate = 30;
    public ulong maxVideoBitrate = 512000;
    public bool enableSimulcast = false;

    [Header("UI")]
    public GridLayoutGroup layoutGroup;
    public TMP_Text statusText;
    public bool showLocalPreview = false;

    [Header("Video Tile Prefab")]
    public VideoTileUI videoTilePrefab;

    [Header("Focused View")]
    public GameObject focusedVideoRoot;
    public RawImage focusedVideoImage;
    public TMP_Text focusedVideoLabel;

    [Header("RenderTexture Capture")]
    public int captureWidth = 960;
    public int captureHeight = 960;
    public float streamFrameRate = 30f;
    public RenderTexture captureRenderTexture;

    [Header("Viewer Diagnostics")]
    [Tooltip("Small GPU sample size used to estimate visible frame changes.")]
    public int viewerSampleWidth = 120;

    [Tooltip("Small GPU sample size used to estimate visible frame changes.")]
    public int viewerSampleHeight = 120;

    [Tooltip("How often to sample the displayed video texture on the viewer.")]
    public float viewerSampleRate = 30f;

    [Tooltip("Rolling window used for display FPS and estimated kbps.")]
    public float viewerMetricsWindow = 1f;

    [Tooltip("If true, also shows estimated visual kbps on viewer labels.")]
    public bool showEstimatedViewerKbps = true;

    private Room room;

    private readonly Dictionary<string, VideoTileUI> videoTiles = new();
    private readonly Dictionary<string, GameObject> audioObjects = new();
    private readonly Dictionary<string, VideoStream> remoteVideoStreams = new();

    private readonly List<RtcVideoSource> rtcVideoSources = new();
    private readonly List<RtcAudioSource> rtcAudioSources = new();

    private string localPreviewKey => $"local_{identity}";
    private string currentFocusedParticipant;
    private Coroutine streamLoopCoroutine;

    private const string ConfigFileName = "livekit_config.json";

    private TextureVideoSource currentPublishedVideoSource;
    private LocalVideoTrack currentPublishedVideoTrack;
    private Coroutine currentPublishedVideoSourceUpdateCoroutine;

    private StreamQualityState currentStreamQualityState = StreamQualityState.Small;
    private bool qualityChangeInProgress = false;

    // Publisher-side metrics
    private int publisherFramesThisWindow = 0;
    private float publisherWindowTime = 0f;
    private float publisherRenderFps = 0f;

    private class ViewerVideoDiagnostics
    {
        public Texture sourceTexture;

        public RenderTexture sampleRt;
        public bool readbackPending;

        public float sampleTimer;
        public float windowTimer;

        public int changedSamplesThisWindow;
        public float displayFps;

        public float lastVisualChangeTime;
        public ulong lastHash;
        public bool hasHash;

        // Estimated kbps using compressed sampled frame bytes
        public long compressedBytesThisWindow;
        public float estimatedKbps;

        // Cached UI values
        public string health = "Starting";
        public float staleFor = 0f;
    }

    private readonly Dictionary<string, ViewerVideoDiagnostics> viewerDiagnostics = new();

    private IEnumerator LoadConfig()
    {
        string persistentPath = Path.Combine(Application.persistentDataPath, ConfigFileName);
        string streamingPath = Path.Combine(Application.streamingAssetsPath, ConfigFileName);

        string json = null;
        string loadedFrom = null;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (File.Exists(persistentPath))
        {
            json = File.ReadAllText(persistentPath);
            loadedFrom = persistentPath;
        }
        else
        {
            using UnityWebRequest req = UnityWebRequest.Get(streamingPath);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text;
                loadedFrom = streamingPath;
            }
            else
            {
                Debug.LogWarning($"[CONFIG] Failed to load config from StreamingAssets: {req.error}");
                yield break;
            }
        }
#else
        if (File.Exists(streamingPath))
        {
            json = File.ReadAllText(streamingPath);
            loadedFrom = streamingPath;
        }
        else
        {
            Debug.LogWarning($"[CONFIG] Config file not found: {streamingPath}");
            yield break;
        }
#endif

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("[CONFIG] Config file is empty.");
            yield break;
        }

        LivekitAppConfig cfg = JsonUtility.FromJson<LivekitAppConfig>(json);

        if (cfg == null)
        {
            Debug.LogWarning("[CONFIG] Failed to parse config JSON.");
            yield break;
        }

        mode = cfg.mode;
        identity = cfg.identity;
        participantName = cfg.participantName;
        url = cfg.url;
        tokenServerUrl = cfg.tokenServerUrl;
        token = cfg.token;
        autoStart = cfg.autoStart;

        roomName = cfg.roomName;
        password = cfg.password;

        frameRate = cfg.frameRate;
        maxVideoBitrate = cfg.maxVideoBitrate;
        enableSimulcast = cfg.enableSimulcast;

        captureWidth = cfg.captureWidth;
        captureHeight = cfg.captureHeight;
        streamFrameRate = cfg.streamFrameRate;
        showLocalPreview = cfg.showLocalPreview;

        if (mode == "pub")
            currentStreamQualityState = StreamQualityState.Small;

        Debug.Log($"[CONFIG] Loaded from: {loadedFrom}");
        Debug.Log($"[CONFIG] persistentDataPath = {Application.persistentDataPath}");
        Debug.Log($"[CONFIG] streamingAssetsPath = {Application.streamingAssetsPath}");
        Debug.Log($"[CONFIG] mode={mode}, identity={identity}, url={url}, tokenServer={tokenServerUrl}");
    }

    private void Start()
    {
        StartCoroutine(StartupRoutine());
    }

    private IEnumerator StartupRoutine()
    {
        yield return LoadConfig();

#if UNITY_STANDALONE || UNITY_EDITOR
        ParseArgs();
#endif

        if (focusedVideoRoot != null)
            focusedVideoRoot.SetActive(false);

        if (autoStart)
            StartCoroutine(AutoBoot());
    }

    private void Update()
    {
        UpdateViewerDiagnostics();
    }

    private void ParseArgs()
    {
        var args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--id" && i + 1 < args.Length) identity = args[i + 1];
            if (args[i] == "--name" && i + 1 < args.Length) participantName = args[i + 1];
            if (args[i] == "--mode" && i + 1 < args.Length) mode = args[i + 1];
            if (args[i] == "--url" && i + 1 < args.Length) url = args[i + 1];
            if (args[i] == "--token-server" && i + 1 < args.Length) tokenServerUrl = args[i + 1];
            if (args[i] == "--token" && i + 1 < args.Length) token = args[i + 1];

            if (args[i] == "--roomName" && i + 1 < args.Length) roomName = args[i + 1];
            if (args[i] == "--password" && i + 1 < args.Length) password = args[i + 1];
        }
    }

    private IEnumerator AutoBoot()
    {
        UpdateStatusText($"Booting {identity} ({mode})");

        if (string.IsNullOrWhiteSpace(token))
        {
            yield return FetchToken();
            if (string.IsNullOrWhiteSpace(token))
                yield break;
        }

        yield return MakeCall();

        if (room == null)
            yield break;

        if (mode == "pub")
        {
            ApplyQualityProfile(currentStreamQualityState);
            yield return PublishVideo();
        }
    }

    private IEnumerator FetchToken()
    {
        string finalName = string.IsNullOrWhiteSpace(participantName) ? identity : participantName;
        string requestUrl =
            $"{tokenServerUrl}?identity={UnityWebRequest.EscapeURL(identity)}" +
            $"&name={UnityWebRequest.EscapeURL(finalName)}" +
            $"&room={UnityWebRequest.EscapeURL(roomName)}" +
            $"&password={UnityWebRequest.EscapeURL(password)}";

        UpdateStatusText("Fetching token...");

        using UnityWebRequest req = UnityWebRequest.Get(requestUrl);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Failed to fetch token: {req.error}");
            UpdateStatusText("Token fetch failed");
            yield break;
        }

        token = req.downloadHandler.text.Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            Debug.LogError("Token server returned empty token");
            UpdateStatusText("Empty token");
            yield break;
        }

        Debug.Log($"Fetched token for {identity}");
    }

    public void UpdateStatusText(string newText)
    {
        if (statusText != null)
            statusText.text = newText;
    }

    public void OnClickMakeCall()
    {
        StartCoroutine(MakeCallWithFreshToken());
    }

    public void OnClickPublishVideo()
    {
        StartCoroutine(PublishVideoSafe());
    }

    public void OnClickPublishAudio()
    {
        StartCoroutine(PublishMicrophone());
    }

    public void OnClickPublishData()
    {
        PublishData();
    }

    public void OnClickHangup()
    {
        if (streamLoopCoroutine != null)
        {
            StopCoroutine(streamLoopCoroutine);
            streamLoopCoroutine = null;
        }

        if (captureCamera != null)
            captureCamera.targetTexture = null;

        if (room != null)
        {
            UnregisterRoomCallbacks();
            room.Disconnect();
            room = null;
        }

        CleanUp();
        ClearFocusedParticipant();
        UpdateStatusText("Disconnected");
    }

    private IEnumerator MakeCallWithFreshToken()
    {
        if (room != null)
        {
            Debug.Log("Already connected.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            yield return FetchToken();
            if (string.IsNullOrWhiteSpace(token))
                yield break;
        }

        yield return MakeCall();
    }

    private IEnumerator MakeCall()
    {
        if (room != null)
            yield break;

        room = new Room();
        room.TrackSubscribed += TrackSubscribed;
        room.TrackUnsubscribed += TrackUnsubscribed;
        room.DataReceived += DataReceived;

        var options = new RoomOptions();

        if (mode == "pub")
            options.AutoSubscribe = false;

        var connect = room.Connect(url, token, options);
        yield return connect;

        if (!connect.IsError)
        {
            Debug.Log("Connected to " + room.Name);
            UpdateStatusText("Connected");
        }
        else
        {
            Debug.LogError("Failed to connect to LiveKit.");
            UpdateStatusText("Connect failed");
            UnregisterRoomCallbacks();
            room = null;
        }
    }

    private void UnregisterRoomCallbacks()
    {
        if (room == null) return;
        room.TrackSubscribed -= TrackSubscribed;
        room.TrackUnsubscribed -= TrackUnsubscribed;
        room.DataReceived -= DataReceived;
    }

    private void CleanUp()
    {
        foreach (var item in audioObjects)
        {
            if (item.Value != null)
            {
                var source = item.Value.GetComponent<AudioSource>();
                if (source != null) source.Stop();
                Destroy(item.Value);
            }
        }
        audioObjects.Clear();

        foreach (var item in rtcAudioSources)
            item.Stop();
        rtcAudioSources.Clear();

        foreach (var kvp in remoteVideoStreams)
        {
            if (kvp.Value != null)
            {
                kvp.Value.Stop();
                kvp.Value.Dispose();
            }
        }
        remoteVideoStreams.Clear();

        foreach (var item in videoTiles)
        {
            if (item.Value != null)
                Destroy(item.Value.gameObject);
        }
        videoTiles.Clear();

        foreach (var item in rtcVideoSources)
        {
            item.Stop();
            item.Dispose();
        }
        rtcVideoSources.Clear();

        foreach (var kvp in viewerDiagnostics)
        {
            if (kvp.Value != null && kvp.Value.sampleRt != null)
            {
                kvp.Value.sampleRt.Release();
                Destroy(kvp.Value.sampleRt);
            }
        }
        viewerDiagnostics.Clear();

        if (currentPublishedVideoSourceUpdateCoroutine != null)
        {
            StopCoroutine(currentPublishedVideoSourceUpdateCoroutine);
            currentPublishedVideoSourceUpdateCoroutine = null;
        }

        if (currentPublishedVideoSource != null)
        {
            currentPublishedVideoSource.Stop();
            currentPublishedVideoSource = null;
        }

        currentPublishedVideoTrack = null;

        publisherFramesThisWindow = 0;
        publisherWindowTime = 0f;
        publisherRenderFps = 0f;
    }

    private VideoTileUI CreateVideoTile(string key, string labelText)
    {
        if (videoTilePrefab == null)
        {
            Debug.LogError("videoTilePrefab is not assigned.");
            return null;
        }

        if (layoutGroup == null)
        {
            Debug.LogError("layoutGroup is not assigned.");
            return null;
        }

        var tile = Instantiate(videoTilePrefab, layoutGroup.transform);
        tile.name = key;
        tile.transform.localScale = Vector3.one;

        if (tile.nameText != null)
            tile.SetLabel(labelText);

        return tile;
    }

    private void BindTileButtons(VideoTileUI tile, string participantId, Func<Texture> textureGetter)
    {
        if (tile.btnIn != null)
        {
            tile.btnIn.onClick.RemoveAllListeners();
            tile.btnIn.onClick.AddListener(() =>
            {
                FocusParticipant(participantId, textureGetter());
            });
        }

        if (tile.btnAudio != null)
        {
            tile.btnAudio.onClick.RemoveAllListeners();
            tile.btnAudio.onClick.AddListener(() =>
            {
                OnClickAudioForParticipant(participantId);
            });
        }
    }

    private void AddRemoteVideoTrack(RemoteVideoTrack videoTrack, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        string key = participant.Identity;

        if (videoTiles.ContainsKey(key))
        {
            Debug.Log($"Video tile already exists for {key}, skipping duplicate.");
            return;
        }

        Debug.Log($"AddRemoteVideoTrack sid={videoTrack.Sid}, participant={participant.Identity}");

        VideoTileUI tile = CreateVideoTile(key, participant.Identity);
        if (tile == null)
            return;

        BindTileButtons(tile, participant.Identity, () => tile.videoImage != null ? tile.videoImage.texture : null);

        var diag = new ViewerVideoDiagnostics
        {
            sourceTexture = null,
            readbackPending = false,
            sampleTimer = 0f,
            windowTimer = 0f,
            changedSamplesThisWindow = 0,
            displayFps = 0f,
            lastVisualChangeTime = Time.unscaledTime,
            hasHash = false,
            compressedBytesThisWindow = 0,
            estimatedKbps = 0f,
            health = "Starting",
            staleFor = 0f
        };

        viewerDiagnostics[key] = diag;

        var stream = new VideoStream(videoTrack);
        stream.TextureReceived += tex =>
        {
            if (tile != null)
                tile.SetTexture(tex);

            if (viewerDiagnostics.TryGetValue(key, out var d) && d != null)
                d.sourceTexture = tex;

            if (currentFocusedParticipant == participant.Identity && focusedVideoImage != null)
                focusedVideoImage.texture = tex;
        };

        remoteVideoStreams[key] = stream;
        videoTiles[key] = tile;

        stream.Start();
        StartCoroutine(stream.Update());
    }

    private void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (mode == "pub")
            return;

        if (track is RemoteVideoTrack videoTrack)
        {
            AddRemoteVideoTrack(videoTrack, publication, participant);
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            Debug.Log("AddAudioTrack " + audioTrack.Sid);

            if (audioObjects.ContainsKey(audioTrack.Sid))
                return;

            GameObject audioObj = new GameObject(audioTrack.Sid);
            var source = audioObj.AddComponent<AudioSource>();
            _ = new AudioStream(audioTrack, source);

            audioObjects[audioTrack.Sid] = audioObj;
        }
    }

    private void TrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack)
        {
            string key = participant.Identity;

            if (remoteVideoStreams.TryGetValue(key, out var stream))
            {
                stream.Stop();
                stream.Dispose();
                remoteVideoStreams.Remove(key);
            }

            if (viewerDiagnostics.TryGetValue(key, out var diag) && diag != null)
            {
                if (diag.sampleRt != null)
                {
                    diag.sampleRt.Release();
                    Destroy(diag.sampleRt);
                }

                viewerDiagnostics.Remove(key);
            }

            if (videoTiles.TryGetValue(key, out var tile))
            {
                if (tile != null)
                    Destroy(tile.gameObject);

                videoTiles.Remove(key);
            }

            if (currentFocusedParticipant == participant.Identity && focusedVideoLabel != null)
            {
                focusedVideoLabel.text = participant.Identity + "\nReconnecting...";
            }
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
            if (audioObjects.TryGetValue(audioTrack.Sid, out var audioObj))
            {
                if (audioObj != null)
                {
                    var source = audioObj.GetComponent<AudioSource>();
                    if (source != null) source.Stop();
                    Destroy(audioObj);
                }

                audioObjects.Remove(audioTrack.Sid);
            }
        }
    }

    private void DataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        Debug.Log($"DataReceived raw: {json}");

        if (mode != "pub")
            return;

        QualityCommandPacket cmd = null;

        try
        {
            cmd = JsonUtility.FromJson<QualityCommandPacket>(json);
        }
        catch
        {
            return;
        }

        if (cmd == null) return;
        if (cmd.type != "quality") return;
        if (cmd.targetIdentity != identity) return;

        Debug.Log($"[QUALITY CMD] Sender {identity} received state {cmd.state}");

        if (!Enum.TryParse(cmd.state, out StreamQualityState state))
            return;

        StartCoroutine(ApplyQualityStateByReconnect(state));
    }

    public IEnumerator PublishMicrophone()
    {
        if (room == null)
        {
            Debug.LogError("Room is null. Connect first.");
            yield break;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone device available.");
            yield break;
        }

        Debug.Log("PublishMicrophone");

        string localSid = "my-audio-source";
        GameObject audioObject = new GameObject(localSid);
        audioObjects[localSid] = audioObject;

        var rtcSource = new MicrophoneSource(Microphone.devices[0], audioObject);
        var track = LocalAudioTrack.CreateAudioTrack("my-audio-track", rtcSource, room);

        var options = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
            Source = TrackSource.SourceMicrophone
        };

        var publish = room.LocalParticipant.PublishTrack(track, options);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Audio track published!");
            rtcAudioSources.Add(rtcSource);
            rtcSource.Start();
        }
        else
        {
            Debug.LogError("Audio publish error.");
            Destroy(audioObject);
            audioObjects.Remove(localSid);
        }
    }

    private IEnumerator PublishVideoSafe()
    {
        if (room == null)
            yield return MakeCallWithFreshToken();

        if (room == null)
        {
            Debug.LogError("Still not connected. Cannot publish video.");
            yield break;
        }

        yield return PublishVideo();
    }

    public IEnumerator PublishVideo()
    {
        if (room == null)
        {
            Debug.LogError("Room is null. Connect first.");
            yield break;
        }

        var cam = captureCamera;
        if (cam == null)
        {
            Debug.LogError("No capture camera found.");
            yield break;
        }

        if (captureRenderTexture != null)
        {
            if (captureRenderTexture.width != captureWidth || captureRenderTexture.height != captureHeight)
            {
                captureRenderTexture.Release();
                Destroy(captureRenderTexture);
                captureRenderTexture = null;
            }
        }

        if (captureRenderTexture == null)
        {
            captureRenderTexture = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
            captureRenderTexture.name = $"RT_{identity}";
            captureRenderTexture.Create();
        }

        cam.targetTexture = captureRenderTexture;
        cam.enabled = false;
        cam.Render();

        Debug.Log($"Capture camera: {cam.name}");
        Debug.Log($"Assigned RT: {captureRenderTexture.name}, {captureRenderTexture.width}x{captureRenderTexture.height}");
        Debug.Log($"Camera targetTexture == RT ? {cam.targetTexture == captureRenderTexture}");

        if (showLocalPreview && !videoTiles.ContainsKey(localPreviewKey))
        {
            var previewTile = CreateVideoTile(localPreviewKey, $"{identity} (local)");
            if (previewTile != null)
            {
                previewTile.SetTexture(captureRenderTexture);
                BindTileButtons(previewTile, localPreviewKey, () => captureRenderTexture);
                videoTiles[localPreviewKey] = previewTile;
            }
        }
        else if (showLocalPreview && videoTiles.TryGetValue(localPreviewKey, out var existingPreview) && existingPreview != null)
        {
            existingPreview.SetTexture(captureRenderTexture);
        }

        currentPublishedVideoSource = new TextureVideoSource(captureRenderTexture);
        currentPublishedVideoTrack = LocalVideoTrack.CreateVideoTrack($"video_{identity}", currentPublishedVideoSource, room);

        var options = new TrackPublishOptions
        {
            VideoCodec = VideoCodec.H264,
            Simulcast = enableSimulcast,
            Source = TrackSource.SourceCamera,
            VideoEncoding = new VideoEncoding
            {
                MaxBitrate = maxVideoBitrate,
                MaxFramerate = frameRate
            }
        };

        var publish = room.LocalParticipant.PublishTrack(currentPublishedVideoTrack, options);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log($"Video track published! identity={identity}, simulcast={enableSimulcast}");

            currentPublishedVideoSource.Start();

            if (currentPublishedVideoSourceUpdateCoroutine != null)
                StopCoroutine(currentPublishedVideoSourceUpdateCoroutine);

            currentPublishedVideoSourceUpdateCoroutine = StartCoroutine(currentPublishedVideoSource.Update());

            if (streamLoopCoroutine != null)
                StopCoroutine(streamLoopCoroutine);

            publisherFramesThisWindow = 0;
            publisherWindowTime = 0f;
            publisherRenderFps = 0f;

            streamLoopCoroutine = StartCoroutine(StreamLoop());

            LogPublisherVideoState("After Publish");
        }
        else
        {
            Debug.LogError("Video publish error.");

            if (currentPublishedVideoSource != null)
            {
                currentPublishedVideoSource.Stop();
                currentPublishedVideoSource = null;
            }

            currentPublishedVideoTrack = null;
        }
    }

    private void LogPublisherVideoState(string prefix)
    {
        string rtInfo = captureRenderTexture != null
            ? $"{captureRenderTexture.width}x{captureRenderTexture.height}"
            : "null";

        string camRtInfo = (captureCamera != null && captureCamera.targetTexture != null)
            ? $"{captureCamera.targetTexture.width}x{captureCamera.targetTexture.height}"
            : "null";

        Debug.Log($"[PUB] {prefix} | identity={identity} | RT={rtInfo} | CamTarget={camRtInfo} | " +
                  $"cfgFps={frameRate} | streamFps={streamFrameRate} | renderFps={publisherRenderFps:F1} | " +
                  $"bitrate={maxVideoBitrate} | simulcast={enableSimulcast}");
    }

    private IEnumerator StreamLoop()
    {
        float interval = 1f / Mathf.Max(streamFrameRate, 0.0001f);

        while (true)
        {
            if (captureCamera != null)
            {
                captureCamera.Render();

                publisherFramesThisWindow++;
                publisherWindowTime += interval;

                if (publisherWindowTime >= 1f)
                {
                    publisherRenderFps =
                        publisherFramesThisWindow / Mathf.Max(publisherWindowTime, 0.0001f);

                    UpdateStatusText(
                        $"PUB {identity} | " +
                        $"{captureWidth}x{captureHeight} | " +
                        $"cfgFps:{frameRate} | streamFps:{streamFrameRate:F0} | render:{publisherRenderFps:F1} | " +
                        $"bitrate:{maxVideoBitrate / 1000f:F0}kbps | " +
                        $"simulcast:{enableSimulcast}"
                    );

                    publisherFramesThisWindow = 0;
                    publisherWindowTime = 0f;
                }
            }

            yield return new WaitForSeconds(interval);
        }
    }

    public void PublishData()
    {
        if (room == null)
        {
            Debug.LogError("Room is null. Connect first.");
            return;
        }

        var str = "hello from unity!";
        room.LocalParticipant.PublishData(System.Text.Encoding.Default.GetBytes(str));
    }

    public void FocusParticipant(string participantId, Texture tex)
    {
        currentFocusedParticipant = participantId;

        if (layoutGroup != null)
            layoutGroup.gameObject.SetActive(false);

        if (focusedVideoRoot != null)
            focusedVideoRoot.SetActive(true);

        if (focusedVideoImage != null)
        {
            focusedVideoImage.texture = tex;
            focusedVideoImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        if (focusedVideoLabel != null)
        {
            if (viewerDiagnostics.TryGetValue(participantId, out var diag) && diag != null)
            {
                focusedVideoLabel.text = BuildViewerLabelText(participantId, diag);
            }
            else
            {
                focusedVideoLabel.text = participantId;
            }
        }

        if (mode == "view" && remoteVideoStreams.ContainsKey(participantId))
        {
            SendFocusedQualityCommands(participantId);
        }
    }

    public void ClearFocusedParticipant()
    {
        currentFocusedParticipant = null;

        if (focusedVideoRoot != null)
            focusedVideoRoot.SetActive(false);

        if (focusedVideoImage != null)
        {
            focusedVideoImage.texture = null;
            focusedVideoImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        if (focusedVideoLabel != null)
            focusedVideoLabel.text = "";

        if (layoutGroup != null)
            layoutGroup.gameObject.SetActive(true);

        if (mode == "view")
        {
            SendGridQualityCommands();
        }
    }

    private void OnClickAudioForParticipant(string participantId)
    {
        Debug.Log($"Audio button clicked for participant: {participantId}");
    }

    private void UpdateViewerDiagnostics()
    {
        if (viewerDiagnostics.Count == 0)
            return;

        float dt = Time.unscaledDeltaTime;
        float now = Time.unscaledTime;
        float sampleInterval = 1f / Mathf.Max(viewerSampleRate, 1f);

        var keys = new List<string>(viewerDiagnostics.Keys);

        foreach (string key in keys)
        {
            if (!viewerDiagnostics.TryGetValue(key, out var diag) || diag == null)
                continue;

            if (diag.sourceTexture == null)
            {
                diag.health = "NoTexture";
                diag.staleFor = 999f;
                UpdateViewerLabel(key, diag);
                continue;
            }

            diag.sampleTimer += dt;
            diag.windowTimer += dt;

            if (diag.sampleRt == null)
            {
                diag.sampleRt = new RenderTexture(viewerSampleWidth, viewerSampleHeight, 0, RenderTextureFormat.ARGB32);
                diag.sampleRt.name = $"ViewerDiag_{key}";
                diag.sampleRt.Create();
            }

            if (!diag.readbackPending && diag.sampleTimer >= sampleInterval)
            {
                diag.sampleTimer = 0f;
                RequestViewerTextureSample(key, diag);
            }

            if (diag.windowTimer >= viewerMetricsWindow)
            {
                diag.displayFps = diag.changedSamplesThisWindow / Mathf.Max(diag.windowTimer, 0.0001f);
                diag.estimatedKbps = (diag.compressedBytesThisWindow * 8f / 1000f) / Mathf.Max(diag.windowTimer, 0.0001f);

                diag.changedSamplesThisWindow = 0;
                diag.compressedBytesThisWindow = 0;
                diag.windowTimer = 0f;
            }

            diag.staleFor = now - diag.lastVisualChangeTime;
            diag.health = GetViewerHealth(diag.staleFor);

            UpdateViewerLabel(key, diag);

            if (currentFocusedParticipant == key && focusedVideoLabel != null)
            {
                focusedVideoLabel.text = BuildViewerLabelText(key, diag);
            }
        }
    }

    private void RequestViewerTextureSample(string key, ViewerVideoDiagnostics diag)
    {
        if (diag.sourceTexture == null || diag.sampleRt == null)
            return;

        diag.readbackPending = true;

        Graphics.Blit(diag.sourceTexture, diag.sampleRt);

        AsyncGPUReadback.Request(diag.sampleRt, 0, TextureFormat.RGBA32, request =>
        {
            OnViewerSampleReady(key, request);
        });
    }

    private void OnViewerSampleReady(string key, AsyncGPUReadbackRequest request)
    {
        if (!viewerDiagnostics.TryGetValue(key, out var diag) || diag == null)
            return;

        diag.readbackPending = false;

        if (request.hasError)
            return;

        NativeArray<byte> data = request.GetData<byte>();
        ulong hash = HashSample(data);

        // Very rough "activity-based kbps" estimate:
        // compress the tiny sampled frame and count bytes per second.
        byte[] raw = data.ToArray();
        byte[] compressed = CompressBytes(raw);
        diag.compressedBytesThisWindow += compressed != null ? compressed.Length : raw.Length;

        if (!diag.hasHash)
        {
            diag.hasHash = true;
            diag.lastHash = hash;
            diag.lastVisualChangeTime = Time.unscaledTime;
            return;
        }

        if (hash != diag.lastHash)
        {
            diag.lastHash = hash;
            diag.changedSamplesThisWindow++;
            diag.lastVisualChangeTime = Time.unscaledTime;
        }
    }

    private static ulong HashSample(NativeArray<byte> data)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;

        for (int i = 0; i < data.Length; i++)
        {
            hash ^= data[i];
            hash *= prime;
        }

        return hash;
    }

    private static byte[] CompressBytes(byte[] input)
    {
        try
        {
            using var output = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                gzip.Write(input, 0, input.Length);
            }
            return output.ToArray();
        }
        catch
        {
            return input;
        }
    }

    private static string GetViewerHealth(float staleFor)
    {
        if (staleFor < 0.15f) return "Smooth";
        if (staleFor < 0.50f) return "Unstable";
        return "Frozen";
    }

    private void UpdateViewerLabel(string key, ViewerVideoDiagnostics diag)
    {
        if (!videoTiles.TryGetValue(key, out var tile) || tile == null || tile.nameText == null)
            return;

        tile.SetLabel(BuildViewerLabelText(key, diag));
    }

    private string BuildViewerLabelText(string key, ViewerVideoDiagnostics diag)
    {
        // Add viewer video diagnostics label (display fps, stale time, estimated kbps)
        //if (showEstimatedViewerKbps)
        //{
        //    return
        //        $"{key}\n" +
        //        $"display:{diag.displayFps:F1}fps  {diag.health}\n" +
        //        $"stale:{diag.staleFor:F2}s  net~{diag.estimatedKbps:F0}kbps";
        //}

        //return
        //    $"{key}\n" +
        //    $"display:{diag.displayFps:F1}fps  {diag.health}\n" +
        //    $"stale:{diag.staleFor:F2}s";

        // swap to above commented code if want full diagnostics
        return key;
    }

    void SendQualityCommand(string targetIdentity, StreamQualityState state)
    {
        if (room == null) return;
        if (mode != "view") return;

        QualityCommandPacket cmd = new QualityCommandPacket
        {
            targetIdentity = targetIdentity,
            state = state.ToString()
        };

        string json = JsonUtility.ToJson(cmd);
        room.LocalParticipant.PublishData(System.Text.Encoding.UTF8.GetBytes(json));

        Debug.Log($"[QUALITY CMD] Viewer sent {state} to {targetIdentity}");
    }

    void SendFocusedQualityCommands(string focusedIdentity)
    {
        foreach (var kvp in remoteVideoStreams)
        {
            if (kvp.Key == focusedIdentity)
                SendQualityCommand(kvp.Key, StreamQualityState.Big);
            else
                SendQualityCommand(kvp.Key, StreamQualityState.Idle);
        }
    }

    void SendGridQualityCommands()
    {
        foreach (var kvp in remoteVideoStreams)
        {
            SendQualityCommand(kvp.Key, StreamQualityState.Small);
        }
    }

 

    IEnumerator ApplyQualityStateByReconnect(StreamQualityState newState)
    {
        if (mode != "pub")
            yield break;

        if (qualityChangeInProgress)
            yield break;

        if (currentStreamQualityState == newState)
            yield break;

        qualityChangeInProgress = true;

        Debug.Log($"[QUALITY CMD] Applying state by reconnect: {newState}");

        currentStreamQualityState = newState;
        ApplyQualityProfile(newState);

        yield return ReconnectWithCurrentQuality();

        qualityChangeInProgress = false;
    }

    IEnumerator ReconnectWithCurrentQuality()
    {
        Debug.Log($"[QUALITY CMD] Reconnecting sender {identity} with {captureWidth}x{captureHeight} @ {streamFrameRate}fps");

        if (room != null)
        {
            UnregisterRoomCallbacks();
            room.Disconnect();
            room = null;
        }

        if (streamLoopCoroutine != null)
        {
            StopCoroutine(streamLoopCoroutine);
            streamLoopCoroutine = null;
        }

        if (currentPublishedVideoSourceUpdateCoroutine != null)
        {
            StopCoroutine(currentPublishedVideoSourceUpdateCoroutine);
            currentPublishedVideoSourceUpdateCoroutine = null;
        }

        if (captureCamera != null)
            captureCamera.targetTexture = null;

        if (captureRenderTexture != null)
        {
            captureRenderTexture.Release();
            Destroy(captureRenderTexture);
            captureRenderTexture = null;
        }

        CleanUp();
        UpdateStatusText($"Reconnecting {identity}...");

        yield return null;

        // try reconnect with existing token first
        yield return MakeCall();

        // only fetch a fresh token if reconnect failed
        if (room == null)
        {
            token = "";

            yield return FetchToken();
            if (string.IsNullOrWhiteSpace(token))
                yield break;

            yield return MakeCall();
            if (room == null)
                yield break;
        }

        yield return PublishVideo();
    }

    private void OnDestroy()
    {
        if (streamLoopCoroutine != null)
        {
            StopCoroutine(streamLoopCoroutine);
            streamLoopCoroutine = null;
        }

        if (captureCamera != null)
            captureCamera.targetTexture = null;

        if (captureRenderTexture != null)
        {
            captureRenderTexture.Release();
            captureRenderTexture = null;
        }

        CleanUp();
        ClearFocusedParticipant();
        UnregisterRoomCallbacks();

        if (room != null)
        {
            room.Disconnect();
            room = null;
        }
    }

    // added by michael bellow
    public bool IsConnected => room != null;

    public void StartManualConnectAndPublish()
    {
        StartCoroutine(ManualConnectAndPublishRoutine());
    }

    IEnumerator ManualConnectAndPublishRoutine()
    {
        token = "";

        yield return FetchToken();
        if (string.IsNullOrWhiteSpace(token))
            yield break;

        yield return MakeCall();
        if (room == null)
            yield break;

        if (mode == "pub")
        {
            ApplyQualityProfile(currentStreamQualityState);
            yield return PublishVideo();
        }
    }

    void ApplyQualityProfile(StreamQualityState state)
    {
        switch (state)
        {
            case StreamQualityState.Big:
                captureWidth = 1920;
                captureHeight = 1080;
                frameRate = 60;
                streamFrameRate = 60f;
                maxVideoBitrate = 8000000;
                break;

            case StreamQualityState.Small:
                captureWidth = 640;
                captureHeight = 360;
                frameRate = 30;
                streamFrameRate = 30f;
                maxVideoBitrate = 600000;
                break;

            case StreamQualityState.Idle:
                captureWidth = 160;
                captureHeight = 90;
                frameRate = 1;
                streamFrameRate = 1f;
                maxVideoBitrate = 50000;
                break;
        }
    }
}