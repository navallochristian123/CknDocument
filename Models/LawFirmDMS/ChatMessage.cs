using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CKNDocument.Models.Common;

namespace CKNDocument.Models.LawFirmDMS;

/// <summary>
/// ChatMessage entity - Individual messages within a chat conversation
/// Table: ChatMessage (LawFirmDMS database)
/// </summary>
[Table("ChatMessage")]
public class ChatMessage : BaseEntity
{
    [Key]
    public int MessageID { get; set; }

    [Required]
    public int ConversationID { get; set; }

    /// <summary>
    /// UserID of the sender (null if sent by chatbot)
    /// </summary>
    public int? SenderUserID { get; set; }

    /// <summary>
    /// Who sent this message: Client, Admin, Chatbot
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string SenderType { get; set; } = "Client";

    /// <summary>
    /// The message content
    /// </summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Message type: Text, Option, SystemNotice
    /// - Text: Regular text message
    /// - Option: Chatbot option button (content holds JSON of options)
    /// - SystemNotice: System notification (admin joined, conversation closed, etc.)
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string MessageType { get; set; } = "Text";

    /// <summary>
    /// Whether the message has been read by the recipient
    /// </summary>
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// When the message was read
    /// </summary>
    public DateTime? ReadAt { get; set; }

    // Navigation properties
    [ForeignKey("ConversationID")]
    public virtual ChatConversation? Conversation { get; set; }

    [ForeignKey("SenderUserID")]
    public virtual User? SenderUser { get; set; }
}
