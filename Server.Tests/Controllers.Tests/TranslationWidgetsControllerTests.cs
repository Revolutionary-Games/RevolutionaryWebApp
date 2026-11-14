namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Controllers;
using Server.Models;
using Server.Models.Translations;
using Shared.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class TranslationWidgetsControllerTests
{
    [Fact]
    public async Task ImageIncludesActiveCompletionEscapesNamesAndCachesAcrossRequests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(TranslationWidgetsController).Assembly);
        builder.Services.AddOutputCache();
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var app = builder.Build();
        app.UseRouting();
        app.UseOutputCache();
        app.MapControllers();

        using var scope = app.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var project = new TranslationProject { Id = 1, Slug = "thrive", Name = "Thrive <Test> & Friends" };
        database.TranslationProjects.Add(project);
        foreach (var code in new[] { "en", "fi", "fr", "de", "es" })
        {
            database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
            {
                ProjectId = 1, LanguageCode = code, DisplayName = code == "fi" ? "Suomi <&>" : code,
                Enabled = code != "de", Deleted = code == "es",
            });
        }

        for (int i = 0; i < 4; ++i)
        {
            var key = $"key{i}";
            database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = key, Active = i != 3 });
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = 1, Key = key, LanguageCode = "en", State = TranslationState.Verified,
            });
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = 1, Key = key, LanguageCode = "fi",
                State = i == 0 ? TranslationState.Translated :
                    i == 2 ? TranslationState.NeedsTranslation : TranslationState.Verified,
            });
        }

        database.TranslationProjects.Add(new TranslationProject { Id = 2, Slug = "empty", Name = "Empty" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
        {
            ProjectId = 2, LanguageCode = "fi", DisplayName = "Finnish",
        });
        database.TranslationProjects.Add(new TranslationProject { Id = 3, Slug = "disabled", Enabled = false });
        database.TranslationProjects.Add(new TranslationProject { Id = 4, Slug = "deleted", Deleted = true });
        database.TranslationProjects.Add(new TranslationProject { Id = 5, Slug = "no-languages", Name = "New" });
        await database.SaveChangesAsync();
        await app.StartAsync();
        using var client = app.GetTestClient();

        const string url = "/widgets/thrive/horizontal-auto.svg";
        string original;
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fi-FI");
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            Assert.Equal("image/svg+xml", response.Content.Headers.ContentType!.MediaType);
            Assert.True(response.Headers.CacheControl!.Public);
            Assert.Equal(TimeSpan.FromMinutes(30), response.Headers.CacheControl.MaxAge);
            original = await response.Content.ReadAsStringAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        XNamespace svg = "http://www.w3.org/2000/svg";
        var image = XElement.Parse(original);
        Assert.Equal(svg + "svg", image.Name);
        var texts = image.Elements(svg + "text").Select(element => element.Value).ToArray();
        Assert.Contains(project.Name, texts);
        Assert.Contains("Suomi <&> (fi)", texts);
        Assert.Contains("100%", texts);
        Assert.Contains("66.7%", texts);
        Assert.Contains("0%", texts);
        Assert.DoesNotContain("de (de)", texts);
        Assert.DoesNotContain("es (es)", texts);
        Assert.All(image.Elements(svg + "rect"), rectangle =>
            Assert.True(double.Parse(rectangle.Attribute("width")!.Value, CultureInfo.InvariantCulture) >= 0));

        project.Name = "Changed after generation";
        await database.SaveChangesAsync();
        Assert.Equal(original, await client.GetStringAsync(url));
        Assert.Equal(original, await client.GetStringAsync(url + "?ignored=1"));

        var empty = XElement.Parse(await client.GetStringAsync("/widgets/empty/horizontal-auto.svg"));
        Assert.Contains(empty.Elements(svg + "text"), element => element.Value == "0%");
        var noLanguages = XElement.Parse(await client.GetStringAsync("/widgets/no-languages/horizontal-auto.svg"));
        Assert.Contains(noLanguages.Elements(svg + "text"), element => element.Value == "No languages available");
        foreach (var slug in new[] { "missing", "disabled", "deleted" })
        {
            using var response = await client.GetAsync($"/widgets/{slug}/horizontal-auto.svg");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var pngResponse = await client.GetAsync("/widgets/thrive/horizontal-auto.png");
        pngResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/png", pngResponse.Content.Headers.ContentType!.MediaType);
        Assert.True(pngResponse.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromMinutes(30), pngResponse.Headers.CacheControl.MaxAge);
        var pngData = await pngResponse.Content.ReadAsByteArrayAsync();
        using var pngImage = Image.Load<Rgba32>(pngData);
        Assert.True(pngImage.Width > 500);
        Assert.Equal(76 + 3 * 28, pngImage.Height);

        var greenPixels = 0;
        var grayPixels = 0;
        for (int y = 0; y < pngImage.Height; ++y)
        {
            for (int x = 0; x < pngImage.Width; ++x)
            {
                var pixel = pngImage[x, y];
                if (pixel == new Rgba32(43, 148, 100, 255))
                    ++greenPixels;
                if (pixel == new Rgba32(229, 233, 237, 255))
                    ++grayPixels;
            }
        }

        Assert.InRange(greenPixels, 260 * 20, 2 * 260 * 20);
        Assert.True(grayPixels > 0);

        var cachedPng = await client.GetByteArrayAsync("/widgets/thrive/horizontal-auto.png?ignored=1");
        Assert.Equal(pngData, cachedPng);
        project.Name = "Changed after png generation";
        await database.SaveChangesAsync();
        Assert.Equal(pngData, await client.GetByteArrayAsync("/widgets/thrive/horizontal-auto.png"));

        using var emptyPngResponse = await client.GetAsync("/widgets/empty/horizontal-auto.png");
        emptyPngResponse.EnsureSuccessStatusCode();
        using var emptyPng = Image.Load<Rgba32>(await emptyPngResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(76 + 28, emptyPng.Height);
        Assert.Equal(new Rgba32(229, 233, 237, 255), emptyPng[213, 74]);
        using var noLanguagesPngResponse = await client.GetAsync("/widgets/no-languages/horizontal-auto.png");
        noLanguagesPngResponse.EnsureSuccessStatusCode();
        using var noLanguagesPng = Image.Load<Rgba32>(await noLanguagesPngResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(76 + 28, noLanguagesPng.Height);

        foreach (var slug in new[] { "missing", "disabled", "deleted" })
        {
            using var response = await client.GetAsync($"/widgets/{slug}/horizontal-auto.png");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
