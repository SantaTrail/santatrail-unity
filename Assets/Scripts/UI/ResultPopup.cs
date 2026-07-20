using TMPro;
using UnityEngine;

public class ResultPopup : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text resultText;

    void Start()
    {
        panel.SetActive(false);
    }

    public void ShowCorrect()
    {
        resultText.text = "🎉 Correct!";
        panel.SetActive(true);

        Invoke(nameof(Hide), 2f);
    }

    public void ShowWrong()
    {
        resultText.text = "❌ Wrong!\nTry again!";
        panel.SetActive(true);

        Invoke(nameof(Hide), 1.5f);
    }

    void Hide()
    {
        panel.SetActive(false);
    }
}