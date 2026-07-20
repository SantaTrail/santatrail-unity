using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GiftButton : MonoBehaviour
{
    [Header("UI")]
    public Image toyImage;
    public TMP_Text toyName;

    private ToyData toy;
    private SantaLetterGameManager santaLetterGameManager;

    //-------------------------------------------------

    public void Setup(
        ToyData newToy,
        SantaLetterGameManager manager)
    {
        toy = newToy;
        santaLetterGameManager = manager;

        if (toyName != null)
            if (toyName != null)
                {
                    toyName.text = "";
                }

        if (toyImage != null)
            toyImage.sprite = toy.icon;
    }

    //-------------------------------------------------

    public void OnClick()
    {
        santaLetterGameManager.SelectGift(toy);
    }
}