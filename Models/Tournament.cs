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

        // ── CHON DOI VAO VONG TRONG ──
        // So doi HANG BA tot nhat duoc lay them.
        //   null = tu tinh cho du luy thua 2
        //   4    = giai 24 doi kieu EURO (6 nhat + 6 nhi + 4 hang ba = 16)
        //   0    = khong lay doi hang ba nao
        public int? BestThirdPlaceCount { get; set; }

        // Danh sach doi BTC chon TAY, luu dang "12,45,78" (cach nhau dau phay).
        // Rong/null = dung hoan toan danh sach tu dong.
        // Co gia tri = danh sach nay THAY THE danh sach tu dong.
        public string? ManualQualifiedIds { get; set; }

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

        // ===== CÁC THUỘC TÍNH BỔ SUNG ĐỂ FIX LỖI CONTROLLER =====
        // IsFree mac dinh FALSE: chi giai nam trong 2 suat mien phi tron doi moi
        // duoc gan true (xem ham Create). De true o day thi MOI giai deu mien phi.
        public bool IsFree { get; set; } = false;
        public bool IsPaid { get; set; } = false;        // Đánh dấu đã thanh toán phí tạo giải chưa
        public int ActivationFee { get; set; } = 0;      // Phí kích hoạt giải đấu
        public DateTime? PaidAt { get; set; }            // Thoi diem ADMIN xac nhan da nhan tien

        public string? PaymentNote { get; set; }         // Ma doi soat, vd "PNH12"

        // Nguoi tao giai bam "Toi da chuyen khoan" luc nao.
        // Admin loc theo cot nay de biet ai can kiem tra truoc, khong phai do het
        // danh sach. Khac PaidAt: day la BTC TU BAO, chua duoc xac nhan.
        public DateTime? PaymentClaimedAt { get; set; }

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