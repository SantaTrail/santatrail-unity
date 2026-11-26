using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Creates the editable visual-novel layout in the scene. The generated
/// objects stay in the Hierarchy, so their RectTransforms can be adjusted.
[ExecuteAlways]
public sealed class VisualNovelSceneAuthoring : MonoBehaviour
{
    private void OnEnable()
    {
        if (transform.Find("Visual Novel Canvas") == null)
        {
            BuildLayout();
        }
    }

    [ContextMenu("Rebuild Visual Novel Layout")]
    private void BuildLayout()
    {
        Transform old = transform.Find("Visual Novel Canvas");
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old.gameObject);
            else DestroyImmediate(old.gameObject);
        }

        GameObject canvasObject = new GameObject("Visual Novel Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        GameObject background = CreateImage("Background (black by default)", canvasObject.transform, Color.black);
        Stretch(background.GetComponent<RectTransform>());

        GameObject portrait = CreateImage("Current Speaker Portrait", canvasObject.transform, Color.white);
        Rect(portrait.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0, 0), new Vector2(390, 330), new Vector2(430, 620));
        portrait.GetComponent<Image>().preserveAspect = true;
        portrait.SetActive(false);

        GameObject chapter = CreateText("Chapter", canvasObject.transform, 34, TextAlignmentOptions.Center, new Color(1, .88f, .36f));
        Rect(chapter.GetComponent<RectTransform>(), new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -72), new Vector2(1100, 60));

        GameObject bubble = CreateImage("Comic Dialogue Bubble", canvasObject.transform, new Color(1, .98f, .89f));
        Rect(bubble.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 215), new Vector2(1500, 390));
        AddOutline(bubble, new Color(.1f, .24f, .22f), 9);

        GameObject plate = CreateImage("Speaker Plate", bubble.transform, new Color(.12f, .45f, .34f));
        Rect(plate.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -48), new Vector2(390, 68));
        GameObject speaker = CreateText("Speaker", plate.transform, 30, TextAlignmentOptions.Center, Color.white);
        StretchWithOffsets(speaker.GetComponent<RectTransform>(), new Vector2(10, 4), new Vector2(-10, -4));

        GameObject dialogue = CreateText("Dialogue", bubble.transform, 38, TextAlignmentOptions.TopLeft, new Color(.08f, .16f, .18f));
        Rect(dialogue.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -10), new Vector2(1300, 220));
        GameObject hint = CreateText("Hint", bubble.transform, 23, TextAlignmentOptions.BottomRight, new Color(.12f, .45f, .34f));
        hint.GetComponent<TextMeshProUGUI>().text = "CLICK / SPACE TO CONTINUE  ▸";
        Rect(hint.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-70, 34), new Vector2(460, 40));
        bubble.AddComponent<Button>().targetGraphic = bubble.GetComponent<Image>();

        for (int i = 0; i < 2; i++)
        {
            GameObject choice = CreateImage("Choice " + (i + 1), canvasObject.transform, i == 0 ? new Color(.17f, .49f, .36f) : new Color(.22f, .36f, .64f));
            Rect(choice.GetComponent<RectTransform>(), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, i == 0 ? 515 : 425), new Vector2(940, 70));
            GameObject label = CreateText("Label", choice.transform, 27, TextAlignmentOptions.Center, Color.white);
            label.GetComponent<TextMeshProUGUI>().text = "Choice";
            StretchWithOffsets(label.GetComponent<RectTransform>(), new Vector2(12, 4), new Vector2(-12, -4));
            choice.AddComponent<Button>();
            choice.SetActive(false);
        }

        GameObject skip = CreateImage("Skip", canvasObject.transform, new Color(.17f, .29f, .34f));
        Rect(skip.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-145, -70), new Vector2(220, 52));
        GameObject skipLabel = CreateText("Label", skip.transform, 27, TextAlignmentOptions.Center, Color.white);
        skipLabel.GetComponent<TextMeshProUGUI>().text = "SKIP OPENING";
        StretchWithOffsets(skipLabel.GetComponent<RectTransform>(), new Vector2(12, 4), new Vector2(-12, -4));
        skip.AddComponent<Button>();

#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<Image>().color = color;
        return obj;
    }

    private static GameObject CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return obj;
    }

    private static void Rect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    { rect.anchorMin = min; rect.anchorMax = max; rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
    private static void StretchWithOffsets(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
    private static void AddOutline(GameObject obj, Color color, float distance)
    { Outline outline = obj.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(distance, -distance); }
}
