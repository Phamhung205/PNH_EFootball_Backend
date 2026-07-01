using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Appwebbongda.Models
{
    public class Tournament
    {
        [Key]
        public int TournamentId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty; // Đã fix cảnh báo

        [Required]
        public string Format { get; set; } = string.Empty; // Đã fix cảnh báo

        public int MaxTeams { get; set; }
        public int? NumberOfGroups { get; set; }
        public int? TeamsAdvancingPerGroup { get; set; }

        public DateTime StartDate { get; set; }

        public string? Description { get; set; } // Thêm dấu ? để cho phép Null

        public string Status { get; set; } = "Sắp khởi tranh";

        // ID nguoi tao giai (de BTC chi sua duoc giai do chinh minh tao).
        // Null voi cac giai cu tao truoc khi co tinh nang nay.
        public int? CreatedByUserId { get; set; }

        // Cho phep nguoi dung DANG KY tham du giai nay hay khong (admin bat/tat).
        // true = mo dang ky, false = dong. Mac dinh false.
        public bool AllowRegistration { get; set; } = false;

        // LOGO giai dau (URL hoac base64). DB da co san cot nay.
        public string? LogoUrl { get; set; }

        // Khởi tạo List rỗng để fix cảnh báo Null cho ICollection
        public ICollection<Group> Groups { get; set; } = new List<Group>();
        public ICollection<Team> Teams { get; set; } = new List<Team>();
    }
}