namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

using System.Collections.Generic;
using System.Text.Json;
using Server.Controllers;
using Xunit;

public class LFSControllerTests
{
    [Fact]
    public void LFSController_LFSResultIsSerializedCorrectly()
    {
        var response = new LFSResponse
        {
            Objects = new List<LFSResponse.LFSObject>
            {
                new("an_oid", 123)
                {
                    Actions = new Dictionary<string, LFSResponse.LFSObject.Action>
                    {
                        {
                            "download", new LFSResponse.LFSObject.DownloadAction
                            {
                                Href = "https://example.com",
                                ExpiresIn = 30,
                            }
                        },
                    },
                },
            },
        };

        var serialized = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            @"{""transfer"":""basic"",""objects"":[{""oid"":""an_oid"",""size"":123,""authenticated""" +
            @":true,""actions"":{""download"":{""href"":""https://example.com"",""expires_in"":30,""" +
            @"header"":null}}}]}",
            serialized);
    }
}