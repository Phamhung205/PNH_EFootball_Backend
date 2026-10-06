using Appwebbongda.Services;
// ============================================================
// BACKEND - FILE THÊM MỚI
// Controllers/ChampionHonorsController.cs
// ============================================================

using Appwebbongda.Data;
using Appwebbongda.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Appwebbongda.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class ChampionHonorsController : ControllerBase
	{
		private readonly AppDbContext _context;


		public ChampionHonorsController(
			AppDbContext context
		)
		{
			_context = context;
		}

		public class HonorUpsertDto
		{
			public string CompetitionName { get; set; } = string.Empty;

			public string? Season { get; set; }

			public string ChampionName { get; set; } = string.Empty;

			public string? TeamName { get; set; }

			public string? TeamLogoUrl { get; set; }

			public string? ChampionAvatarUrl { get; set; }

			public DateTime? StartDate { get; set; }

			public DateTime? EndDate { get; set; }

			public int Placement { get; set; } = 1;
		}

		private int? CurrentUserId()
		{
			var value =
				User.FindFirstValue(
					ClaimTypes.NameIdentifier
				)
				??
				User.FindFirstValue(
					"sub"
				)
				??
				User.FindFirstValue(
					"nameid"
				);

			return int.TryParse(
				value,
				out var id
			)
				? id
				: null;
		}

		private bool IsAdmin()
		{
			return string.Equals(
				User.FindFirstValue(
					ClaimTypes.Role
				),
				"Admin",
				StringComparison.OrdinalIgnoreCase
			);
		}

		private static string NormalizeName(
			string? value
		)
		{
			if (
				string.IsNullOrWhiteSpace(
					value
				)
			)
			{
				return string.Empty;
			}

			var s =
				value
					.Trim()
					.ToLowerInvariant()
					.Normalize(
						NormalizationForm.FormD
					);

			var sb =
				new StringBuilder();

			foreach (
				var c in s
			)
			{
				var category =
					System.Globalization
						.CharUnicodeInfo
						.GetUnicodeCategory(
							c
						);

				if (
					category ==
					System.Globalization
						.UnicodeCategory
						.NonSpacingMark
				)
				{
					continue;
				}

				if (c == 'đ')
				{
					sb.Append('d');

					continue;
				}

				sb.Append(
					char.IsLetterOrDigit(
						c
					)
						? c
						: ' '
				);
			}

			return string.Join(
				" ",
				sb
					.ToString()
					.Split(
						' ',
						StringSplitOptions
							.RemoveEmptyEntries
					)
			);
		}

		// Gộp BXH theo tên.
		//
		// Ví dụ:
		// "HùngIT"
		// "hungit"
		// " HùngIT "
		//
		// => được xem là cùng một người.
		private static string PersonKey(
			ChampionHonor honor
		)
		{
			return
				$"n:{NormalizeName(honor.ChampionName)}";
		}

		// =====================================================
		// PUBLIC
		// Danh sách giải đã có lịch sử vinh danh
		// =====================================================

		[HttpGet("competitions")]
		[AllowAnonymous]
		public async Task<IActionResult> GetCompetitions()
		{
			var list =
				await _context
					.ChampionHonors
					.AsNoTracking().Where(SystemCompetitions.RankedHonor)
					.Select(
						x =>
							x.CompetitionName
					)
					.Distinct()
					.OrderBy(
						x => x
					)
					.ToListAsync();

			return Ok(
				new
				{
					success = true,

					data = list
				}
			);
		}

		// =====================================================
		// PUBLIC
		// BXH tổng hợp
		//
		// competition = all:
		//   - Xếp theo tổng số lần vô địch.
		//   - Hiện 🏅 🥈 🥉.
		//   - Hiện tổng danh hiệu.
		//
		// competition = Premier League...
		//   - Chỉ tính riêng giải đó.
		//   - Xếp ai vô địch nhiều nhất.
		// =====================================================

		[HttpGet("ranking")]
		[AllowAnonymous]
		public async Task<IActionResult> GetRanking(
			[FromQuery]
			string? competition
		)
		{
			var query =
				_context
					.ChampionHonors
					.AsNoTracking().Where(SystemCompetitions.RankedHonor)
					.AsQueryable();

			if (
				!string.IsNullOrWhiteSpace(
					competition
				)
				&&
				!string.Equals(
					competition,
					"all",
					StringComparison.OrdinalIgnoreCase
				)
			)
			{
				query =
					query.Where(
						x =>
							x.CompetitionName ==
							competition
					);
			}

			var raw =
				await query
					.OrderByDescending(
						x => x.EndDate
					)
					.ThenByDescending(
						x => x.Id
					)
					.ToListAsync();

			var ranking =
				raw
					.GroupBy(
						PersonKey
					)

					// BXH nhà vô địch chỉ hiển thị người
					// đã từng có ít nhất 1 chức vô địch.
					.Where(
						g =>
							g.Any(
								x =>
									x.Placement ==
									1
							)
					)

					.Select(
						g =>
						{
							var newest =
								g
									.OrderByDescending(
										x =>
											x.EndDate
									)
									.ThenByDescending(
										x =>
											x.Id
									)
									.First();

							var gold =
								g.Count(
									x =>
										x.Placement ==
										1
								);

							var silver =
								g.Count(
									x =>
										x.Placement ==
										2
								);

							var bronze =
								g.Count(
									x =>
										x.Placement ==
										3
								);

							return new
							{
								key =
									g.Key,

								userId =
									newest.UserId,

								name =
									newest.ChampionName,

								avatarUrl =
									g.Where(h => !string.IsNullOrWhiteSpace(h.ChampionAvatarUrl)).OrderByDescending(h => h.UpdatedAt).ThenByDescending(h => h.Id).Select(h => h.ChampionAvatarUrl).FirstOrDefault(),

								// Số lần vô địch.
								championships =
									gold,

								// Huy chương.
								gold,

								silver,

								bronze,

								// Tổng danh hiệu.
								totalHonors =
									gold +
									silver +
									bronze,

								// Các CLB người này đã từng vô địch.
								teams =
									g
										.Where(
											x =>
												x.Placement ==
													1
												&&
												!string.IsNullOrWhiteSpace(
													x.TeamName
												)
										)
										.GroupBy(
											x =>
												NormalizeName(
													x.TeamName
												)
										)
										.Select(
											x =>
												new
												{
													name =
														x.First()
															.TeamName,

													logoUrl =
														x.First()
															.TeamLogoUrl
												}
										)
										.ToList(),

								// Các giải từng vô địch.
								competitions =
									g
										.Where(
											x =>
												x.Placement ==
												1
										)
										.Select(
											x =>
												x.CompetitionName
										)
										.Distinct()
										.OrderBy(
											x => x
										)
										.ToList(),

								// Toàn bộ lịch sử.
								history =
									g
										.OrderByDescending(
											x =>
												x.EndDate
										)
										.ThenByDescending(
											x =>
												x.Id
										)
										.Select(
											x =>
												new
												{
													x.Id,

													x.CompetitionName,

													x.Season,

													x.TeamName,

													x.TeamLogoUrl,

													x.StartDate,

													x.EndDate,

													x.Placement,

													x.Source
												}
										)
										.ToList()
							};
						}
					)

					// TOP ưu tiên số lần vô địch.
					.OrderByDescending(
						x =>
							x.championships
					)

					// Nếu bằng số lần vô địch:
					// ưu tiên nhiều huy chương bạc hơn.
					.ThenByDescending(
						x =>
							x.silver
					)

					// Sau đó tới huy chương đồng.
					.ThenByDescending(
						x =>
							x.bronze
					)

					.ThenBy(
						x =>
							x.name
					)

					.Select(
						(
							x,
							index
						) =>
							new
							{
								rank =
									index +
									1,

								x.key,

								x.userId,

								x.name,

								x.avatarUrl,

								x.championships,

								x.gold,

								x.silver,

								x.bronze,

								x.totalHonors,

								x.teams,

								x.competitions,

								x.history
							}
					)
					.ToList();

			return Ok(
				new
				{
					success = true,

					data =
						ranking
				}
			);
		}

		// =====================================================
		// PUBLIC
		// Danh sách tất cả bản ghi thành tích
		// =====================================================

		[HttpGet]
		[AllowAnonymous]
		public async Task<IActionResult> GetAll(
			[FromQuery]
			string? competition
		)
		{
			var query =
				_context
					.ChampionHonors
					.AsNoTracking().Where(SystemCompetitions.RankedHonor)
					.AsQueryable();

			if (
				!string.IsNullOrWhiteSpace(
					competition
				)
				&&
				!string.Equals(
					competition,
					"all",
					StringComparison.OrdinalIgnoreCase
				)
			)
			{
				query =
					query.Where(
						x =>
							x.CompetitionName ==
							competition
					);
			}

			var list =
				await query
					.OrderByDescending(
						x => x.EndDate
					)
					.ThenByDescending(
						x => x.Id
					)
					.Select(
						x =>
							new
							{
								x.Id,

								x.TournamentId,

								x.UserId,

								x.CompetitionName,

								x.Season,

								x.ChampionName,

								x.ChampionAvatarUrl,

								x.TeamName,

								x.TeamLogoUrl,

								x.StartDate,

								x.EndDate,

								x.Placement,

								x.Source
							}
					)
					.ToListAsync();

			return Ok(
				new
				{
					success = true,

					data =
						list
				}
			);
		}

		// =====================================================
		// ADMIN ONLY
		// Thêm thủ công
		// =====================================================

		[HttpPost]
		[Authorize(
			Roles = "Admin"
		)]
		public async Task<IActionResult> Create(
			[FromBody]
			HonorUpsertDto dto
		)
		{
			var error =
				Validate(dto);

			if (
				error != null
			)
			{
				return BadRequest(
					new
					{
						success =
							false,

						message =
							error
					}
				);
			}

			var item =
				new ChampionHonor
				{
					CompetitionName =
						dto
							.CompetitionName
							.Trim(),

					Season =
						dto
							.Season
							?.Trim(),

					ChampionName =
						dto
							.ChampionName
							.Trim(),

					TeamName =
						dto
							.TeamName
							?.Trim(),

					TeamLogoUrl =
						dto.TeamLogoUrl,

					ChampionAvatarUrl =
						dto.ChampionAvatarUrl,

					StartDate =
						dto.StartDate,

					EndDate =
						dto
							.EndDate!
							.Value,

					Placement =
						dto.Placement,

					Source =
						"Manual",

					CreatedAt =
						DateTime.UtcNow,

					UpdatedAt =
						DateTime.UtcNow
				};

			_context
				.ChampionHonors
				.Add(item);

			await _context
				.SaveChangesAsync();

			return Ok(
				new
				{
					success = true,

					message =
						"Đã thêm thành tích.",

					data =
						item
				}
			);
		}

		// =====================================================
		// ADMIN ONLY
		// Chỉnh sửa
		// =====================================================

		[HttpPut("{id:int}")]
		[Authorize(
			Roles = "Admin"
		)]
		public async Task<IActionResult> Update(
			int id,

			[FromBody]
			HonorUpsertDto dto
		)
		{
			var error =
				Validate(dto);

			if (
				error != null
			)
			{
				return BadRequest(
					new
					{
						success =
							false,

						message =
							error
					}
				);
			}

			var item =
				await _context
					.ChampionHonors
					.FindAsync(id);

			if (
				item == null
			)
			{
				return NotFound(
					new
					{
						success =
							false,

						message =
							"Không tìm thấy bản ghi."
					}
				);
			}

			item.CompetitionName =
				dto
					.CompetitionName
					.Trim();

			item.Season =
				dto
					.Season
					?.Trim();

			item.ChampionName =
				dto
					.ChampionName
					.Trim();

			item.TeamName =
				dto
					.TeamName
					?.Trim();

			item.TeamLogoUrl =
				dto.TeamLogoUrl;

			item.ChampionAvatarUrl =
				dto.ChampionAvatarUrl;

			item.StartDate =
				dto.StartDate;

			item.EndDate =
				dto
					.EndDate!
					.Value;

			item.Placement =
				dto.Placement;

			item.UpdatedAt =
				DateTime.UtcNow;

			await _context
				.SaveChangesAsync();

			return Ok(
				new
				{
					success = true,

					message =
						"Đã cập nhật thành tích.",

					data =
						item
				}
			);
		}

		// =====================================================
		// ADMIN ONLY
		// Xóa
		// =====================================================

		[HttpDelete("{id:int}")]
		[Authorize(
			Roles = "Admin"
		)]
		public async Task<IActionResult> Delete(
			int id
		)
		{
			var item =
				await _context
					.ChampionHonors
					.FindAsync(id);

			if (
				item == null
			)
			{
				return NotFound(
					new
					{
						success =
							false,

						message =
							"Không tìm thấy bản ghi."
					}
				);
			}

			_context
				.ChampionHonors
				.Remove(item);

			await _context
				.SaveChangesAsync();

			return Ok(
				new
				{
					success = true,

					message =
						"Đã xóa thành tích."
				}
			);
		}

		// =====================================================
		// AUTO
		// Hệ thống tự đồng bộ khi giải kết thúc
		// =====================================================

		[HttpPost(
			"sync/{tournamentId:int}"
		)]
		[Authorize]
		public async Task<IActionResult> SyncTournament(
			int tournamentId
		)
		{
			var tournament =
				await _context
					.Tournaments
					.FindAsync(
						tournamentId
					);

			if (
				tournament == null
			)
			{
				return NotFound(
					new
					{
						success =
							false,

						message =
							"Không tìm thấy giải đấu."
					}
				);
			}

			var uid =
				CurrentUserId();

			if (
				!IsAdmin()
				&&
				(
					uid == null
					||
					tournament.CreatedByUserId !=
					uid.Value
				)
			)
			{
				return StatusCode(
					403,

					new
					{
						success =
							false,

						message =
							"Bạn không có quyền đồng bộ giải này."
					}
				);
			}

            if (!SystemCompetitions.IsCompleted(tournament.Status))
                return BadRequest(new { success = false, message = "Chỉ đồng bộ khi giải đã kết thúc." });
            if (string.IsNullOrWhiteSpace(tournament.SystemCompetition))
                return BadRequest(new { success = false, message = "Giải chưa được gán vào giải hệ thống." });
            try
            {
                await new ChampionHonorSyncService(_context).StageAsync(tournament);
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Đã cập nhật BXH giải hệ thống." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

		private static string? Validate(
			HonorUpsertDto dto
		)
		{
			if (
				string.IsNullOrWhiteSpace(
					dto.CompetitionName
				)
			)
			{
				return
					"Tên giải đấu không được để trống.";
			}

			if (
				string.IsNullOrWhiteSpace(
					dto.ChampionName
				)
			)
			{
				return
					"Tên người đạt thành tích không được để trống.";
			}

			if (
				string.IsNullOrWhiteSpace(
					dto.TeamName
				)
			)
			{
				return
					"Tên đội bóng không được để trống.";
			}

			if (
				!dto.EndDate.HasValue
			)
			{
				return
					"Phải chọn thời gian kết thúc.";
			}

			if (
				dto.StartDate.HasValue
				&&
				dto.StartDate.Value.Date >
				dto.EndDate.Value.Date
			)
			{
				return
					"Thời gian bắt đầu không được sau thời gian kết thúc.";
			}

			if (
				dto.Placement <
					1
				||
				dto.Placement >
					3
			)
			{
				return
					"Thứ hạng chỉ nhận 1, 2 hoặc 3.";
			}

			return null;
		}

		// =====================================================
		// TỰ XÁC ĐỊNH TOP 1 / TOP 2 / TOP 3
		// =====================================================

	}
}
