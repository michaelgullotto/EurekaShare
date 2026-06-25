using System.IO;
using UnityEditor;
using UnityEngine;

public static class LivekitConfigQuickSet
{
    static string ConfigPath => Path.Combine(Application.dataPath, "StreamingAssets", "livekit_config.json");

    [MenuItem("Tools/Eureka Share/Config/Set View")]
    static void SetView()
    {
        LivekitAppConfig cfg = LoadConfig();
        cfg.mode = "view";
        cfg.identity = "pcView";
        SaveConfig(cfg);

        Debug.Log("[LivekitConfigQuickSet] Set config to VIEW / pcView");
    }

    [MenuItem("Tools/Eureka Share/Config/Set Pub")]
    static void SetPub()
    {
        LivekitAppConfig cfg = LoadConfig();
        cfg.mode = "pub";
        cfg.identity = "sender";
        SaveConfig(cfg);

        Debug.Log("[LivekitConfigQuickSet] Set config to PUB / sender");
    }

    static LivekitAppConfig LoadConfig()
    {
        string json = File.ReadAllText(ConfigPath);
        return JsonUtility.FromJson<LivekitAppConfig>(json);
    }

    static void SaveConfig(LivekitAppConfig cfg)
    {
        string json = JsonUtility.ToJson(cfg, true);
        File.WriteAllText(ConfigPath, json);
        AssetDatabase.Refresh();
    }
}