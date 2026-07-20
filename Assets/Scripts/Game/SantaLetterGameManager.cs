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

    [Header("Gift Buttons")]
    public GiftButton[] giftButtons;

    [Header("Toy Database")]
    public ToyData[] allToys;

    private ChildData currentChild;

    private int wrongAttempts = 0;

    //----------------------------------------------------

    void Start()
    {
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

    currentChild = childGenerator.Generate();

    Debug.Log("Child Generated: " + currentChild.name);

    await letterGenerator.GenerateLetter(currentChild);

    GenerateGiftChoices();
}

    //----------------------------------------------------

    void GenerateGiftChoices()
    {
        List<ToyData> pool =
            new List<ToyData>(allToys);

        List<ToyData> choices =
            new List<ToyData>();

        choices.Add(currentChild.targetToy);

        pool.Remove(currentChild.targetToy);

        while (choices.Count < giftButtons.Length)
        {
            int index =
                Random.Range(0, pool.Count);

            choices.Add(pool[index]);

            pool.RemoveAt(index);
        }

        Shuffle(choices);

        for (int i = 0; i < giftButtons.Length; i++)
        {
            giftButtons[i].Setup(
                choices[i],
                this
            );
        }
    }

    //----------------------------------------------------

    public void SelectGift(ToyData selectedToy)
    {
        if (selectedToy.id ==
            currentChild.targetToy.id)
        {
            Debug.Log("Correct!");

            deliveryManager.CompleteDelivery();

            return;
        }

        Debug.Log("Wrong!");

        ShowNextHint();
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