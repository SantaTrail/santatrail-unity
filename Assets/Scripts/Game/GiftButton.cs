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

    /// <summary>
    /// The choice currently represented by this button. The first-run guide
    /// uses this to point at the correct present after the choices are shuffled.
    /// </summary>
    public ToyData AssignedToy => toy;

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
        AudioManager.Instance.PlayClick();  // clicking sound
        santaLetterGameManager.SelectGift(toy);
    }
}
