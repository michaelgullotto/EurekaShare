using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SenderLanDiscovery : MonoBehaviour
{
    [System.Serializable]
    class DiscoveredRoom
    {
        public string key;
        public string roomName;
        public string ip;
        public int tokenPort;
        public int livekitPort;
        public float lastSeenTime;
    }

    [Header("Discovery")]
    [SerializeField] int listenPort = 47777;
    [SerializeField] float staleAfterSeconds = 3f;

    [Header("UI")]
    [SerializeField] GameObject browserUIRoot;
    [SerializeField] Transform roomListParent;
    [SerializeField] Button roomButtonPrefab;
    [SerializeField] TMP_InputField passwordInput;
    [SerializeField] TMP_Text statusText;

    [SerializeField] TMP_InputField identityInput;

    [SerializeField] GameObject openBut, closeBut;

    [Header("LiveKit")]
    [SerializeField] MyLivekitManager livekitManager;

    UdpClient udpClient;
    Coroutine listenRoutine;

    readonly Dictionary<string, DiscoveredRoom> discoveredRooms = new();
    readonly List<Button> spawnedButtons = new();

    DiscoveredRoom selectedRoom;

    public void OpenBrowserUI()
    {
        if (browserUIRoot) browserUIRoot.SetActive(true);
        if (identityInput != null && string.IsNullOrWhiteSpace(identityInput.text))
        {
            identityInput.text = livekitManager.identity;
        }
        SetStatus("Looking For Rooms...");
        openBut.SetActive(false);
        closeBut.SetActive(true);
        StartListening();
    }

    public void CloseBrowserUI()
    {
        StopListening();
        ClearRoomButtons();
        discoveredRooms.Clear();
        selectedRoom = null;
        openBut.SetActive(true);
        closeBut.SetActive(false);

        if (browserUIRoot) browserUIRoot.SetActive(false);
        SetStatus("");
    }

    public void ConnectSelectedRoom()
    {
        if (selectedRoom == null)
        {
            SetStatus("Select A Room First");
            return;
        }

        if (livekitManager == null)
        {
            SetStatus("Missing LiveKit Manager");
            return;
        }

        string password = passwordInput != null ? passwordInput.text : "";
        string newIdentity = identityInput != null ? identityInput.text.Trim() : "";

        if (string.IsNullOrWhiteSpace(newIdentity))
            newIdentity = "sender";

        livekitManager.identity = newIdentity;
        livekitManager.participantName = newIdentity;

        livekitManager.url = $"ws://{selectedRoom.ip}:{selectedRoom.livekitPort}";
        livekitManager.tokenServerUrl = $"http://{selectedRoom.ip}:{selectedRoom.tokenPort}/token";
        livekitManager.roomName = selectedRoom.roomName;
        livekitManager.password = password;
        livekitManager.token = "";

        SetStatus("Connecting...");
        StartCoroutine(ConnectRoutine());
    }

    void StartListening()
    {
        StopListening();

        udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, listenPort));

        listenRoutine = StartCoroutine(ListenLoop());
    }

    void StopListening()
    {
        if (listenRoutine != null)
        {
            StopCoroutine(listenRoutine);
            listenRoutine = null;
        }

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
    }

    IEnumerator ListenLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (true)
        {
            while (udpClient != null && udpClient.Available > 0)
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(data);

                ViewerHostBroadcastPacket packet = JsonUtility.FromJson<ViewerHostBroadcastPacket>(json);
                if (packet == null) continue;
                if (packet.type != "viewerHost") continue;
                if (string.IsNullOrWhiteSpace(packet.roomName)) continue;
                if (string.IsNullOrWhiteSpace(packet.ip)) continue;

                string key = packet.ip + "|" + packet.roomName;

                if (!discoveredRooms.TryGetValue(key, out var room))
                {
                    room = new DiscoveredRoom();
                    room.key = key;
                    discoveredRooms[key] = room;
                }

                room.roomName = packet.roomName;
                room.ip = packet.ip;
                room.tokenPort = packet.tokenPort;
                room.livekitPort = packet.livekitPort;
                room.lastSeenTime = Time.unscaledTime;

                RebuildRoomButtons();
            }

            RemoveStaleRooms();
            yield return null;
        }
    }

    void RemoveStaleRooms()
    {
        float now = Time.unscaledTime;
        bool changed = false;

        List<string> keysToRemove = new List<string>();

        foreach (var kvp in discoveredRooms)
        {
            if (now - kvp.Value.lastSeenTime > staleAfterSeconds)
                keysToRemove.Add(kvp.Key);
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            discoveredRooms.Remove(keysToRemove[i]);
            changed = true;
        }

        if (changed)
            RebuildRoomButtons();
    }

    void RebuildRoomButtons()
    {
        ClearRoomButtons();

        foreach (var kvp in discoveredRooms)
        {
            DiscoveredRoom room = kvp.Value;

            Button btn = Instantiate(roomButtonPrefab, roomListParent);
            spawnedButtons.Add(btn);

            TMP_Text txt = btn.GetComponentInChildren<TMP_Text>(true);
            if (txt != null)
                txt.text = room.roomName;

            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                selectedRoom = room;
                SetStatus("Selected: " + room.roomName);
            });
        }
    }

    void ClearRoomButtons()
    {
        for (int i = 0; i < spawnedButtons.Count; i++)
        {
            if (spawnedButtons[i] != null)
                Destroy(spawnedButtons[i].gameObject);
        }

        spawnedButtons.Clear();
    }

    IEnumerator ConnectRoutine()
    {
        livekitManager.OnClickHangup();
        livekitManager.StartManualConnectAndPublish();

        float timeout = 8f;

        while (timeout > 0f)
        {
            if (livekitManager.IsConnected)
            {
                SetStatus("Connect Success");
                yield break;
            }

            timeout -= Time.deltaTime;
            yield return null;
        }

        SetStatus("Connect Failed");
    }

    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    void OnDestroy()
    {
        StopListening();
        ClearRoomButtons();
    }
}