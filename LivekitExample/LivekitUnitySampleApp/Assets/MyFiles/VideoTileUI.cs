using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VideoTileUI : MonoBehaviour
{
    public RawImage videoImage;
    public Button btnIn, btnOut, btnAudio;
    public TMP_Text nameText;

    public void SetLabel(string text)
    {
        if (nameText != null)
            nameText.text = text;
    }

    public void SetTexture(Texture tex)
    {
        if (videoImage != null)
            videoImage.texture = tex;
    }
}