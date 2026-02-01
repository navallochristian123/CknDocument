namespace CKNDocument.Models.Common;

/// <summary>
/// Interface for entities that track audit information
/// </summary>
public interface IAuditableEntity
{
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
}
