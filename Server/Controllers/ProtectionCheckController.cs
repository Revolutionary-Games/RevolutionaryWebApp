namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

/// <summary>
///   Allows testing the data protection to see if it is currently working or not
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ProtectionCheckController : Controller
{
    private const string TestProtectionPurposeString = "ProtectionCheckController.Test.v1";

    private const int MagicValue = 33;

    private static uint sequenceNumber;

    private readonly ITimeLimitedDataProtector dataProtector;

    public ProtectionCheckController(IDataProtectionProvider dataProtectionProvider)
    {
        dataProtector = dataProtectionProvider.CreateProtector(TestProtectionPurposeString)
            .ToTimeLimitedDataProtector();
    }

    [HttpPost]
    public ActionResult<string> GetTestSignature()
    {
        var data = JsonSerializer.Serialize(new TestSignedData
        {
            Magic = MagicValue,
            Sequence = ++sequenceNumber,
        });

        return dataProtector.Protect(data);
    }

    [HttpGet]
    public IActionResult VerifyTestSignature([Required] string signature)
    {
        string json;
        try
        {
            json = dataProtector.Unprotect(signature);
        }
        catch (CryptographicException e)
        {
            if (e.InnerException is FormatException)
                return BadRequest("Signature format is not valid");

            if (e.Message.Contains("The payload was invalid"))
                return BadRequest("Signature is invalid");

            throw;
        }

        var data = JsonSerializer.Deserialize<TestSignedData>(json);

        if (data == null)
        {
            return BadRequest("Decoded signature data is null, something is wrong");
        }

        if (data.Magic != MagicValue)
        {
            return Problem("Decoded signature data has wrong magic value, something is very wrong");
        }

        return Ok("Protection is working");
    }

    private class TestSignedData
    {
        public int Magic { get; set; }
        public uint Sequence { get; set; }
    }
}
