using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// ClientFolder entity - Document organization folders
/// Table: ClientFolder (LawFirmDMS database)
/// </summary>
[Table("ClientFolder")]
public class ClientFolder : BaseEntity
{
    [Key]
    public int FolderId { get; set; }

    [Required]
    public int ClientId { get; set; }

    [Required]
    public int FirmId { get; set; }

    public int? ParentFolderId { get; set; }

    [Required]
    [MaxLength(255)]
    public string FolderName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string? Color { get; set; }

    // Navigation properties
    [ForeignKey("ClientId")]
    public virtual User? Client { get; set; }

    [ForeignKey("FirmId")]
    public virtual Firm? Firm { get; set; }

    [ForeignKey("ParentFolderId")]
    public virtual ClientFolder? ParentFolder { get; set; }

    public virtual ICollection<ClientFolder> ChildFolders { get; set; } = new List<ClientFolder>();
    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}
