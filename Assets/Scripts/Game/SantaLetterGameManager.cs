using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum Difficulty
{
    Easy,
    Medium,
    Hard
}

public class SantaLetterGameManager : MonoBehaviour
{
    [Header("Systems")]
    public ChildGenerator childGenerator;
    public LetterGenerator letterGenerator;
    public LetterUI letterUI;
    public HintPanel hintPanel;
    public DeliveryManager deliveryManager;
    public ToyLoader toyLoader;

    private ToyData[] allToys;

    private ChildData currentChild;
    private ToyData selectedToy;

    private int wrongAttempts = 0;
    private int totalDeliveries;

    public Difficulty difficulty;

    [Header("Gift Buttons")]
    public GiftButton[] giftButtons;

    [Header("Toy Database")]
    public ResultPopup resultPopup;

    [SerializeField]
    private string level1SceneName = "PreviewLV";

    [SerializeField]
    private float correctAnswerTransitionDelay = 2f;

    [Header("Mission UI")]
    public MissionProgressUI missionUI;

    [Header("Buttons")]
    public Button packPresentButton;
    public Button retryButton;

    [Header("Background AI")]
    [SerializeField]
    private bool preloadNextLetter = true;

    private Task<PreparedDelivery> nextDeliveryTask;
    private PreparedDelivery preparedNextDelivery;

    private bool isPreparingNextDelivery;
    private bool isAdvancing;

    // ============================================================
    // PREPARED DELIVERY
    // ============================================================

    private class PreparedDelivery
    {
        public ChildData child;
        public string letter;
        public ToyData[] giftChoices;

        public PreparedDelivery(
            ChildData child,
            string letter,
            ToyData[] giftChoices)
        {
            this.child = child;
            this.letter = letter;
            this.giftChoices = giftChoices;
        }
    }

    // ============================================================
    // START
    // ============================================================

    private async void Start()
    {
        allToys = toyLoader.toys;

        if (letterGenerator != null &&
            letterUI != null)
        {
            letterGenerator.ui = letterUI;
        }

        if (deliveryManager != null)
        {
            deliveryManager.santaLetterGameManager = this;
        }

        if (GameManager.hasActiveLevelSettings)
        {
            difficulty =
                (Difficulty)GameManager.activeMissionDifficulty;
        }

        totalDeliveries =
            GetDeliveryCount();

        if (deliveryManager != null)
        {
            deliveryManager.totalDeliveries =
                totalDeliveries;
        }

        if (missionUI != null)
        {
            missionUI.SetupMission(
                totalDeliveries
            );
        }

        Debug.Log(
            "Number of Toys = " +
            allToys.Length
        );

        foreach (ToyData toy in allToys)
        {
            Debug.Log(
                "Loaded Toy: " +
                toy.name
            );
        }

        // --------------------------------------------------------
        // FIRST DELIVERY
        // --------------------------------------------------------

        PreparedDelivery firstDelivery =
            await CreateDeliveryAsync();

        if (firstDelivery == null)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "could not prepare first delivery."
            );

            return;
        }

        ShowDelivery(
            firstDelivery
        );

        // --------------------------------------------------------
        // START PREPARING NEXT LETTER IMMEDIATELY
        // --------------------------------------------------------

        StartPreparingNextDelivery();
    }

    // ============================================================
    // CREATE DELIVERY
    // ============================================================

    private async Task<PreparedDelivery>
        CreateDeliveryAsync()
    {
        Debug.Log(
            "===== Creating Delivery ====="
        );

        if (toyLoader == null ||
            childGenerator == null ||
            letterGenerator == null)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "required references are missing."
            );

            return null;
        }

        if (allToys == null ||
            allToys.Length == 0)
        {
            allToys =
                toyLoader.toys;
        }

        if (allToys == null ||
            allToys.Length == 0)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "no toys are available."
            );

            return null;
        }

        ToyData targetToy =
            toyLoader.GetRandomToy();

        if (targetToy == null)
        {
            return null;
        }

        ChildData child =
            childGenerator.Generate(
                targetToy
            );

        if (child == null)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "child generation failed."
            );

            return null;
        }

        Debug.Log(
            "Child Generated: " +
            child.name
        );

        // --------------------------------------------------------
        // AI GENERATION
        // --------------------------------------------------------

        string letter =
            await letterGenerator.GenerateLetterText(
                child
            );

        if (string.IsNullOrWhiteSpace(letter))
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "letter generation failed."
            );

            return null;
        }

        // --------------------------------------------------------
        // GIFT CHOICES ARE GENERATED BEFORE THE DELIVERY STARTS
        // --------------------------------------------------------

        ToyData[] giftChoices =
            GenerateGiftChoicesForChild(
                child
            );

        if (giftChoices == null ||
            giftChoices.Length == 0)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "gift choices could not be generated."
            );

            return null;
        }

        return new PreparedDelivery(
            child,
            letter,
            giftChoices
        );
    }

    // ============================================================
    // SHOW DELIVERY
    // ============================================================

    private void ShowDelivery(
        PreparedDelivery delivery)
    {
        if (delivery == null ||
            delivery.child == null)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "invalid delivery."
            );

            return;
        }

        currentChild =
            delivery.child;

        selectedToy = null;
        wrongAttempts = 0;

        if (packPresentButton != null)
        {
            packPresentButton.interactable =
                false;
        }

        if (letterUI != null)
        {
            letterUI.UpdateUI(
                delivery.child,
                delivery.letter
            );
        }

        ApplyGiftChoices(
            delivery.giftChoices
        );

        Debug.Log(
            "===== DELIVERY READY ====="
        );

        Debug.Log(
            "Child: " +
            currentChild.name
        );

        Debug.Log(
            "Target Toy: " +
            currentChild.targetToy.name
        );
    }

    // ============================================================
    // START BACKGROUND PREPARATION
    // ============================================================

    private void StartPreparingNextDelivery()
    {
        if (!preloadNextLetter)
        {
            return;
        }

        if (isPreparingNextDelivery)
        {
            return;
        }

        isPreparingNextDelivery = true;

        Debug.Log(
            "===== START BACKGROUND LETTER GENERATION ====="
        );

        nextDeliveryTask =
            CreateDeliveryAsync();

        _ = FinishPreparingNextDeliveryAsync(
            nextDeliveryTask
        );
    }

    // ============================================================
    // FINISH BACKGROUND PREPARATION
    // ============================================================

    private async Task FinishPreparingNextDeliveryAsync(
        Task<PreparedDelivery> task)
    {
        try
        {
            PreparedDelivery delivery =
                await task;

            preparedNextDelivery =
                delivery;

            if (delivery != null)
            {
                Debug.Log(
                    "===== NEXT DELIVERY READY ====="
                );
            }
            else
            {
                Debug.LogWarning(
                    "SantaLetterGameManager: " +
                    "next delivery preparation failed."
                );
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogException(
                exception
            );

            preparedNextDelivery =
                null;
        }
        finally
        {
            isPreparingNextDelivery =
                false;
        }
    }

    // ============================================================
    // START NEW DELIVERY
    // ============================================================

    public async void StartNewDelivery()
    {
        PreparedDelivery delivery =
            await GetNextDeliveryAsync();

        if (delivery == null)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "could not prepare a new delivery."
            );

            return;
        }

        ShowDelivery(
            delivery
        );

        StartPreparingNextDelivery();
    }

    // ============================================================
    // GET NEXT DELIVERY
    // ============================================================

    private async Task<PreparedDelivery>
        GetNextDeliveryAsync()
    {
        // --------------------------------------------------------
        // NEXT LETTER ALREADY FINISHED
        // --------------------------------------------------------

        if (preparedNextDelivery != null)
        {
            PreparedDelivery delivery =
                preparedNextDelivery;

            preparedNextDelivery =
                null;

            nextDeliveryTask =
                null;

            return delivery;
        }

        // --------------------------------------------------------
        // NEXT LETTER IS STILL GENERATING
        // --------------------------------------------------------

        if (isPreparingNextDelivery &&
            nextDeliveryTask != null)
        {
            Debug.Log(
                "Next letter is still generating..."
            );

            PreparedDelivery delivery =
                await nextDeliveryTask;

            preparedNextDelivery =
                null;

            nextDeliveryTask =
                null;

            isPreparingNextDelivery =
                false;

            return delivery;
        }

        // --------------------------------------------------------
        // NO BACKGROUND GENERATION
        // --------------------------------------------------------

        return await CreateDeliveryAsync();
    }

    // ============================================================
    // COMPATIBILITY WITH OLD CODE
    // ============================================================

    public async Task<bool>
        PrepareNewDeliveryAsync()
    {
        PreparedDelivery delivery =
            await GetNextDeliveryAsync();

        if (delivery == null)
        {
            return false;
        }

        ShowDelivery(
            delivery
        );

        StartPreparingNextDelivery();

        return true;
    }

    // ============================================================
    // GIFT CHOICES
    // ============================================================

    private ToyData[] GenerateGiftChoicesForChild(
        ChildData child)
    {
        if (child == null ||
            child.targetToy == null ||
            giftButtons == null ||
            giftButtons.Length == 0)
        {
            return null;
        }

        List<ToyData> pool =
            new List<ToyData>();

        foreach (ToyData toy in allToys)
        {
            if (toy == null)
            {
                continue;
            }

            if (toy.minAge <= child.age &&
                toy.maxAge >= child.age)
            {
                pool.Add(toy);
            }
        }

        if (pool.Count <
            giftButtons.Length)
        {
            pool =
                new List<ToyData>(
                    allToys
                );
        }

        List<ToyData> choices =
            new List<ToyData>();

        choices.Add(
            child.targetToy
        );

        pool.Remove(
            child.targetToy
        );

        while (
            choices.Count <
            giftButtons.Length &&
            pool.Count > 0
        )
        {
            int index =
                Random.Range(
                    0,
                    pool.Count
                );

            ToyData toy =
                pool[index];

            if (!choices.Contains(toy))
            {
                choices.Add(toy);
            }

            pool.RemoveAt(
                index
            );
        }

        Shuffle(
            choices
        );

        return choices.ToArray();
    }

    private void ApplyGiftChoices(
        ToyData[] choices)
    {
        if (choices == null ||
            giftButtons == null)
        {
            return;
        }

        int count =
            Mathf.Min(
                choices.Length,
                giftButtons.Length
            );

        for (int i = 0; i < count; i++)
        {
            if (giftButtons[i] == null ||
                choices[i] == null)
            {
                continue;
            }

            Debug.Log(
                $"Button {i + 1}: " +
                choices[i].name
            );

            giftButtons[i].Setup(
                choices[i],
                this
            );
        }
    }

    // ============================================================
    // SELECT GIFT
    // ============================================================

    public void SelectGift(
        ToyData toy)
    {
        if (toy == null)
        {
            return;
        }

        selectedToy =
            toy;

        Debug.Log(
            "Selected " +
            toy.name
        );

        if (packPresentButton != null)
        {
            packPresentButton.interactable =
                true;
        }
    }

    // ============================================================
    // PACK PRESENT
    // ============================================================

    public void PackPresent()
    {
        if (selectedToy == null ||
            currentChild == null)
        {
            return;
        }

        if (
            selectedToy.id ==
            currentChild.targetToy.id
        )
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayCorrect();
            }

            if (resultPopup != null)
            {
                resultPopup.ShowCorrect();
            }

            if (packPresentButton != null)
            {
                packPresentButton.interactable =
                    false;
            }

            if (correctAnswerTransitionDelay > 0f)
            {
                Invoke(
                    nameof(AdvanceCorrectDelivery),
                    correctAnswerTransitionDelay
                );
            }
            else
            {
                AdvanceCorrectDelivery();
            }
        }
        else
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayWrong();
            }

            if (resultPopup != null)
            {
                resultPopup.ShowWrong();
            }

            ShowNextHint();
        }
    }

    // ============================================================
    // ADVANCE DELIVERY
    // ============================================================

    private async void AdvanceCorrectDelivery()
    {
        if (isAdvancing)
        {
            return;
        }

        isAdvancing = true;

        if (deliveryManager != null)
        {
            deliveryManager.CompleteDelivery();

            isAdvancing = false;
            return;
        }

        PreparedDelivery next =
            await GetNextDeliveryAsync();

        if (next == null)
        {
            Debug.LogError(
                "SantaLetterGameManager: " +
                "no next delivery available."
            );

            isAdvancing = false;
            return;
        }

        ShowDelivery(
            next
        );

        StartPreparingNextDelivery();

        isAdvancing = false;
    }

    // ============================================================
    // HINT
    // ============================================================

    private void ShowNextHint()
    {
        if (currentChild == null ||
            currentChild.targetToy == null ||
            hintPanel == null)
        {
            return;
        }

        string[] clues;

        switch (currentChild.difficulty)
        {
            case "medium":
                clues =
                    currentChild.targetToy.mediumClues;
                break;

            case "hard":
                clues =
                    currentChild.targetToy.hardClues;
                break;

            default:
                clues =
                    currentChild.targetToy.easyClues;
                break;
        }

        if (clues == null)
        {
            return;
        }

        if (wrongAttempts < clues.Length)
        {
            hintPanel.ShowHint(
                clues[wrongAttempts]
            );
        }

        wrongAttempts++;
    }

    // ============================================================
    // SHUFFLE
    // ============================================================

    private void Shuffle(
        List<ToyData> list)
    {
        for (
            int i = 0;
            i < list.Count;
            i++
        )
        {
            int r =
                Random.Range(
                    i,
                    list.Count
                );

            ToyData temp =
                list[i];

            list[i] =
                list[r];

            list[r] =
                temp;
        }
    }

    // ============================================================
    // DELIVERY COUNT
    // ============================================================

    private int GetDeliveryCount()
    {
        if (
            GameManager.hasActiveDeliveryCount &&
            GameManager.activeDeliveryCount > 0
        )
        {
            return Mathf.Max(
                1,
                GameManager.activeDeliveryCount
            );
        }

        switch (difficulty)
        {
            case Difficulty.Easy:
                return 3;

            case Difficulty.Medium:
                return 5;

            case Difficulty.Hard:
                return 8;

            default:
                return 5;
        }
    }

    // ============================================================
    // RETRY
    // ============================================================

    public void Retry()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayClick();
        }

        selectedToy = null;

        wrongAttempts = 0;

        if (packPresentButton != null)
        {
            packPresentButton.interactable =
                false;
        }

        if (hintPanel != null)
        {
            hintPanel.Hide();
        }
    }
}
