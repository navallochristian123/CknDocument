namespace CKNDocument.Models.Common;

/// <summary>
/// Base entity with common audit properties
/// </summary>
public abstract class BaseEntity
{
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
