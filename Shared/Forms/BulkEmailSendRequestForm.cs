namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using Models.Enums;

    public class BulkEmailSendRequestForm
    {
        [Required]
        [StringLength(120, MinimumLength = 10)]
        public string Title { get; set; }

        [Required]
        [StringLength(8000, MinimumLength = 40)]
        public string PlainBody { get; set; }

        [Required]
        [StringLength(16000, MinimumLength = 40)]
        public string HTMLBody { get; set; }

        public BulkEmailRecipientsMode RecipientsMode { get; set; }
        public BulkEmailIgnoreMode IgnoreMode { get; set; }
        public BulkEmailReplyToMode ReplyMode { get; set; }

        [StringLength(200000, MinimumLength = 10)]
        public string ManualRecipients { get; set; }
    }
}
