using TMPro;
using UnityEngine;

public class LetterUI : MonoBehaviour
{
    public TMP_Text childName;

    public TMP_Text childAge;

    public TMP_Text letter;

    public void UpdateUI(
        ChildData child,
        string aiLetter)
    {
        childName.text =
            child.name.ToUpper();

        childAge.text =
            "AGE " + child.age;

        letter.text =
            aiLetter;
    }
}