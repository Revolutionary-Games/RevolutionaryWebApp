namespace RevolutionaryWebApp.Shared.Tests.Forms.Tests;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Shared.Forms;
using Shared.Models.Enums;
using Xunit;

public class BulkEmailSendRequestFormTests
{
    [Fact]
    public void BulkEmailSendRequestFormTest_ValidPassesValidation()
    {
        var model = new BulkEmailSendRequestForm
        {
            Title = "This is a dummy email title",
            HTMLBody = "This is a dummy email body that is long enough to pass validation",
            RecipientsMode = BulkEmailRecipientsMode.DevCenterUsers,
            IgnoreMode = BulkEmailIgnoreMode.CLASigned,
        };
        model.PlainBody = model.HTMLBody;

        var errors = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void BulkEmailSendRequestFormTest_DisallowSendToNoOne()
    {
        var model = new BulkEmailSendRequestForm
        {
            Title = "This is a dummy email title",
            HTMLBody = "This is a dummy email body that is long enough to pass validation",
            RecipientsMode = BulkEmailRecipientsMode.DevCenterUsers,
            IgnoreMode = BulkEmailIgnoreMode.DevCenterUsers,
        };
        model.PlainBody = model.HTMLBody;

        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors));
        Assert.NotEmpty(errors);

        Assert.NotNull(errors[0].ErrorMessage);
        Assert.Contains("no one will receive", errors[0].ErrorMessage!);
    }
}
