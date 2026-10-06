using Appwebbongda.Data;
using Appwebbongda.Models;
using Microsoft.EntityFrameworkCore;
namespace Appwebbongda.Services;
public class ChampionHonorSyncService(AppDbContext context)
{
    private readonly AppDbContext _context = context;
    private const int KNOCKOUT_BASE = 100;
    // Stage honours in the same SaveChanges transaction as the tournament status.
    public async Task StageAsync(Tournament tournament, bool requireResults = true)
    {
        if (!SystemCompetitions.IsCompleted(tournament.Status) || string.IsNullOrWhiteSpace(tournament.SystemCompetition)) return;
        if (!SystemCompetitions.IsValid(tournament.SystemCompetition)) throw new InvalidOperationException("Giải hệ thống không hợp lệ.");
        var tournamentId = tournament.TournamentId;
        // Reclassify existing results even when an older finished tournament has incomplete match records.
        var existingHonours = await _context.ChampionHonors.Where(h => h.TournamentId == tournamentId).ToListAsync();
        foreach (var honour in existingHonours) honour.CompetitionName = tournament.SystemCompetition!;
        var matches = await _context.Matches.Where(m => m.TournamentId == tournamentId).ToListAsync();
        if (matches.Count == 0 || matches.Any(m => !IsFinished(m) || !m.HomeScore.HasValue || !m.AwayScore.HasValue))
            { if (requireResults) throw new InvalidOperationException("Vui lòng hoàn tất kết quả các trận đấu trước khi kết thúc giải."); return; }
        var knockout = matches.Where(m => m.Round >= KNOCKOUT_BASE && !m.IsThirdPlace).ToList();
        if (knockout.Count > 0 && knockout.Count(m => m.Round == knockout.Max(x => x.Round)) != 1)
            { if (requireResults) throw new InvalidOperationException("Chưa có trận chung kết để xác định nhà vô địch."); return; }
        if (knockout.Count == 0 && !string.Equals(tournament.Format, "League", StringComparison.OrdinalIgnoreCase))
            { if (requireResults) throw new InvalidOperationException("Giải loại trực tiếp chưa có kết quả chung kết."); return; }
        if (knockout.Count > 0 && Winner(knockout.OrderByDescending(m => m.Round).First()) == null) { if (requireResults) throw new InvalidOperationException("Trận chung kết chưa xác định được đội thắng."); return; }
        var placements = await ResolvePlacements(tournamentId);
        if (placements.Count == 0) { if (requireResults) throw new InvalidOperationException("Chưa đủ kết quả để xác định nhà vô địch. Vui lòng hoàn tất các trận đấu trước khi kết thúc giải."); return; }
			var teams =
				await _context
					.Teams
					.Where(
						x =>
							x.TournamentId ==
							tournamentId
					)
					.ToDictionaryAsync(
						x =>
							x.TeamId
					);

			var registrations =
				await _context
					.Registrations
					.Include(
						x =>
							x.User
					)
					.Where(
						x =>
							x.TournamentId ==
								tournamentId
							&&
							x.TeamId !=
								null
					)
					.OrderBy(
						x =>
							x.Id
					)
					.ToListAsync();

			foreach (
				var placement in
				placements
			)
			{
				if (
					!teams.TryGetValue(
						placement.TeamId,
						out var team
					)
				)
				{
					continue;
				}

				var registration =
					registrations
						.FirstOrDefault(
							x =>
								x.TeamId ==
								placement.TeamId
						);

				var user =
					registration?.User;

				var item =
					await _context
						.ChampionHonors
						.FirstOrDefaultAsync(
							x =>
								x.TournamentId ==
									tournamentId
								&&
								x.Placement ==
									placement.Placement
						);

				if (
					item == null
				)
				{
					item =
						new ChampionHonor
						{
							TournamentId =
								tournamentId,

							Source =
								"Auto",

							CreatedAt =
								DateTime.UtcNow
						};

					_context
						.ChampionHonors
						.Add(item);
				}

				item.UserId =
					user?.Id;

				item.CompetitionName =
					tournament.SystemCompetition!;

				item.Season =
					string.IsNullOrWhiteSpace(
						tournament.Season
					)
						? tournament
							.StartDate
							.Year
							.ToString()
						: tournament.Season;

				item.ChampionName =
					!string.IsNullOrWhiteSpace(
						user?.FullName
					)
						? user!.FullName
						: team.Name;

				if (string.IsNullOrWhiteSpace(item.ChampionAvatarUrl)) item.ChampionAvatarUrl = user?.AvatarUrl;

				item.TeamName =
					team.Name;

				item.TeamLogoUrl =
					team.LogoUrl;

				item.StartDate =
					tournament.StartDate;

				item.EndDate = item.EndDate == default ? DateTime.UtcNow : item.EndDate;

				item.Placement =
					placement.Placement;

				item.UpdatedAt =
					DateTime.UtcNow;
			}

    }
		private static bool IsFinished(
			Match match
		)
		{
			return
				string.Equals(
					match.Status,
					"Completed",
					StringComparison.OrdinalIgnoreCase
				)
				||
				string.Equals(
					match.Status,
					"Finished",
					StringComparison.OrdinalIgnoreCase
				)
				||
				string.Equals(
					match.Status,
					"done",
					StringComparison.OrdinalIgnoreCase
				);
		}

		private static int? Winner(
			Match match
		)
		{
			if (
				!match.HomeScore.HasValue
				||
				!match.AwayScore.HasValue
			)
			{
				return null;
			}

			if (
				match.HomeScore.Value >
				match.AwayScore.Value
			)
			{
				return match.HomeTeamId;
			}

			if (
				match.AwayScore.Value >
				match.HomeScore.Value
			)
			{
				return match.AwayTeamId;
			}

			if (
				match.HomePenalty.HasValue
				&&
				match.AwayPenalty.HasValue
			)
			{
				if (
					match.HomePenalty.Value >
					match.AwayPenalty.Value
				)
				{
					return match.HomeTeamId;
				}

				if (
					match.AwayPenalty.Value >
					match.HomePenalty.Value
				)
				{
					return match.AwayTeamId;
				}
			}

			return null;
		}


		private async Task<
			List<(
				int Placement,
				int TeamId
			)>
		> ResolvePlacements(
			int tournamentId
		)
		{
			var matches =
				await _context
					.Matches
					.Where(
						x =>
							x.TournamentId ==
							tournamentId
					)
					.ToListAsync();

			// ================================================
			// KNOCKOUT
			// ================================================

			var knockout =
				matches
					.Where(
						x =>
							x.Round >=
								KNOCKOUT_BASE
							&&
							!x.IsThirdPlace
					)
					.ToList();

			if (
				knockout.Count >
				0
			)
			{
				var finalRound =
					knockout.Max(
						x =>
							x.Round
					);

				var final =
					knockout
						.Where(
							x =>
								x.Round ==
								finalRound
						)
						.OrderByDescending(
							x =>
								x.MatchId
						)
						.FirstOrDefault();

				if (
					final != null
					&&
					IsFinished(
						final
					)
				)
				{
					var champion =
						Winner(
							final
						);

					if (
						champion !=
						null
					)
					{
						var result =
							new List<
								(
									int Placement,
									int TeamId
								)
							>
							{
								(
									1,
									champion.Value
								)
							};

						var runnerUp =
							final.HomeTeamId ==
							champion.Value
								? final.AwayTeamId
								: final.HomeTeamId;

						result.Add(
							(
								2,
								runnerUp
							)
						);

						var thirdMatch =
							matches
								.Where(
									x =>
										x.Round >=
											KNOCKOUT_BASE
										&&
										x.IsThirdPlace
										&&
										IsFinished(
											x
										)
								)
								.OrderByDescending(
									x =>
										x.Round
								)
								.ThenByDescending(
									x =>
										x.MatchId
								)
								.FirstOrDefault();

						var third =
							thirdMatch ==
							null
								? null
								: Winner(
									thirdMatch
								);

						if (
							third !=
							null
						)
						{
							result.Add(
								(
									3,
									third.Value
								)
							);
						}

						return result;
					}
				}
			}

			// ================================================
			// LEAGUE / VÒNG TRÒN
			// ================================================

			var teams =
				await _context
					.Teams
					.Where(
						x =>
							x.TournamentId ==
							tournamentId
					)
					.ToListAsync();

			var leagueMatches =
				matches
					.Where(
						x =>
							x.Round <
								KNOCKOUT_BASE
							&&
							IsFinished(
								x
							)
					)
					.ToList();

			if (
				teams.Count ==
					0
				||
				leagueMatches.Count ==
					0
			)
			{
				return new();
			}

			var rows =
				teams
					.Select(
						team =>
						{
							var home =
								leagueMatches
									.Where(
										m =>
											m.HomeTeamId ==
											team.TeamId
									)
									.ToList();

							var away =
								leagueMatches
									.Where(
										m =>
											m.AwayTeamId ==
											team.TeamId
									)
									.ToList();

							int won =
								home.Count(
									m =>
										m.HomeScore.HasValue
										&&
										m.AwayScore.HasValue
										&&
										m.HomeScore.Value >
										m.AwayScore.Value
								)
								+
								away.Count(
									m =>
										m.HomeScore.HasValue
										&&
										m.AwayScore.HasValue
										&&
										m.AwayScore.Value >
										m.HomeScore.Value
								);

							int drawn =
								home.Count(
									m =>
										m.HomeScore.HasValue
										&&
										m.AwayScore.HasValue
										&&
										m.HomeScore.Value ==
										m.AwayScore.Value
								)
								+
								away.Count(
									m =>
										m.HomeScore.HasValue
										&&
										m.AwayScore.HasValue
										&&
										m.AwayScore.Value ==
										m.HomeScore.Value
								);

							int gf =
								home.Sum(
									m =>
										m.HomeScore ??
										0
								)
								+
								away.Sum(
									m =>
										m.AwayScore ??
										0
								);

							int ga =
								home.Sum(
									m =>
										m.AwayScore ??
										0
								)
								+
								away.Sum(
									m =>
										m.HomeScore ??
										0
								);

							return new
							{
								team.TeamId,

								Points =
									won *
										3
									+
									drawn,

								GoalDiff =
									gf -
									ga,

								GoalsFor =
									gf,

								team.Name
							};
						}
					)
					.OrderByDescending(
						x =>
							x.Points
					)
					.ThenByDescending(
						x =>
							x.GoalDiff
					)
					.ThenByDescending(
						x =>
							x.GoalsFor
					)
					.ThenBy(
						x =>
							x.Name
					)
					.Take(3)
					.ToList();

			return rows
				.Select(
					(
						x,
						index
					) =>
						(
							Placement:
								index +
								1,

							TeamId:
								x.TeamId
						)
				)
				.ToList();
		}
}
