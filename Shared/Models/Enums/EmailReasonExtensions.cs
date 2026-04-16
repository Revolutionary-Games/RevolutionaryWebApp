namespace RevolutionaryWebApp.Shared.Models.Enums;

public static class EmailReasonExtensions
{
    /// <summary>
    ///   Returns true if the recipient is allowed to unsubscribe from emails of this category.
    ///   Security-critical operational emails cannot be unsubscribed.
    /// </summary>
    public static bool CanUnSubscribe(this EmailReason reason)
    {
        return reason switch
        {
            EmailReason.ImportantEmails => false,
            _ => true,
        };
    }
}
