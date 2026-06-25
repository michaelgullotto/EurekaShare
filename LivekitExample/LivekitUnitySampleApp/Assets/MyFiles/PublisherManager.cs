using UnityEngine;

public class PublisherManager : MonoBehaviour
{
    [SerializeField] private Camera _captureCamera;
    [SerializeField] private GameObject obj_BG;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RandomColor();

        Debug.Log("persistentDataPath = " + Application.persistentDataPath);
    }

    private void RandomColor()
    {
        obj_BG.GetComponent<MeshRenderer>().material.color = Random.ColorHSV();
    }

    private void StreamCameraSettings()
    {
        _captureCamera.allowHDR = false;
        _captureCamera.allowMSAA = false;
        _captureCamera.depthTextureMode = DepthTextureMode.None;
    }
}
