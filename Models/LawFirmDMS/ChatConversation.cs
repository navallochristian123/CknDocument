using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// ChatConversation entity - Represents a chat session between a client and admin
/// Table: ChatConversation (LawFirmDMS database)
/// </summary>
[Table("ChatConversation")]
public class ChatConversation : BaseEntity
{
    [Key]
    public int ConversationID { get; set; }

    [Required]
    public int FirmID { get; set; }

    /// <summary>
    /// The client who initiated the conversation
    /// </summary>
    [Required]
    public int ClientUserID { get; set; }

    /// <summary>
    /// The admin assigned to this conversation (null if still in chatbot mode)
    /// </summary>
    public int? AdminUserID { get; set; }

    /// <summary>
    /// Subject/topic of the conversation
    /// </summary>
    [MaxLength(255)]
    public string? Subject { get; set; }

    /// <summary>
    /// Category: LostDocument, ForgottenDocument, UploadHelp, AccountIssue, General
    /// </summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>
    /// Status: Active, WaitingForAdmin, WithAdmin, Closed
    /// - Active: Client is chatting with chatbot
    /// - WaitingForAdmin: Client requested live admin, waiting for admin to join
    /// - WithAdmin: Admin has joined, live chat in progress
    /// - Closed: Conversation ended
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = "Active";

    /// <summary>
    /// When the conversation was closed
    /// </summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// Client satisfaction rating (1-5) after closing
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>
    /// Feedback comment from client after closing
    /// </summary>
    [MaxLength(500)]
    public string? Feedback { get; set; }

    // Navigation properties
    [ForeignKey("FirmID")]
    public virtual Firm? Firm { get; set; }

    [ForeignKey("ClientUserID")]
    public virtual User? ClientUser { get; set; }

    [ForeignKey("AdminUserID")]
    public virtual User? AdminUser { get; set; }

    public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
