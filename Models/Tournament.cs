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

        // #72: Danh gia sao - tong diem sao + so luot danh gia
        // Diem trung binh = RatingSum / RatingCount
        public int RatingSum { get; set; } = 0;
        public int RatingCount { get; set; } = 0;

        // #9: Mua giai (VD "Mua 2024", "Mua 1"...). Cho phep null neu khong dung.
        public string? Season { get; set; }

        // Box chat: admin bat -> user da dang ky se thay box chat
        public bool ChatEnabled { get; set; } = false;

        // ===== THU PHI GIAI DAU =====
        public int EntryFee { get; set; } = 0;          // Phi 1 nguoi (VD 20000)
        public int AdminFee { get; set; } = 0;          // Phi cat cho admin (VD 15000)
        public string? BankName { get; set; }           // Ten ngan hang
        public string? BankAccount { get; set; }        // So tai khoan
        public string? BankHolder { get; set; }         // Ten chu tai khoan
        public int Prize1 { get; set; } = 0;            // Thuong top 1 (admin nhap tay)
        public int Prize2 { get; set; } = 0;            // Thuong top 2
        public int Prize3 { get; set; } = 0;            // Thuong top 3


        // Khởi tạo List rỗng để fix cảnh báo Null cho ICollection
        public ICollection<Group> Groups { get; set; } = new List<Group>();
        public ICollection<Team> Teams { get; set; } = new List<Team>();
    }
}