namespace RevolutionaryWebApp.Client.Services;

using System;
using RevolutionaryWebApp.Shared.Models.Pages;
using RevolutionaryWebApp.Shared.Services;
using RevolutionaryWebApp.Shared.Utilities;

public class ClientMediaLinkConverter : IMediaLinkConverter
{
    private string? baseUrl;

    public string TranslateImageLink(string imageType, string globalId, MediaFileSize size)
    {
        if (baseUrl == null)
            throw new InvalidOperationException("BaseUrl not set for link translation");

        if (size == MediaFileSize.Original)
            throw new ArgumentException("This method doesn't support getting original size");

        return baseUrl + MediaFileExtensions.GetViewPath(globalId, size, imageType);
    }

    public void OnReceiveBaseUrl(string configMediaBaseUrl)
    {
        baseUrl = configMediaBaseUrl;

        if (!baseUrl.EndsWith('/'))
            baseUrl += '/';
    }
}
