using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// Stores the OpenAI analysis results for a document.
/// Created when a client uploads a document - AI reads the content and provides:
/// - Document type detection
/// - Auto-generated compliance checklist
/// - Missing items / deficiency report
/// - Summary of document contents
/// </summary>
[Table("DocumentAIAnalyses")]
public class DocumentAIAnalysis : BaseEntity
{
    [Key]
    public int AnalysisId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    public int FirmId { get; set; }

    /// <summary>AI-detected document type (e.g., Contract, Affidavit, NDA)</summary>
    [MaxLength(150)]
    public string? DetectedDocumentType { get; set; }

    /// <summary>Confidence score 0-100</summary>
    public double? Confidence { get; set; }

    /// <summary>AI-generated summary of the document</summary>
    public string? Summary { get; set; }

    /// <summary>JSON array of AI-generated checklist items with pass/fail status
    /// Format: [{"item":"...","status":"pass|fail|warning","detail":"..."}]
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? ChecklistJson { get; set; }

    /// <summary>JSON array of issues/deficiencies found by AI
    /// Format: [{"severity":"high|medium|low","issue":"...","recommendation":"..."}]
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? IssuesJson { get; set; }

    /// <summary>JSON array of missing items the AI identified
    /// Format: [{"item":"...","importance":"required|recommended","detail":"..."}]
    /// </summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? MissingItemsJson { get; set; }

    /// <summary>Full raw AI response for reference</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? RawResponseJson { get; set; }

    /// <summary>Text content extracted from the document for AI processing</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? ExtractedText { get; set; }

    /// <summary>Whether analysis completed successfully</summary>
    public bool IsProcessed { get; set; } = false;

    /// <summary>When the analysis was completed</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Error message if analysis failed</summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>OpenAI model used</summary>
    [MaxLength(50)]
    public string? ModelUsed { get; set; }

    /// <summary>Tokens used for the API call</summary>
    public int? TokensUsed { get; set; }

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document? Document { get; set; }

    [ForeignKey("FirmId")]
    public virtual Firm? Firm { get; set; }
}
