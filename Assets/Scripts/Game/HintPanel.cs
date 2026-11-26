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
        BehavioralAnalyticsCollector.AddEvent("hint_shown", "{\"hint_type\":\"panel\"}");
        panel.SetActive(true);

        hintText.text =
            "Hint\n\n" + clue;
    }

    //-------------------------------------------------

    public void Hide()
    {
        BehavioralAnalyticsCollector.AddEvent("hint_dismissed", "{\"hint_type\":\"panel\"}");
        panel.SetActive(false);
    }
}
