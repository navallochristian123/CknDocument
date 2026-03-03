using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CKNDocument.Data;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.LawFirm;

/// <summary>
/// Controller for Chat views - Admin chat management
/// </summary>
[Authorize(Policy = "FirmMember")]
public class ChatController : Controller
{
    private readonly LawFirmDMSDbContext _context;
    private readonly ChatService _chatService;

    public ChatController(LawFirmDMSDbContext context, ChatService chatService)
    {
        _context = context;
        _chatService = chatService;
    }

    /// <summary>
    /// Admin Chat Management page - full chat interface
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    public IActionResult Index()
    {
        return View("~/Views/Admin/Chat.cshtml");
    }

    /// <summary>
    /// Client Chat History page (optional - clients mainly use the floating widget)
    /// </summary>
    [Authorize(Policy = "ClientOnly")]
    public IActionResult History()
    {
        return View("~/Views/Client/ChatHistory.cshtml");
    }
}
