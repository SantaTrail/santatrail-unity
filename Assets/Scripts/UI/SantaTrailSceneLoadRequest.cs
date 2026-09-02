/// <summary>
/// Carries a scene transition through LoadingScene without adding persistent
/// GameObjects or changing the existing button setup.
/// </summary>
public static class SantaTrailSceneLoadRequest
{
    public static bool HasPendingRequest { get; private set; }
    public static string TargetSceneName { get; private set; }
    public static bool PrepareLetter { get; private set; }

    public static void Request(string targetSceneName, bool prepareLetter)
    {
        TargetSceneName = targetSceneName;
        PrepareLetter = prepareLetter;
        HasPendingRequest = true;
    }

    public static bool TryConsume(out string targetSceneName, out bool prepareLetter)
    {
        if (!HasPendingRequest)
        {
            targetSceneName = null;
            prepareLetter = false;
            return false;
        }

        targetSceneName = TargetSceneName;
        prepareLetter = PrepareLetter;
        TargetSceneName = null;
        PrepareLetter = false;
        HasPendingRequest = false;
        return true;
    }
}
