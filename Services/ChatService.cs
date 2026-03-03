using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using Microsoft.EntityFrameworkCore;

namespace CKNDocument.Services;

/// <summary>
/// Service for managing chat conversations, messages, and chatbot auto-responses
/// </summary>
public class ChatService
{
    private readonly LawFirmDMSDbContext _context;

    public ChatService(LawFirmDMSDbContext context)
    {
        _context = context;
    }

    // ==========================================
    // Chatbot Auto-Response Logic
    // ==========================================

    /// <summary>
    /// FAQ categories and automated responses
    /// </summary>
    private static readonly Dictionary<string, ChatbotFAQ> FAQs = new()
    {
        ["lost_document"] = new ChatbotFAQ
        {
            Category = "LostDocument",
            Keywords = new[] { "lost", "missing", "can't find", "cannot find", "disappeared", "gone", "where is my document", "lost document", "missing document", "lost file" },
            Response = "I understand you're having trouble finding a document. Here are some steps that might help:\n\n" +
                       "1. **Check your 'My Documents' page** - All your uploaded documents are listed there.\n" +
                       "2. **Use the Search feature** - Try searching by document name or type.\n" +
                       "3. **Check Archive** - Your document may have been archived. Visit the Archive section.\n" +
                       "4. **Check with your assigned lawyer/staff** - They may have moved or reorganized your documents.\n\n" +
                       "Would you like to speak with a live admin for further assistance?",
            Options = new[] { "Search My Documents", "Check Archive", "Talk to Live Admin" }
        },
        ["forgotten_document"] = new ChatbotFAQ
        {
            Category = "ForgottenDocument",
            Keywords = new[] { "forgot", "forgotten", "don't remember", "which document", "what document", "forgot to upload", "forgot document", "forget" },
            Response = "It sounds like you may have forgotten which document you need or forgot to upload one. Here's what you can do:\n\n" +
                       "1. **Review your document checklist** - Check if there are pending items in your checklist.\n" +
                       "2. **Check notifications** - You may have received reminders about required documents.\n" +
                       "3. **Upload a new document** - Go to 'Upload' to submit any missing documents.\n\n" +
                       "Would you like more help?",
            Options = new[] { "View My Checklist", "Upload Document", "Talk to Live Admin" }
        },
        ["upload_help"] = new ChatbotFAQ
        {
            Category = "UploadHelp",
            Keywords = new[] { "upload", "how to upload", "submit", "send document", "attach", "file upload", "upload document", "how do i upload", "submit document" },
            Response = "To upload a document, follow these steps:\n\n" +
                       "1. Go to **'Upload'** from the sidebar menu.\n" +
                       "2. Click **'Choose File'** or drag and drop your document.\n" +
                       "3. Select the **document type/category**.\n" +
                       "4. Add a **title and description** (optional).\n" +
                       "5. Click **'Upload'** to submit.\n\n" +
                       "Supported formats: PDF, DOCX, DOC, JPG, PNG.\n\n" +
                       "Need more help?",
            Options = new[] { "Go to Upload Page", "Talk to Live Admin" }
        },
        ["account_issue"] = new ChatbotFAQ
        {
            Category = "AccountIssue",
            Keywords = new[] { "password", "account", "login", "can't login", "locked", "reset password", "change password", "email", "account issue", "locked out", "access denied" },
            Response = "For account-related issues:\n\n" +
                       "1. **Forgot password?** - Use the 'Forgot Password' link on the login page.\n" +
                       "2. **Account locked?** - After multiple failed attempts, your account may be temporarily locked. Wait 15 minutes and try again.\n" +
                       "3. **Email not confirmed?** - Check your email inbox and spam folder for a confirmation link.\n" +
                       "4. **Can't access your account?** - Contact your firm's admin for assistance.\n\n" +
                       "Would you like to speak with an admin?",
            Options = new[] { "Reset Password", "Talk to Live Admin" }
        }
    };

    /// <summary>
    /// Gets the initial greeting message options for the chatbot
    /// </summary>
    public static (string message, string[] options) GetGreeting()
    {
        var message = "👋 Hello! I'm the CKN Document Assistant. How can I help you today?\n\nPlease select a topic or type your question:";
        var options = new[]
        {
            "I lost or can't find a document",
            "I forgot which document to submit",
            "How do I upload a document?",
            "I have an account/password issue",
            "Talk to a Live Admin"
        };
        return (message, options);
    }

    /// <summary>
    /// Processes a client message and returns chatbot response (if applicable)
    /// </summary>
    public static (string? response, string[]? options, string? category, bool shouldEscalate) ProcessChatbotMessage(string userMessage)
    {
        var message = userMessage.ToLower().Trim();

        // Check for live admin request
        if (message.Contains("live admin") || message.Contains("talk to admin") || message.Contains("real person") ||
            message.Contains("human") || message.Contains("speak to admin") || message.Contains("live chat") ||
            message.Contains("talk to a live admin"))
        {
            return (
                "I'll connect you with a live admin now. Please wait a moment while an admin joins the chat...",
                null,
                "General",
                true
            );
        }

        // Match against FAQ keywords
        foreach (var faq in FAQs.Values)
        {
            if (faq.Keywords.Any(k => message.Contains(k)))
            {
                return (faq.Response, faq.Options, faq.Category, false);
            }
        }

        // Default response if no match
        return (
            "I'm not sure I understand your question. Here are some things I can help with:\n\n" +
            "• Finding lost or missing documents\n" +
            "• Guidance on document uploads\n" +
            "• Account and password issues\n\n" +
            "You can also choose to speak with a live admin for personalized assistance.",
            new[] { "I lost a document", "Help with upload", "Account issue", "Talk to Live Admin" },
            null,
            false
        );
    }

    // ==========================================
    // Conversation Management
    // ==========================================

    /// <summary>
    /// Creates a new chat conversation
    /// </summary>
    public async Task<ChatConversation> CreateConversationAsync(int firmId, int clientUserId, string? subject = null, string? category = null)
    {
        var conversation = new ChatConversation
        {
            FirmID = firmId,
            ClientUserID = clientUserId,
            Subject = subject ?? "New Support Chat",
            Category = category,
            Status = "Active",
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatConversations.Add(conversation);
        await _context.SaveChangesAsync();
        return conversation;
    }

    /// <summary>
    /// Adds a message to a conversation
    /// </summary>
    public async Task<ChatMessage> AddMessageAsync(int conversationId, int? senderUserId, string senderType, string content, string messageType = "Text")
    {
        var message = new ChatMessage
        {
            ConversationID = conversationId,
            SenderUserID = senderUserId,
            SenderType = senderType,
            Content = content,
            MessageType = messageType,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.ChatMessages.Add(message);

        // Update conversation's UpdatedAt
        var conversation = await _context.ChatConversations.FindAsync(conversationId);
        if (conversation != null)
        {
            conversation.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return message;
    }

    /// <summary>
    /// Escalates a conversation to live admin
    /// </summary>
    public async Task<bool> EscalateToAdminAsync(int conversationId)
    {
        var conversation = await _context.ChatConversations.FindAsync(conversationId);
        if (conversation == null) return false;

        conversation.Status = "WaitingForAdmin";
        conversation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Admin joins a conversation
    /// </summary>
    public async Task<bool> AdminJoinConversationAsync(int conversationId, int adminUserId)
    {
        var conversation = await _context.ChatConversations.FindAsync(conversationId);
        if (conversation == null) return false;

        conversation.AdminUserID = adminUserId;
        conversation.Status = "WithAdmin";
        conversation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Closes a conversation
    /// </summary>
    public async Task<bool> CloseConversationAsync(int conversationId, int? rating = null, string? feedback = null)
    {
        var conversation = await _context.ChatConversations.FindAsync(conversationId);
        if (conversation == null) return false;

        conversation.Status = "Closed";
        conversation.ClosedAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;
        conversation.Rating = rating;
        conversation.Feedback = feedback;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Gets conversations waiting for admin (within a firm)
    /// </summary>
    public async Task<List<ChatConversation>> GetWaitingConversationsAsync(int firmId)
    {
        return await _context.ChatConversations
            .Include(c => c.ClientUser)
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
            .Where(c => c.FirmID == firmId && (c.Status == "WaitingForAdmin" || c.Status == "WithAdmin"))
            .OrderByDescending(c => c.Status == "WaitingForAdmin" ? 0 : 1)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all conversations for an admin (within a firm)
    /// </summary>
    public async Task<List<ChatConversation>> GetAdminConversationsAsync(int firmId, string? statusFilter = null)
    {
        var query = _context.ChatConversations
            .Include(c => c.ClientUser)
            .Include(c => c.AdminUser)
            .Where(c => c.FirmID == firmId);

        if (!string.IsNullOrEmpty(statusFilter))
        {
            query = query.Where(c => c.Status == statusFilter);
        }

        return await query
            .OrderByDescending(c => c.Status == "WaitingForAdmin" ? 0 : c.Status == "WithAdmin" ? 1 : 2)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets conversations for a client
    /// </summary>
    public async Task<List<ChatConversation>> GetClientConversationsAsync(int clientUserId)
    {
        return await _context.ChatConversations
            .Include(c => c.AdminUser)
            .Where(c => c.ClientUserID == clientUserId)
            .OrderByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets messages for a conversation
    /// </summary>
    public async Task<List<ChatMessage>> GetMessagesAsync(int conversationId, int page = 1, int pageSize = 50)
    {
        return await _context.ChatMessages
            .Include(m => m.SenderUser)
            .Where(m => m.ConversationID == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// Gets a conversation by ID with validation
    /// </summary>
    public async Task<ChatConversation?> GetConversationAsync(int conversationId)
    {
        return await _context.ChatConversations
            .Include(c => c.ClientUser)
            .Include(c => c.AdminUser)
            .FirstOrDefaultAsync(c => c.ConversationID == conversationId);
    }

    /// <summary>
    /// Marks messages as read
    /// </summary>
    public async Task MarkMessagesAsReadAsync(int conversationId, int readerUserId)
    {
        var unreadMessages = await _context.ChatMessages
            .Where(m => m.ConversationID == conversationId && !m.IsRead && m.SenderUserID != readerUserId)
            .ToListAsync();

        foreach (var msg in unreadMessages)
        {
            msg.IsRead = true;
            msg.ReadAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Gets unread message count for admin (all waiting + unread in active conversations)
    /// </summary>
    public async Task<int> GetUnreadCountForAdminAsync(int firmId)
    {
        return await _context.ChatConversations
            .Where(c => c.FirmID == firmId && (c.Status == "WaitingForAdmin" || c.Status == "WithAdmin"))
            .SelectMany(c => c.Messages)
            .Where(m => !m.IsRead && m.SenderType == "Client")
            .CountAsync();
    }

    /// <summary>
    /// Gets unread message count for a client
    /// </summary>
    public async Task<int> GetUnreadCountForClientAsync(int clientUserId)
    {
        return await _context.ChatConversations
            .Where(c => c.ClientUserID == clientUserId && c.Status != "Closed")
            .SelectMany(c => c.Messages)
            .Where(m => !m.IsRead && (m.SenderType == "Admin" || m.SenderType == "Chatbot"))
            .CountAsync();
    }

    /// <summary>
    /// Gets or creates an active conversation for a client
    /// </summary>
    public async Task<ChatConversation?> GetActiveConversationForClientAsync(int clientUserId)
    {
        return await _context.ChatConversations
            .Include(c => c.AdminUser)
            .FirstOrDefaultAsync(c => c.ClientUserID == clientUserId && c.Status != "Closed");
    }
}

/// <summary>
/// Helper class for FAQ definitions
/// </summary>
public class ChatbotFAQ
{
    public string Category { get; set; } = string.Empty;
    public string[] Keywords { get; set; } = Array.Empty<string>();
    public string Response { get; set; } = string.Empty;
    public string[] Options { get; set; } = Array.Empty<string>();
}
