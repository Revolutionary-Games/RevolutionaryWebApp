namespace ThriveDevCenter.Shared.Forms;

using System.ComponentModel.DataAnnotations;
using Models.Enums;
using SharedBase.ModelVerifiers;

public class BulkEmailSendRequestForm
{
    [Required]
    [StringLength(120, MinimumLength = 10, ErrorMessage = "Title must be between 10 and 120 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(8000, MinimumLength = 40)]
    [Display(Name = "Plain Text Body")]
    public string PlainBody { get; set; } = string.Empty;

    [Required]
    [StringLength(16000, MinimumLength = 40)]
    [Display(Name = "HTML Body")]
    public string HTMLBody { get; set; } = string.Empty;

    public BulkEmailRecipientsMode RecipientsMode { get; set; }

    [DisallowIf(ThisMatches = nameof(BulkEmailIgnoreMode.DevCenterDevelopers),
        OtherProperty = nameof(RecipientsMode),
        IfOtherMatchesValue = nameof(BulkEmailRecipientsMode.DevCenterDevelopers),
        ErrorMessage = "Ignore mode set so that no one will receive this email.")]
    [DisallowIf(ThisMatches = nameof(BulkEmailIgnoreMode.DevCenterUsers),
        OtherProperty = nameof(RecipientsMode),
        IfOtherMatchesValue = nameof(BulkEmailRecipientsMode.DevCenterUsers),
        ErrorMessage = "Ignore mode set so that no one will receive this email.")]
    [DisallowIfEnabled]
    public BulkEmailIgnoreMode IgnoreMode { get; set; }

    public BulkEmailReplyToMode ReplyMode { get; set; }

    [StringLength(200000, MinimumLength = 10)]
    [NotNullOrEmptyIf(PropertyMatchesValue = nameof(RecipientsMode),
        Value = nameof(BulkEmailRecipientsMode.ManualList))]
    [Display(Name = "Manual Recipients List")]
    public string? ManualRecipients { get; set; }
}