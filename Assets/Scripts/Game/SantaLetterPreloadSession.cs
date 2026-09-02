/// <summary>
/// Tells the letter scene that LoadingScene is preparing its first delivery.
/// The flag is set before the additive scene load so the manager does not
/// start a second delivery from Start().
/// </summary>
public static class SantaLetterPreloadSession
{
    public static bool IsPreparing { get; set; }
}
