using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Appwebbongda.Models
{
    public class Group
    {
        [Key]
        public int GroupId { get; set; }

        [ForeignKey("Tournament")]
        public int TournamentId { get; set; }
        public Tournament? Tournament { get; set; } // Thêm dấu ?

        [Required]
        public string GroupName { get; set; } = string.Empty; // Fix cảnh báo
    }
}