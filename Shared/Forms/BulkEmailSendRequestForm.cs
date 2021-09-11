namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using Models.Enums;
    using ModelVerifiers;

    public class BulkEmailSendRequestForm
    {
        [Required]
        [StringLength(120, MinimumLength = 10, ErrorMessage = "Title must be between 10 and 120 characters.")]
        public string Title { get; set; }

        [Required]
        [StringLength(8000, MinimumLength = 40)]
        [Display(Name = "Plain Text Body")]
        public string PlainBody { get; set; }

        [Required]
        [StringLength(16000, MinimumLength = 40)]
        [Display(Name = "HTML Body")]
        public string HTMLBody { get; set; }

        public BulkEmailRecipientsMode RecipientsMode { get; set; }
        public BulkEmailIgnoreMode IgnoreMode { get; set; }
        public BulkEmailReplyToMode ReplyMode { get; set; }

        [StringLength(200000, MinimumLength = 10)]
        [NotNullOrEmptyIf(PropertyMatchesValue = nameof(RecipientsMode),
            Value = nameof(BulkEmailRecipientsMode.ManualList))]
        [Display(Name = "Manual Recipients List")]
        public string ManualRecipients { get; set; }
    }
}
