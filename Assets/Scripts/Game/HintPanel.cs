using TMPro;
using UnityEngine;

public class HintPanel : MonoBehaviour
{
    [Header("UI")]
    public GameObject panel;

    public TMP_Text hintText;

    //-------------------------------------------------

    public void ShowHint(string clue)
    {
        panel.SetActive(true);

        hintText.text =
            "Hint\n\n" + clue;
    }

    //-------------------------------------------------

    public void Hide()
    {
        panel.SetActive(false);
    }
}