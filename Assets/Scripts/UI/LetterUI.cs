using TMPro;
using UnityEngine;

public class LetterUI : MonoBehaviour
{
    public TMP_Text childName;
    public TMP_Text childAge;
    public TMP_Text letter;

    [Header("Fonts")]
    public TMP_FontAsset age3to4Font;
    public TMP_FontAsset age5to6Font;
    public TMP_FontAsset age7to8Font;
    public TMP_FontAsset age9to11Font;

    public void UpdateUI(
        ChildData child,
        string aiLetter)
    {
        if (child == null)
        {
            return;
        }

        ApplyFont(
            child.age
        );

        if (childName != null)
        {
            childName.SetText(
                child.name.ToUpper()
            );
        }

        if (childAge != null)
        {
            childAge.SetText(
                "AGE {0}",
                child.age
            );
        }

        if (letter != null)
        {
            letter.SetText(
                aiLetter ?? ""
            );

            letter.ForceMeshUpdate();
        }
    }

    private void ApplyFont(
        int age)
    {
        TMP_FontAsset selectedFont;

        if (age <= 4)
        {
            selectedFont =
                age3to4Font;

            if (letter != null)
            {
                letter.fontSize = 30;
                letter.lineSpacing = 1;
            }
        }
        else if (age <= 6)
        {
            selectedFont =
                age5to6Font;

            if (letter != null)
            {
                letter.fontSize = 30;
                letter.lineSpacing = 0.1f;
            }
        }
        else if (age <= 8)
        {
            selectedFont =
                age7to8Font;

            if (letter != null)
            {
                letter.fontSize = 30;
                letter.lineSpacing = 1;
            }
        }
        else
        {
            selectedFont =
                age9to11Font;

            if (letter != null)
            {
                letter.fontSize = 20;
                letter.lineSpacing = 1;
            }
        }

        if (letter != null)
        {
            letter.font =
                selectedFont;

            letter.fontSize =
                age > 8 ? 20 : 30;
        }

        if (childName != null)
        {
            childName.font =
                selectedFont;

            childName.fontSize =
                30;
        }

        if (childAge != null)
        {
            childAge.font =
                selectedFont;

            childAge.fontSize =
                30;
        }
    }
}