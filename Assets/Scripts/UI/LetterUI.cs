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

    public void UpdateUI(ChildData child, string aiLetter)
    {
        ApplyFont(child.age);

        childName.text = child.name.ToUpper();
        childAge.text = "AGE " + child.age;
        letter.text = aiLetter;
    }

    void ApplyFont(int age)
    {
        TMP_FontAsset selectedFont;
        
        if (age <= 4)
        {
            selectedFont = age3to4Font;
            // for ajusting the font space for realistic
            letter.fontSize = 30;
            letter.lineSpacing = 1;
        }

        else if (age <= 6)
        {
            selectedFont = age5to6Font;
            letter.fontSize = 30;
            letter.lineSpacing = 0.1f;
        }

        else if (age <= 8)
        {
            selectedFont = age7to8Font;
            letter.fontSize = 30;
            letter.lineSpacing = 1;
            childName.fontSize = 50;
            childAge.fontSize = 50;
        }

        else
        {
            selectedFont = age9to11Font;
            letter.fontSize = 20;
            letter.lineSpacing = 1;
        }

        // Apply font
        letter.font = selectedFont;
        childName.font = selectedFont;
        childAge.font = selectedFont;

        // Set font sizes
        childName.fontSize = 30;
        childAge.fontSize = 30;
    }
}