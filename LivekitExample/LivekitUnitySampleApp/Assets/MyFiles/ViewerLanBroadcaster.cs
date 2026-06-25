using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;


[System.Serializable]
public class ViewerHostBroadcastPacket
{
    public string type = "viewerHost";
    public string roomName;
    public string ip;
    public int tokenPort = 3000;
    public int livekitPort = 7880;
}

public class ViewerLanBroadcaster : MonoBehaviour
{
    [Header("Broadcast")]
    [SerializeField] int broadcastPort = 47777;
    [SerializeField] float broadcastInterval = 1f;
    [SerializeField] int tokenPort = 3000;
    [SerializeField] int livekitPort = 7880;
    [SerializeField] bool autoStart = true;

    string roomName;
    string localIp;

    [SerializeField] TextMeshProUGUI buttonText;

    UdpClient udpClient;
    Coroutine broadcastRoutine;

    bool isFullscreen = false;

    void Start()
    {
        SetWindowed();

        if (autoStart)
            StartBroadcasting();
    }

    public void StartBroadcasting()
    {
        StopBroadcasting();

        if (!LoadRoomNameFromConfig())
        {
            Debug.LogError("[ViewerLanBroadcaster] Failed to load roomName from livekit_config.json");
            return;
        }

        localIp = GetLocalIPv4();
        if (string.IsNullOrWhiteSpace(localIp))
        {
            Debug.LogError("[ViewerLanBroadcaster] Failed to find local IPv4 address");
            return;
        }

        udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;

        broadcastRoutine = StartCoroutine(BroadcastLoop());

        Debug.Log($"[ViewerLanBroadcaster] Broadcasting room '{roomName}' from {localIp}:{broadcastPort}");
    }

    public void StopBroadcasting()
    {
        if (broadcastRoutine != null)
        {
            StopCoroutine(broadcastRoutine);
            broadcastRoutine = null;
        }

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
    }

    IEnumerator BroadcastLoop()
    {
        IPEndPoint broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, broadcastPort);

        while (true)
        {
            ViewerHostBroadcastPacket packet = new ViewerHostBroadcastPacket
            {
                roomName = roomName,
                ip = localIp,
                tokenPort = tokenPort,
                livekitPort = livekitPort
            };

            string json = JsonUtility.ToJson(packet);
            byte[] data = Encoding.UTF8.GetBytes(json);

            udpClient.Send(data, data.Length, broadcastEndPoint);

            yield return new WaitForSeconds(broadcastInterval);
        }
    }

    bool LoadRoomNameFromConfig()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "livekit_config.json");

        if (!File.Exists(path))
        {
            Debug.LogError("[ViewerLanBroadcaster] Config file not found: " + path);
            return false;
        }

        string json = File.ReadAllText(path);
        LivekitAppConfig cfg = JsonUtility.FromJson<LivekitAppConfig>(json);

        if (cfg == null)
        {
            Debug.LogError("[ViewerLanBroadcaster] Failed to parse config JSON");
            return false;
        }

        roomName = cfg.roomName;

        if (string.IsNullOrWhiteSpace(roomName))
        {
            Debug.LogError("[ViewerLanBroadcaster] roomName is empty in config");
            return false;
        }

        return true;
    }

    string GetLocalIPv4()
    {
        string bestIp = "";

        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            {
                bestIp = ip.ToString();
                break;
            }
        }

        return bestIp;
    }

    void OnDestroy()
    {
        StopBroadcasting();
    }

    public void SetScreen()
    {
        if (isFullscreen)
        {
            buttonText.text = "Set Fullscreen";
            SetWindowed();    
        }
        else 
        {
            buttonText.text = "Set Windowed";
            SetFullscreen();
        }
        isFullscreen = !isFullscreen;
        EventSystem.current.SetSelectedGameObject(null);
    }

     void SetWindowed()
    {

        if (Screen.currentResolution.height >= 1080)
        {
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
        }
        else if (Screen.currentResolution.height >= 900)
        {
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
        }
        else if (Screen.currentResolution.height >= 720)
        {
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        }
        else
        {
            Screen.SetResolution(854, 480, FullScreenMode.Windowed);
        }
        
    }

     void SetFullscreen()
    {
        Resolution currentRes = Screen.currentResolution;
        Screen.SetResolution(currentRes.width, currentRes.height, FullScreenMode.FullScreenWindow);
    }
}