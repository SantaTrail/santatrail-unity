using TMPro;
using UnityEngine;
using System.Collections;

public class WaitingUI : MonoBehaviour
{
    public GameObject panel;
    public TMP_Text loadingText;

    Coroutine loadingCoroutine;

    public void Show()
    {
        panel.SetActive(true);

        if (loadingCoroutine != null)
            StopCoroutine(loadingCoroutine);

        loadingCoroutine = StartCoroutine(AnimateLoading());
    }

    public void Hide()
    {
        if (loadingCoroutine != null)
            StopCoroutine(loadingCoroutine);

        panel.SetActive(false);
    }

    IEnumerator AnimateLoading()
    {
        float seconds = 0;
        while(true)
        {
            seconds += Time.deltaTime;

            loadingText.text =
                $"Preparing letter... {seconds:F1}s";

            yield return null;
        }
    }
}