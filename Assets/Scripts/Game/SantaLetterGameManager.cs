using System.Collections.Generic;
using UnityEngine;

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

    private ChildData currentChild;

    private int wrongAttempts = 0;

    //----------------------------------------------------

    void Start()
{
    allToys = toyLoader.toys;

    Debug.Log("Number of Toys = " + allToys.Length);

    foreach (ToyData toy in allToys)
    {
        Debug.Log("Loaded Toy: " + toy.name);
    }

    StartNewDelivery();
}

    //----------------------------------------------------

    // public async void StartNewDelivery()
    // {
    //     wrongAttempts = 0;

    //     hintPanel.Hide();

    //     currentChild = childGenerator.Generate();

    //     await letterGenerator.GenerateLetter(currentChild);

    //     GenerateGiftChoices();
    // }

    public async void StartNewDelivery()
{
    Debug.Log("===== Start New Delivery =====");

    Debug.Log("hintPanel = " + hintPanel);
    Debug.Log("childGenerator = " + childGenerator);
    Debug.Log("letterGenerator = " + letterGenerator);

    wrongAttempts = 0;

    hintPanel.Hide();

    ToyData selectedToy = toyLoader.GetRandomToy();
    currentChild = childGenerator.Generate(selectedToy);
    

    Debug.Log("Child Generated: " + currentChild.name);

    await letterGenerator.GenerateLetter(currentChild);

    GenerateGiftChoices();
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

    //----------------------------------------------------

    // public void SelectGift(ToyData selectedToy)
    // {
    //     if (selectedToy.id ==
    //         currentChild.targetToy.id)
    //     {
    //         Debug.Log("Correct!");

    //         deliveryManager.CompleteDelivery();

    //         return;
    //     }

    //     Debug.Log("Wrong!");

    //     ShowNextHint();
    // }

    public void SelectGift(ToyData selectedToy)
{
    Debug.Log("Clicked : " + selectedToy.name);

    // Correct answer
    if (selectedToy.id == currentChild.targetToy.id)
    {
        resultPopup.ShowCorrect();

        Debug.Log("Correct!");

        // Wait 2 seconds before the next child
        Invoke(nameof(StartNewDelivery), 2f);

        return;
    }

    // Wrong answer
    resultPopup.ShowWrong();

    ShowNextHint();

    Debug.Log("Wrong!");
}

    //----------------------------------------------------

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
}