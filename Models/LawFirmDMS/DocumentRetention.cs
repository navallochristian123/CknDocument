using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// DocumentRetention entity - Retention policy assignment
/// Table: Document_Retention (LawFirmDMS database)
/// </summary>
[Table("Document_Retention")]
public class DocumentRetention
{
    [Key]
    public int RetentionID { get; set; }

    public int? DocumentID { get; set; }

    public int? PolicyID { get; set; }

    [Column(TypeName = "date")]
    public DateTime? ExpiryDate { get; set; }

    public bool? IsArchived { get; set; } = false;

    // Navigation properties
    [ForeignKey("DocumentID")]
    public virtual Document? Document { get; set; }

    [ForeignKey("PolicyID")]
    public virtual RetentionPolicy? Policy { get; set; }
}
