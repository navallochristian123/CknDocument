using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentAccess entity - Access control for documents
/// Table: Document_Access (LawFirmDMS database)
/// </summary>
[Table("Document_Access")]
public class DocumentAccess
{
    [Key]
    public int AccessID { get; set; }

    public int? DocumentID { get; set; }

    public int? UserID { get; set; }

    [MaxLength(50)]
    public string? Permission { get; set; }

    public DateTime? GrantedAt { get; set; }

    // Navigation properties
    [ForeignKey("DocumentID")]
    public virtual Document? Document { get; set; }

    [ForeignKey("UserID")]
    public virtual User? User { get; set; }
}
