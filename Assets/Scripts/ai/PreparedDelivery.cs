using System;

[Serializable]
public class PreparedDelivery
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