namespace Serein.Core;

public record BackgroundAppChoice(string Path, string Name, Approval? Saved);
public static class BackgroundAppSelection
{
    // Offering an executable is not approval or suspension. The engine independently
    // revalidates current identity, protection, ownership and exact hash on every action.
    public static List<BackgroundAppChoice> Candidates(IEnumerable<ProcessRow> rows, Profile profile, string sid)
    {
        var choices = profile.AllowedApps.Select(a => new BackgroundAppChoice(a.Path, Path.GetFileNameWithoutExtension(a.Path), a)).ToList();
        choices.AddRange(rows.Where(r => r.CanApprove && r.Owner == sid && r.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !r.Path.Equals(profile.GamePath, StringComparison.OrdinalIgnoreCase))
            .Select(r => new BackgroundAppChoice(r.Path, r.Name, null)));
        return choices.DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public static List<Approval> ApproveSelection(IReadOnlyList<BackgroundAppChoice> offered, IEnumerable<string> selected)
    {
        var paths = selected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count > 64) throw new InvalidDataException("Choose at most 64 applications.");
        return paths.Select(path =>
        {
            var choice = offered.SingleOrDefault(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Application was not offered for management.");
            return choice.Saved ?? JsonFiles.Approve(choice.Path);
        }).ToList();
    }
}
