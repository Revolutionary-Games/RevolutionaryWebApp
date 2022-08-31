namespace Scripts;

using System.Collections.Generic;
using System.Text.RegularExpressions;
using ScriptsBase.Checks;

public class CodeChecks : CodeChecksBase<Program.CheckOptions>
{
    public CodeChecks(Program.CheckOptions opts) : base(opts)
    {
        FilePathsToAlwaysIgnore.Add(new Regex(@"Server/Migrations/"));
    }

    protected override Dictionary<string, CodeCheck> ValidChecks { get; } = new()
    {
        { "files", new FileChecks() },
        { "compile", new CompileCheck() },
        { "inspectcode", new InspectCode() },

        // Cleanup doesn't currently work nicely for the DevCenter (due to not working on .razor files)
        // { "cleanupcode", new CleanupCode() },
    };

    protected override IEnumerable<string> ForceIgnoredJetbrainsInspections =>
        new[] { "CSharpErrors", "Html.PathError" };

    protected override IEnumerable<string> ExtraIgnoredJetbrainsInspectWildcards => new[] { "Server/Migrations/*" };

    protected override string MainSolutionFile => "ThriveDevCenter.sln";
}
