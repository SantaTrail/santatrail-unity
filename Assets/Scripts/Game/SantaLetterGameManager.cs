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

    [Header("Gift Buttons")]
    public GiftButton[] giftButtons;

    [Header("Toy Database")]
    // public ToyData[] allToys;
    public ResultPopup resultPopup;
    [SerializeField] private string level1SceneName = "PreviewLV";
    [SerializeField] private float correctAnswerTransitionDelay = 2f;

    private ChildData currentChild;

    private int wrongAttempts = 0;
    public Difficulty difficulty;
    private int totalDeliveries;
    private ToyData selectedToy;

    [Header("Mission UI")]
    public MissionProgressUI missionUI;

    [Header("Buttons")]
    public Button packPresentButton;
    public Button retryButton;

    //----------------------------------------------------

    void Start()
    {
        allToys = toyLoader.toys;

        if (letterGenerator != null && letterUI != null)
        {
            letterGenerator.ui = letterUI;
        }

        if (deliveryManager != null)
        {
            deliveryManager.santaLetterGameManager = this;
        }

        if (GameManager.hasActiveLevelSettings)
        {
            difficulty = (Difficulty)GameManager.activeMissionDifficulty;
        }

        totalDeliveries = GetDeliveryCount();
        if (deliveryManager != null)
        {
            deliveryManager.totalDeliveries = totalDeliveries;
        }
        missionUI.SetupMission(totalDeliveries);

        Debug.Log("Number of Toys = " + allToys.Length);

        foreach (ToyData toy in allToys)
        {
            Debug.Log("Loaded Toy: " + toy.name);
        }

        if (SantaLetterPreloadSession.IsPreparing)
        {
            Debug.Log("SantaLetterGameManager: waiting for LoadingScene to prepare the first delivery.");
            return;
        }

        StartNewDelivery();
    }

    public async void StartNewDelivery()
    {
        bool prepared = await PrepareNewDeliveryAsync();
        if (!prepared)
        {
            Debug.LogError("SantaLetterGameManager: could not prepare a new delivery.");
        }
    }

    /// <summary>
    /// Generates all data needed by the letter screen and completes only when
    /// the letter UI and every gift button have been populated.
    /// </summary>
    public async Task<bool> PrepareNewDeliveryAsync()
    {
        Debug.Log("===== Prepare New Delivery =====");

        Debug.Log("hintPanel = " + hintPanel);
        Debug.Log("childGenerator = " + childGenerator);
        Debug.Log("letterGenerator = " + letterGenerator);

        if (toyLoader == null || childGenerator == null || letterGenerator == null)
        {
            Debug.LogError("SantaLetterGameManager: letter-generation references are incomplete.");
            return false;
        }

        if (allToys == null || allToys.Length == 0)
        {
            allToys = toyLoader.toys;
        }

        if (allToys == null || allToys.Length == 0)
        {
            Debug.LogError("SantaLetterGameManager: no toys are available for the letter.");
            return false;
        }

        wrongAttempts = 0;
        selectedToy = null;
        if (packPresentButton != null)
        {
            packPresentButton.interactable = false;
        }

        ToyData targetToy = toyLoader.GetRandomToy();
        if (targetToy == null)
        {
            return false;
        }

        currentChild = childGenerator.Generate(targetToy);
        if (currentChild == null)
        {
            Debug.LogError("SantaLetterGameManager: child generation returned no child.");
            return false;
        }

        Debug.Log("Child Generated: " + currentChild.name);

        bool letterGenerated = await letterGenerator.GenerateLetter(currentChild);
        if (!letterGenerated)
        {
            Debug.LogError("SantaLetterGameManager: letter generation failed.");
            return false;
        }

        GenerateGiftChoices();
        return true;
    }

    //----------------------------------------------------

    void GenerateGiftChoices()
    {
        Debug.Log("GenerateGiftChoices()");

        List<ToyData> pool = new List<ToyData>();

        foreach(ToyData toy in allToys)
        {
            if(toy.minAge <= currentChild.age &&
            toy.maxAge >= currentChild.age)
            {
                pool.Add(toy);
            }
        }

        if(pool.Count < giftButtons.Length)
        {
            pool = new List<ToyData>(allToys);
        }

        List<ToyData> choices =
            new List<ToyData>();

        choices.Add(currentChild.targetToy);

        pool.Remove(currentChild.targetToy);

        while (choices.Count < giftButtons.Length)
        {
            int index =
                Random.Range(0, pool.Count);

            ToyData toy = pool[index];
            if(!choices.Contains(toy))
            {
                choices.Add(toy);
            }

            pool.RemoveAt(index);
        }

        Shuffle(choices);

        for (int i = 0; i < giftButtons.Length; i++)
        {
            Debug.Log($"Button {i + 1}: {choices[i].name}");

            giftButtons[i].Setup(
                choices[i],
                this
            );
        }
    }

    public void SelectGift(ToyData toy)
    {
        selectedToy = toy;

        Debug.Log("Selected " + toy.name);

        // Enable Pack Present button
        if (packPresentButton != null)
        {
            packPresentButton.interactable = true;
        }
    }

    public void PackPresent()
    {
        if (selectedToy == null)
        return;

        if(selectedToy.id == currentChild.targetToy.id)
        {
            AudioManager.Instance.PlayCorrect();

            resultPopup.ShowCorrect();
            packPresentButton.interactable = false;

            if (correctAnswerTransitionDelay > 0f)
            {
                Invoke(nameof(AdvanceCorrectDelivery), correctAnswerTransitionDelay);
            }
            else
            {
                AdvanceCorrectDelivery();
            }
        }
        else
        {
            AudioManager.Instance.PlayWrong();

            resultPopup.ShowWrong();

            ShowNextHint();
        }
    }

    private void AdvanceCorrectDelivery()
    {
        if (deliveryManager != null)
        {
            deliveryManager.CompleteDelivery();
            return;
        }

        LoadLevel1Scene();
    }

    private void LoadLevel1Scene()
    {
        if (string.IsNullOrWhiteSpace(level1SceneName))
        {
            Debug.LogError("SantaLetterGameManager: level1SceneName is empty.");
            return;
        }

        SceneManager.LoadScene(level1SceneName);
    }

    void ShowNextHint()
{
    string[] clues;

    switch(currentChild.difficulty)
    {
        case "medium":
            clues = currentChild.targetToy.mediumClues;
            break;

        case "hard":
            clues = currentChild.targetToy.hardClues;
            break;

        default:
            clues = currentChild.targetToy.easyClues;
            break;
    }

    if(wrongAttempts < clues.Length)
    {
        hintPanel.ShowHint(clues[wrongAttempts]);
    }

    wrongAttempts++;
}

    //----------------------------------------------------

    void Shuffle(List<ToyData> list)
    {
        for(int i=0;i<list.Count;i++)
        {
            int r =
                Random.Range(i,list.Count);

            ToyData temp =
                list[i];

            list[i] =
                list[r];

            list[r] =
                temp;
        }
    }

    int GetDeliveryCount()
    {
        if (GameManager.hasActiveDeliveryCount && GameManager.activeDeliveryCount > 0)
        {
            return Mathf.Max(1, GameManager.activeDeliveryCount);
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

    public void Retry()
    {
        AudioManager.Instance.PlayClick();

        selectedToy = null;

        hintPanel.Hide();

        // StartNewDelivery();
    }
}
