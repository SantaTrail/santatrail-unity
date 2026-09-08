using TMPro;
using UnityEngine;

public class ResultPopup : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text resultText;
    private bool showRequested;

    void Awake()
    {
        if (panel == null)
        {
            panel = gameObject;
        }

        // This component is attached to the panel, which normally starts
        // inactive. When ShowCorrect/ShowWrong activates it for the first
        // time, Awake runs during SetActive. Preserve that requested popup
        // instead of immediately hiding it.
        if (!showRequested)
        {
            panel.SetActive(false);
        }
    }

    public void ShowCorrect()
    {
        Show("Correct!", 2f);
    }

    public void ShowWrong()
    {
        Show("Wrong!\nTry again!", 1.5f);
    }

    private void Show(string message, float duration)
    {
        showRequested = true;
        CancelInvoke(nameof(Hide));

        if (resultText != null)
        {
            resultText.text = message;
        }

        panel.SetActive(true);
        Invoke(nameof(Hide), duration);
    }

    void Hide()
    {
        showRequested = false;
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }
}
