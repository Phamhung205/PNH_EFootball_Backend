namespace Appwebbongda.Services;
public static class SystemCompetitions
{
    public static readonly string[] Names = { "Premier League", "LaLiga", "Bundesliga", "Champions League", "Europa League", "World Cup", "Serie A", "Ligue 1", "Eredivisie", "Primeira Liga", "SPL", "Belgian Pro League", "Conference League" };
    public static bool IsValid(string? name) => name != null && Names.Contains(name, StringComparer.Ordinal);
    public static bool IsCompleted(string? status) => new[] { "Hoàn thành", "Đã kết thúc", "Completed", "Finished", "done" }.Contains(status?.Trim(), StringComparer.OrdinalIgnoreCase);
    // Both the overall and per-competition rankings require a current assignment.
    public static readonly System.Linq.Expressions.Expression<Func<Appwebbongda.Models.ChampionHonor, bool>> RankedHonor =
        honor => honor.Tournament != null
            && honor.Tournament.SystemCompetition != null
            && Names.Contains(honor.Tournament.SystemCompetition)
            && honor.CompetitionName == honor.Tournament.SystemCompetition;
}
