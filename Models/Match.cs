using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Appwebbongda.Models
{
    public class Match
    {
        [Key]
        public int MatchId { get; set; }

        [Required]
        public int TournamentId { get; set; }

        [ForeignKey("TournamentId")]
        public Tournament? Tournament { get; set; }

        [Required]
        public int HomeTeamId { get; set; }

        [ForeignKey("HomeTeamId")]
        public Team? HomeTeam { get; set; }

        [Required]
        public int AwayTeamId { get; set; }

        [ForeignKey("AwayTeamId")]
        public Team? AwayTeam { get; set; }

        [Required]
        public int Round { get; set; }

        public DateTime? MatchDate { get; set; }

        public int? HomeScore { get; set; }

        public int? AwayScore { get; set; }

        [Required]
        public string Status { get; set; } = "Scheduled"; // Scheduled, Ongoing, Finished
    }
}
