namespace ThriveDevCenter.Shared.Models;

using System.Text.Json.Serialization;
using ModelVerifiers;

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
