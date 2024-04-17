namespace RevolutionaryWebApp.Shared.Models.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using DevCenterCommunication.Models;
using SharedBase.ModelVerifiers;

public class VersionedPageDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(AppInfo.MaxPageTitleLength, MinimumLength = 2)]
    [NoTrailingOrPrecedingSpace]
    public string Title { get; set; } = null!;

    public PageVisibility Visibility { get; set; }

    [MaxLength(AppInfo.MaxPagePermalinkLength)]
    [NoWhitespace]
    public string? Permalink { get; set; }

    public DateTime? PublishedAt { get; set; }

    public long? CreatorId { get; set; }

    public long? LastEditorId { get; set; }

    [Required]
    [MaxLength(AppInfo.MaxPageLength)]
    public string LatestContent { get; set; } = null!;

    [MaxLength(AppInfo.MaxPageEditCommentLength)]
    public string? LastEditComment { get; set; }

    public int VersionNumber { get; set; }

    public PageType Type { get; set; }

    public bool Deleted { get; set; }

    public static string GeneratePermalinkFromTitle(string title, bool representAllCharacters = false)
    {
        var builder = new StringBuilder(title.Length);

        for (int i = 0; i < title.Length; ++i)
        {
            var character = title[i];

            if (character is >= '0' and <= '9')
            {
                builder.Append(character);
            }
            else if (character is >= 'A' and <= 'Z')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is >= 'a' and <= 'z')
            {
                builder.Append(character);
            }
            else if (character is '_' or '-')
            {
                builder.Append(character);
            }
            else if (character is ' ' or (>= '{' and <= '~') or '/' or '\\' or (>= '(' and <= ',') or '&' or ';' or '.')
            {
                if (builder[^1] != '-')
                    builder.Append('-');
            }
            else if (representAllCharacters)
            {
                if (builder[^1] != '_')
                    builder.Append('_');
            }
        }

        return builder.ToString();
    }

    public VersionedPageDTO Clone()
    {
        return new VersionedPageDTO
        {
            Id = Id,
            UpdatedAt = UpdatedAt,
            CreatedAt = CreatedAt,
            Title = Title,
            Visibility = Visibility,
            Permalink = Permalink,
            PublishedAt = PublishedAt,
            CreatorId = CreatorId,
            LastEditorId = LastEditorId,
            LatestContent = LatestContent,
            LastEditComment = LastEditComment,
            VersionNumber = VersionNumber,
            Type = Type,
            Deleted = Deleted,
        };
    }
}
