using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API controller for chat-related operations (REST endpoints for data loading)
/// Real-time messaging is handled via SignalR ChatHub
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FirmMember")]
public class ChatApiController : ControllerBase
{
    private readonly ChatService _chatService;

    public ChatApiController(ChatService chatService)
    {
        _chatService = chatService;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";

    /// <summary>
    /// Get conversations for the current user (filtered by role)
    /// </summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromQuery] string? status = null)
    {
        var role = GetUserRole();
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        if (role == "Admin")
        {
            var conversations = await _chatService.GetAdminConversationsAsync(firmId, status);
            return Ok(new
            {
                success = true,
                data = conversations.Select(c => new
                {
                    conversationId = c.ConversationID,
                    clientName = c.ClientUser?.FullName ?? "Unknown",
                    clientEmail = c.ClientUser?.Email,
                    adminName = c.AdminUser?.FullName,
                    subject = c.Subject,
                    category = c.Category,
                    status = c.Status,
                    createdAt = c.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    updatedAt = c.UpdatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    lastMessage = c.Messages.FirstOrDefault()?.Content
                })
            });
        }
        else if (role == "Client")
        {
            var conversations = await _chatService.GetClientConversationsAsync(userId);
            return Ok(new
            {
                success = true,
                data = conversations.Select(c => new
                {
                    conversationId = c.ConversationID,
                    adminName = c.AdminUser?.FullName,
                    subject = c.Subject,
                    category = c.Category,
                    status = c.Status,
                    createdAt = c.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    updatedAt = c.UpdatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
                })
            });
        }

        return Forbid();
    }

    /// <summary>
    /// Get messages for a conversation
    /// </summary>
    [HttpGet("conversations/{conversationId}/messages")]
    public async Task<IActionResult> GetMessages(int conversationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var conversation = await _chatService.GetConversationAsync(conversationId);
        if (conversation == null) return NotFound(new { success = false, message = "Conversation not found" });

        // Authorization check
        if (role == "Client" && conversation.ClientUserID != userId)
            return Forbid();
        if (role == "Admin" && conversation.FirmID != firmId)
            return Forbid();

        var messages = await _chatService.GetMessagesAsync(conversationId, page, pageSize);

        return Ok(new
        {
            success = true,
            data = messages.Select(m => new
            {
                messageId = m.MessageID,
                senderType = m.SenderType,
                senderName = m.SenderUser?.FullName ?? (m.SenderType == "Chatbot" ? "CKN Assistant" : "Unknown"),
                content = m.Content,
                messageType = m.MessageType,
                isRead = m.IsRead,
                createdAt = m.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
            })
        });
    }

    /// <summary>
    /// Get unread message count
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var role = GetUserRole();
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        int count;
        if (role == "Admin")
        {
            count = await _chatService.GetUnreadCountForAdminAsync(firmId);
        }
        else if (role == "Client")
        {
            count = await _chatService.GetUnreadCountForClientAsync(userId);
        }
        else
        {
            return Ok(new { success = true, count = 0 });
        }

        return Ok(new { success = true, count });
    }

    /// <summary>
    /// Get the active conversation for the current client
    /// </summary>
    [HttpGet("active-conversation")]
    public async Task<IActionResult> GetActiveConversation()
    {
        var userId = GetCurrentUserId();
        var role = GetUserRole();

        if (role != "Client")
            return Ok(new { success = true, data = (object?)null });

        var conversation = await _chatService.GetActiveConversationForClientAsync(userId);
        if (conversation == null)
            return Ok(new { success = true, data = (object?)null });

        return Ok(new
        {
            success = true,
            data = new
            {
                conversationId = conversation.ConversationID,
                status = conversation.Status,
                adminName = conversation.AdminUser?.FullName,
                subject = conversation.Subject,
                category = conversation.Category
            }
        });
    }

    /// <summary>
    /// Get waiting conversation count (admin only)
    /// </summary>
    [HttpGet("waiting-count")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetWaitingCount()
    {
        var firmId = GetFirmId();
        var conversations = await _chatService.GetWaitingConversationsAsync(firmId);
        var waitingCount = conversations.Count(c => c.Status == "WaitingForAdmin");

        return Ok(new { success = true, count = waitingCount, total = conversations.Count });
    }
}
