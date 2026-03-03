using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Hubs;

/// <summary>
/// SignalR Hub for real-time chat between clients and admins.
/// Uses cookie authentication (default scheme).
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ChatService chatService, ILogger<ChatHub> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    private int GetUserId() => int.Parse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(Context.User?.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => Context.User?.FindFirst(ClaimTypes.Role)?.Value ?? "Client";
    private string GetUserName() => Context.User?.Identity?.Name ?? "Unknown";

    /// <summary>
    /// Called when client connects. Joins the user to their firm's group and personal group.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        // Join personal group (for direct messages)
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");

        // Join firm group (for admin to see all firm activity)
        await Groups.AddToGroupAsync(Context.ConnectionId, $"firm_{firmId}");

        // If admin, join the admin group
        if (role == "Admin")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"firm_admins_{firmId}");
        }

        _logger.LogInformation("User {UserId} ({Role}) connected to ChatHub", userId, role);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        _logger.LogInformation("User {UserId} disconnected from ChatHub", userId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client starts a new conversation
    /// </summary>
    public async Task StartConversation()
    {
        var userId = GetUserId();
        var firmId = GetFirmId();

        // Check if there's already an active conversation
        var existing = await _chatService.GetActiveConversationForClientAsync(userId);
        if (existing != null)
        {
            // Rejoin existing conversation
            await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{existing.ConversationID}");

            var messages = await _chatService.GetMessagesAsync(existing.ConversationID);
            await Clients.Caller.SendAsync("ConversationResumed", new
            {
                conversationId = existing.ConversationID,
                status = existing.Status,
                messages = messages.Select(m => new
                {
                    messageId = m.MessageID,
                    senderType = m.SenderType,
                    senderName = m.SenderUser?.FullName ?? "CKN Assistant",
                    content = m.Content,
                    messageType = m.MessageType,
                    createdAt = m.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
                })
            });
            return;
        }

        // Create new conversation
        var conversation = await _chatService.CreateConversationAsync(firmId, userId, "Support Chat");

        // Join conversation group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversation.ConversationID}");

        // Send chatbot greeting
        var (greeting, options) = ChatService.GetGreeting();
        var botMessage = await _chatService.AddMessageAsync(
            conversation.ConversationID, null, "Chatbot", greeting, "Text");

        await Clients.Caller.SendAsync("ConversationStarted", new
        {
            conversationId = conversation.ConversationID,
            status = conversation.Status
        });

        await Clients.Caller.SendAsync("ReceiveMessage", new
        {
            messageId = botMessage.MessageID,
            senderType = "Chatbot",
            senderName = "CKN Assistant",
            content = greeting,
            messageType = "Text",
            options = options,
            createdAt = botMessage.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        });
    }

    /// <summary>
    /// Client sends a message
    /// </summary>
    public async Task SendMessage(int conversationId, string message)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        var conversation = await _chatService.GetConversationAsync(conversationId);
        if (conversation == null) return;

        // Verify the user belongs to this conversation
        if (role == "Client" && conversation.ClientUserID != userId) return;
        if (role == "Admin" && conversation.AdminUserID != userId && conversation.AdminUserID != null) return;

        var senderType = role == "Admin" ? "Admin" : "Client";
        var userName = Context.User?.FindFirst(ClaimTypes.Name)?.Value ??
                       Context.User?.Identity?.Name ?? "Unknown";

        // Save message to database
        var chatMessage = await _chatService.AddMessageAsync(conversationId, userId, senderType, message);

        // Broadcast message to conversation group
        await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
        {
            messageId = chatMessage.MessageID,
            senderType = senderType,
            senderName = userName,
            content = message,
            messageType = "Text",
            options = (string[]?)null,
            createdAt = chatMessage.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        });

        // If client is chatting with chatbot (no admin assigned), process auto-response
        if (senderType == "Client" && (conversation.Status == "Active"))
        {
            var (response, options, category, shouldEscalate) = ChatService.ProcessChatbotMessage(message);

            if (shouldEscalate)
            {
                await EscalateToAdmin(conversationId);
                return;
            }

            if (response != null)
            {
                // Update category if detected
                if (category != null && string.IsNullOrEmpty(conversation.Category))
                {
                    conversation.Category = category;
                }

                var botMessage = await _chatService.AddMessageAsync(conversationId, null, "Chatbot", response, "Text");

                await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
                {
                    messageId = botMessage.MessageID,
                    senderType = "Chatbot",
                    senderName = "CKN Assistant",
                    content = response,
                    messageType = "Text",
                    options = options,
                    createdAt = botMessage.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
                });
            }
        }

        // Notify admin group about new messages
        if (senderType == "Client")
        {
            var firmId = GetFirmId();
            await Clients.Group($"firm_admins_{firmId}").SendAsync("NewClientMessage", new
            {
                conversationId = conversationId,
                clientName = userName,
                preview = message.Length > 50 ? message[..50] + "..." : message,
                status = conversation.Status
            });
        }
    }

    /// <summary>
    /// Escalate conversation to live admin
    /// </summary>
    public async Task EscalateToAdmin(int conversationId)
    {
        var userId = GetUserId();
        var firmId = GetFirmId();

        var conversation = await _chatService.GetConversationAsync(conversationId);
        if (conversation == null || conversation.ClientUserID != userId) return;

        await _chatService.EscalateToAdminAsync(conversationId);

        // System notice
        var systemMsg = await _chatService.AddMessageAsync(
            conversationId, null, "Chatbot",
            "You've been placed in queue for a live admin. An admin will join shortly. Please wait...",
            "SystemNotice");

        await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
        {
            messageId = systemMsg.MessageID,
            senderType = "Chatbot",
            senderName = "System",
            content = systemMsg.Content,
            messageType = "SystemNotice",
            options = (string[]?)null,
            createdAt = systemMsg.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        });

        await Clients.Group($"conversation_{conversationId}").SendAsync("StatusChanged", "WaitingForAdmin");

        // Notify admin group
        await Clients.Group($"firm_admins_{firmId}").SendAsync("NewChatRequest", new
        {
            conversationId = conversationId,
            clientName = conversation.ClientUser?.FullName ?? "Client",
            category = conversation.Category,
            subject = conversation.Subject,
            createdAt = conversation.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        });
    }

    /// <summary>
    /// Admin joins a conversation
    /// </summary>
    public async Task AdminJoinConversation(int conversationId)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        if (role != "Admin") return;

        var conversation = await _chatService.GetConversationAsync(conversationId);
        if (conversation == null) return;

        // Verify same firm
        var firmId = GetFirmId();
        if (conversation.FirmID != firmId) return;

        // Assign admin
        await _chatService.AdminJoinConversationAsync(conversationId, userId);

        // Join conversation group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");

        // Load conversation history for admin
        var messages = await _chatService.GetMessagesAsync(conversationId);

        var adminName = Context.User?.FindFirst(ClaimTypes.Name)?.Value ??
                        Context.User?.Identity?.Name ?? "Admin";

        // System notice that admin joined
        var systemMsg = await _chatService.AddMessageAsync(
            conversationId, null, "Chatbot",
            $"{adminName} has joined the conversation. You're now chatting with a live admin.",
            "SystemNotice");

        // Send conversation history to admin
        await Clients.Caller.SendAsync("ConversationJoined", new
        {
            conversationId = conversationId,
            clientName = conversation.ClientUser?.FullName ?? "Client",
            category = conversation.Category,
            subject = conversation.Subject,
            messages = messages.Select(m => new
            {
                messageId = m.MessageID,
                senderType = m.SenderType,
                senderName = m.SenderUser?.FullName ?? (m.SenderType == "Chatbot" ? "CKN Assistant" : "Unknown"),
                content = m.Content,
                messageType = m.MessageType,
                createdAt = m.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
            })
        });

        // Notify client that admin joined
        await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
        {
            messageId = systemMsg.MessageID,
            senderType = "Chatbot",
            senderName = "System",
            content = systemMsg.Content,
            messageType = "SystemNotice",
            options = (string[]?)null,
            createdAt = systemMsg.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        });

        await Clients.Group($"conversation_{conversationId}").SendAsync("StatusChanged", "WithAdmin");

        // Mark messages as read by admin
        await _chatService.MarkMessagesAsReadAsync(conversationId, userId);

        // Notify admin group to refresh list
        await Clients.Group($"firm_admins_{firmId}").SendAsync("ConversationUpdated", conversationId);
    }

    /// <summary>
    /// Close a conversation
    /// </summary>
    public async Task CloseConversation(int conversationId, int? rating = null, string? feedback = null)
    {
        var userId = GetUserId();
        var role = GetUserRole();

        var conversation = await _chatService.GetConversationAsync(conversationId);
        if (conversation == null) return;

        // Only admin or the client can close
        if (role == "Client" && conversation.ClientUserID != userId) return;
        if (role == "Admin" && conversation.FirmID != GetFirmId()) return;

        await _chatService.CloseConversationAsync(conversationId, rating, feedback);

        var closedBy = role == "Admin" ? "Admin" : "Client";

        var systemMsg = await _chatService.AddMessageAsync(
            conversationId, null, "Chatbot",
            $"This conversation has been closed by {closedBy}. Thank you for using CKN Document Support!",
            "SystemNotice");

        await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", new
        {
            messageId = systemMsg.MessageID,
            senderType = "Chatbot",
            senderName = "System",
            content = systemMsg.Content,
            messageType = "SystemNotice",
            options = (string[]?)null,
            createdAt = systemMsg.CreatedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        });

        await Clients.Group($"conversation_{conversationId}").SendAsync("StatusChanged", "Closed");

        // Notify admin group
        var firmId = GetFirmId();
        await Clients.Group($"firm_admins_{firmId}").SendAsync("ConversationUpdated", conversationId);
    }

    /// <summary>
    /// Mark messages as read
    /// </summary>
    public async Task MarkAsRead(int conversationId)
    {
        var userId = GetUserId();
        await _chatService.MarkMessagesAsReadAsync(conversationId, userId);
        await Clients.Group($"conversation_{conversationId}").SendAsync("MessagesRead", new
        {
            conversationId = conversationId,
            readBy = userId
        });
    }

    /// <summary>
    /// Typing indicator
    /// </summary>
    public async Task Typing(int conversationId, bool isTyping)
    {
        var userId = GetUserId();
        var role = GetUserRole();
        var name = Context.User?.FindFirst(ClaimTypes.Name)?.Value ??
                   Context.User?.Identity?.Name ?? "Someone";

        await Clients.OthersInGroup($"conversation_{conversationId}").SendAsync("UserTyping", new
        {
            userId = userId,
            name = name,
            role = role,
            isTyping = isTyping
        });
    }
}
