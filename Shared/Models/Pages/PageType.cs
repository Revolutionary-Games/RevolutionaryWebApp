namespace RevolutionaryWebApp.Shared.Models.Pages;

public enum PageType
{
    Template = 0,

    /// <summary>
    ///   Normal, static part of the site
    /// </summary>
    NormalPage = 1,

    /// <summary>
    ///   A timed, update, "news" post
    /// </summary>
    Post,

    WikiPage,
}
