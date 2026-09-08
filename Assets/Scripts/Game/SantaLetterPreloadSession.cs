using System.Threading.Tasks;

public static class SantaLetterPreloadSession
{
    public static bool IsPreparing { get; set; }

    public static PreparedDelivery PreparedDelivery { get; private set; }

    public static Task<PreparedDelivery> PreparingTask { get; private set; }

    public static bool HasPreparedDelivery =>
        PreparedDelivery != null;

    public static bool IsPreparingDelivery =>
        PreparingTask != null && !PreparingTask.IsCompleted;

    public static void SetPreparingTask(
        Task<PreparedDelivery> task)
    {
        PreparingTask = task;
        IsPreparing = true;
    }

    public static void SetPreparedDelivery(
        PreparedDelivery delivery)
    {
        PreparedDelivery = delivery;
        PreparingTask = null;
        IsPreparing = false;
    }

    public static PreparedDelivery ConsumePreparedDelivery()
    {
        PreparedDelivery delivery = PreparedDelivery;

        PreparedDelivery = null;
        PreparingTask = null;
        IsPreparing = false;

        return delivery;
    }

    public static void Clear()
    {
        PreparedDelivery = null;
        PreparingTask = null;
        IsPreparing = false;
    }
}
