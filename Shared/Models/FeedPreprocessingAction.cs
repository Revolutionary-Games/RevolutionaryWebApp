namespace ThriveDevCenter.Shared.Models;

using System.Text.Json.Serialization;
using ModelVerifiers;

/// <summary>
///   Action to process a fetched feed with before saving or creating feed items. Note that the actions should be able
///   to run multiple times when used in feeds that are combined into combined feeds.
/// </summary>
public class FeedPreprocessingAction
{
    [JsonConstructor]
    public FeedPreprocessingAction(PreprocessingActionTarget target, string toFind, string replacer)
    {
        Target = target;
        ToFind = toFind;
        Replacer = replacer;
    }

    public PreprocessingActionTarget Target { get; set; }

    [IsRegex]
    public string ToFind { get; set; }

    public string Replacer { get; set; }
}

public enum PreprocessingActionTarget
{
    Title,
    Summary,
}
