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

    // public void Setup(
    //     ToyData newToy,
    //     SantaLetterGameManager manager)
    // {
    //     toy = newToy;
    //     santaLetterGameManager = manager;

    //     if (toyName != null)
    //     {
    //         toyName.text = toy.name;
    //     }

    //     if (toyImage != null && toy.icon != null)
    //     {
    //         toyImage.sprite = toy.icon;
    //     }
    // }

    public void Setup(
        ToyData newToy,
        SantaLetterGameManager manager)
    {
        Debug.Log("GiftButton.Setup called");

        toy = newToy;
        santaLetterGameManager = manager;

        Debug.Log("Toy = " + toy.name);

        if (toyName == null)
        {
            Debug.LogError("toyName is NULL!");
        }
        else
        {
            toyName.text = toy.name;
            Debug.Log("Button text = " + toyName.text);
        }

        if (toyImage != null)
        {
            if(toyImage != null)
            {
                toyImage.enabled = toy.icon != null;

                toyImage.sprite = toy.icon;
            }
        }
    }

    //-------------------------------------------------

    public void OnClick()
    {
        santaLetterGameManager.SelectGift(toy);
    }
}