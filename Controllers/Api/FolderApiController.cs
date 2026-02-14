using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using CKNDocument.Services;
using System.Security.Claims;

namespace CKNDocument.Controllers.Api;

/// <summary>
/// API Controller for Folder operations
/// Handles folder CRUD for clients
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "FirmMember")]
public class FolderApiController : ControllerBase
{
    private readonly LawFirmDMSDbContext _context;
    private readonly AuditLogService _auditLogService;
    private readonly ILogger<FolderApiController> _logger;

    public FolderApiController(
        LawFirmDMSDbContext context,
        AuditLogService auditLogService,
        ILogger<FolderApiController> logger)
    {
        _context = context;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private int GetCurrentUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetFirmId() => int.Parse(User.FindFirst("FirmId")?.Value ?? "0");
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? "Client";

    /// <summary>
    /// Get all folders for the current client
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetFolders([FromQuery] int? parentFolderId = null)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        IQueryable<ClientFolder> query;

        if (role == "Client")
        {
            // Clients only see their own folders
            query = _context.ClientFolders
                .Include(f => f.ChildFolders)
                .Include(f => f.Documents.Where(d => d.WorkflowStage != "Archived"))
                .Where(f => f.ClientId == userId && f.FirmId == firmId);
        }
        else
        {
            // Staff/Admin can see all folders in the firm
            query = _context.ClientFolders
                .Include(f => f.Client)
                .Include(f => f.ChildFolders)
                .Include(f => f.Documents.Where(d => d.WorkflowStage != "Archived"))
                .Where(f => f.FirmId == firmId);
        }

        if (parentFolderId.HasValue)
        {
            query = query.Where(f => f.ParentFolderId == parentFolderId);
        }
        else
        {
            // Root folders (no parent)
            query = query.Where(f => f.ParentFolderId == null);
        }

        var folders = await query
            .OrderBy(f => f.FolderName)
            .Select(f => new
            {
                id = f.FolderId,
                name = f.FolderName,
                description = f.Description,
                color = f.Color,
                parentFolderId = f.ParentFolderId,
                clientId = f.ClientId,
                clientName = f.Client != null ? f.Client.FullName : null,
                documentCount = f.Documents.Count,
                childFolderCount = f.ChildFolders.Count,
                createdAt = f.CreatedAt
            })
            .ToListAsync();

        return Ok(new { success = true, folders });
    }

    /// <summary>
    /// Get folder details with contents
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetFolder(int id)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        var folder = await _context.ClientFolders
            .Include(f => f.Client)
            .Include(f => f.ParentFolder)
            .Include(f => f.ChildFolders)
            .Include(f => f.Documents.Where(d => d.WorkflowStage != "Archived"))
            .FirstOrDefaultAsync(f => f.FolderId == id && f.FirmId == firmId);

        if (folder == null)
            return NotFound(new { success = false, message = "Folder not found" });

        // Check access
        if (role == "Client" && folder.ClientId != userId)
            return Forbid();

        // Build breadcrumb path
        var breadcrumbs = new List<object>();
        var currentFolder = folder;
        while (currentFolder != null)
        {
            breadcrumbs.Insert(0, new { id = currentFolder.FolderId, name = currentFolder.FolderName });
            currentFolder = await _context.ClientFolders.FirstOrDefaultAsync(f => f.FolderId == currentFolder.ParentFolderId);
        }

        return Ok(new
        {
            success = true,
            folder = new
            {
                id = folder.FolderId,
                name = folder.FolderName,
                description = folder.Description,
                color = folder.Color,
                parentFolderId = folder.ParentFolderId,
                parentFolderName = folder.ParentFolder?.FolderName,
                clientId = folder.ClientId,
                clientName = folder.Client?.FullName,
                createdAt = folder.CreatedAt,
                breadcrumbs = breadcrumbs,
                childFolders = folder.ChildFolders.Select(cf => new
                {
                    id = cf.FolderId,
                    name = cf.FolderName,
                    color = cf.Color
                }),
                documents = folder.Documents.Select(d => new
                {
                    id = d.DocumentID,
                    title = d.Title,
                    originalFileName = d.OriginalFileName,
                    fileExtension = d.FileExtension,
                    status = d.Status,
                    workflowStage = d.WorkflowStage,
                    createdAt = d.CreatedAt
                })
            }
        });
    }

    /// <summary>
    /// Create a new folder
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        if (string.IsNullOrWhiteSpace(dto.FolderName))
            return BadRequest(new { success = false, message = "Folder name is required" });

        // Check if parent folder exists and belongs to the user
        if (dto.ParentFolderId.HasValue)
        {
            var parentFolder = await _context.ClientFolders
                .FirstOrDefaultAsync(f => f.FolderId == dto.ParentFolderId && f.ClientId == userId && f.FirmId == firmId);
            if (parentFolder == null)
                return BadRequest(new { success = false, message = "Parent folder not found or access denied" });
        }

        // Check for duplicate folder name at the same level
        var existingFolder = await _context.ClientFolders
            .FirstOrDefaultAsync(f => f.ClientId == userId &&
                                      f.FirmId == firmId &&
                                      f.ParentFolderId == dto.ParentFolderId &&
                                      f.FolderName.ToLower() == dto.FolderName.ToLower());
        if (existingFolder != null)
            return BadRequest(new { success = false, message = "A folder with this name already exists" });

        var folder = new ClientFolder
        {
            ClientId = userId,
            FirmId = firmId,
            ParentFolderId = dto.ParentFolderId,
            FolderName = dto.FolderName.Trim(),
            Description = dto.Description,
            Color = dto.Color ?? "#FFC107", // Default yellow
            CreatedAt = DateTime.UtcNow
        };

        _context.ClientFolders.Add(folder);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "FolderCreate",
            "ClientFolder",
            folder.FolderId,
            $"Created folder: {folder.FolderName}",
            null,
            null,
            "FolderManagement");

        return Ok(new
        {
            success = true,
            message = "Folder created successfully",
            folder = new
            {
                id = folder.FolderId,
                name = folder.FolderName,
                description = folder.Description,
                color = folder.Color,
                parentFolderId = folder.ParentFolderId
            }
        });
    }

    /// <summary>
    /// Update folder
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> UpdateFolder(int id, [FromBody] UpdateFolderDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var folder = await _context.ClientFolders
            .FirstOrDefaultAsync(f => f.FolderId == id && f.ClientId == userId && f.FirmId == firmId);

        if (folder == null)
            return NotFound(new { success = false, message = "Folder not found" });

        if (!string.IsNullOrWhiteSpace(dto.FolderName))
        {
            // Check for duplicate name
            var existingFolder = await _context.ClientFolders
                .FirstOrDefaultAsync(f => f.ClientId == userId &&
                                          f.FirmId == firmId &&
                                          f.ParentFolderId == folder.ParentFolderId &&
                                          f.FolderName.ToLower() == dto.FolderName.ToLower() &&
                                          f.FolderId != id);
            if (existingFolder != null)
                return BadRequest(new { success = false, message = "A folder with this name already exists" });

            folder.FolderName = dto.FolderName.Trim();
        }

        if (dto.Description != null)
            folder.Description = dto.Description;

        if (!string.IsNullOrEmpty(dto.Color))
            folder.Color = dto.Color;

        folder.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "FolderUpdate",
            "ClientFolder",
            folder.FolderId,
            $"Updated folder: {folder.FolderName}",
            null,
            null,
            "FolderManagement");

        return Ok(new { success = true, message = "Folder updated successfully" });
    }

    /// <summary>
    /// Delete folder (only if empty)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> DeleteFolder(int id)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var folder = await _context.ClientFolders
            .Include(f => f.Documents)
            .Include(f => f.ChildFolders)
            .FirstOrDefaultAsync(f => f.FolderId == id && f.ClientId == userId && f.FirmId == firmId);

        if (folder == null)
            return NotFound(new { success = false, message = "Folder not found" });

        // Check if folder has contents
        if (folder.Documents.Any() || folder.ChildFolders.Any())
            return BadRequest(new { success = false, message = "Cannot delete folder with contents. Move or delete contents first." });

        var folderName = folder.FolderName;
        _context.ClientFolders.Remove(folder);
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "FolderDelete",
            "ClientFolder",
            id,
            $"Deleted folder: {folderName}",
            null,
            null,
            "FolderManagement");

        return Ok(new { success = true, message = "Folder deleted successfully" });
    }

    /// <summary>
    /// Move folder to different parent
    /// </summary>
    [HttpPost("{id}/move")]
    [Authorize(Policy = "ClientOnly")]
    public async Task<IActionResult> MoveFolder(int id, [FromBody] MoveFolderDto dto)
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();

        var folder = await _context.ClientFolders
            .FirstOrDefaultAsync(f => f.FolderId == id && f.ClientId == userId && f.FirmId == firmId);

        if (folder == null)
            return NotFound(new { success = false, message = "Folder not found" });

        // Validate target parent
        if (dto.NewParentFolderId.HasValue)
        {
            // Cannot move folder into itself or its descendants
            if (dto.NewParentFolderId == id)
                return BadRequest(new { success = false, message = "Cannot move folder into itself" });

            var targetFolder = await _context.ClientFolders
                .FirstOrDefaultAsync(f => f.FolderId == dto.NewParentFolderId && f.ClientId == userId && f.FirmId == firmId);
            if (targetFolder == null)
                return BadRequest(new { success = false, message = "Target folder not found" });

            // Check if target is a descendant
            var currentParent = targetFolder;
            while (currentParent != null)
            {
                if (currentParent.ParentFolderId == id)
                    return BadRequest(new { success = false, message = "Cannot move folder into its own descendant" });
                currentParent = await _context.ClientFolders.FirstOrDefaultAsync(f => f.FolderId == currentParent.ParentFolderId);
            }
        }

        folder.ParentFolderId = dto.NewParentFolderId;
        folder.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditLogService.LogAsync(
            "FolderMove",
            "ClientFolder",
            folder.FolderId,
            $"Moved folder: {folder.FolderName}",
            null,
            null,
            "FolderManagement");

        return Ok(new { success = true, message = "Folder moved successfully" });
    }

    /// <summary>
    /// Get folder tree structure
    /// </summary>
    [HttpGet("tree")]
    public async Task<IActionResult> GetFolderTree()
    {
        var userId = GetCurrentUserId();
        var firmId = GetFirmId();
        var role = GetUserRole();

        List<ClientFolder> allFolders;

        if (role == "Client")
        {
            allFolders = await _context.ClientFolders
                .Where(f => f.ClientId == userId && f.FirmId == firmId)
                .OrderBy(f => f.FolderName)
                .ToListAsync();
        }
        else
        {
            allFolders = await _context.ClientFolders
                .Include(f => f.Client)
                .Where(f => f.FirmId == firmId)
                .OrderBy(f => f.FolderName)
                .ToListAsync();
        }

        var tree = BuildFolderTree(allFolders, null);

        return Ok(new { success = true, tree });
    }

    /// <summary>
    /// Get all clients with their folders (Admin only)
    /// </summary>
    [HttpGet("clients")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> GetClientsWithFolders()
    {
        var firmId = GetFirmId();

        var clients = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId && 
                        u.Status == "Active" &&
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Client"))
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new
            {
                id = u.UserID,
                fullName = u.FullName,
                email = u.Email,
                folderCount = _context.ClientFolders.Count(f => f.ClientId == u.UserID && f.FirmId == firmId),
                documentCount = _context.Documents.Count(d => d.UploadedBy == u.UserID && d.FirmID == firmId)
            })
            .ToListAsync();

        return Ok(new { success = true, clients });
    }

    /// <summary>
    /// Search approved documents uploaded by a specific client (Admin only)
    /// </summary>
    [HttpGet("client/{clientId}/approved-documents")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> GetClientApprovedDocuments(int clientId, [FromQuery] string? search = null)
    {
        var firmId = GetFirmId();

        // Verify client exists and belongs to the firm
        var client = await _context.Users
            .FirstOrDefaultAsync(u => u.UserID == clientId && u.FirmID == firmId);

        if (client == null)
            return NotFound(new { success = false, message = "Client not found" });

        var query = _context.Documents
            .Include(d => d.Folder)
            .Include(d => d.Uploader)
            .Where(d => d.FirmID == firmId && 
                        d.UploadedBy == clientId && 
                        d.Status == "Completed" &&
                        d.WorkflowStage != "Archived");

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(d => 
                (d.Title != null && d.Title.ToLower().Contains(search)) ||
                (d.OriginalFileName != null && d.OriginalFileName.ToLower().Contains(search)) ||
                (d.DocumentType != null && d.DocumentType.ToLower().Contains(search)));
        }

        var documents = await query
            .OrderByDescending(d => d.ApprovedAt ?? d.CreatedAt)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                originalFileName = d.OriginalFileName,
                documentType = d.DocumentType,
                fileExtension = d.FileExtension,
                totalFileSize = d.TotalFileSize,
                status = d.Status,
                workflowStage = d.WorkflowStage,
                folderName = d.Folder != null ? d.Folder.FolderName : null,
                folderId = d.FolderId,
                uploadedAt = d.CreatedAt,
                approvedAt = d.ApprovedAt,
                currentVersion = d.CurrentVersion
            })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            client = new
            {
                id = client.UserID,
                fullName = client.FullName,
                email = client.Email
            },
            documents,
            totalCount = documents.Count
        });
    }

    /// <summary>
    /// Get all approved documents with client info (Admin only)
    /// </summary>
    [HttpGet("all-approved")]
    [Authorize(Policy = "AdminOrStaff")]
    public async Task<IActionResult> GetAllApprovedDocuments([FromQuery] string? search = null, [FromQuery] int? clientId = null)
    {
        var firmId = GetFirmId();

        var query = _context.Documents
            .Include(d => d.Folder)
            .Include(d => d.Uploader)
            .Where(d => d.FirmID == firmId && 
                        d.Status == "Completed" &&
                        d.WorkflowStage != "Archived");

        // Filter by client
        if (clientId.HasValue)
        {
            query = query.Where(d => d.UploadedBy == clientId);
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(d => 
                (d.Title != null && d.Title.ToLower().Contains(search)) ||
                (d.OriginalFileName != null && d.OriginalFileName.ToLower().Contains(search)) ||
                (d.DocumentType != null && d.DocumentType.ToLower().Contains(search)) ||
                (d.Uploader != null && (
                    (d.Uploader.FirstName != null && d.Uploader.FirstName.ToLower().Contains(search)) ||
                    (d.Uploader.LastName != null && d.Uploader.LastName.ToLower().Contains(search)) ||
                    (d.Uploader.Email != null && d.Uploader.Email.ToLower().Contains(search))
                )));
        }

        var documents = await query
            .OrderByDescending(d => d.ApprovedAt ?? d.CreatedAt)
            .Take(200)
            .Select(d => new
            {
                id = d.DocumentID,
                title = d.Title,
                originalFileName = d.OriginalFileName,
                documentType = d.DocumentType,
                fileExtension = d.FileExtension,
                totalFileSize = d.TotalFileSize,
                status = d.Status,
                folderName = d.Folder != null ? d.Folder.FolderName : null,
                folderId = d.FolderId,
                clientId = d.UploadedBy,
                clientName = d.Uploader != null ? d.Uploader.FullName : null,
                clientEmail = d.Uploader != null ? d.Uploader.Email : null,
                uploadedAt = d.CreatedAt,
                approvedAt = d.ApprovedAt
            })
            .ToListAsync();

        return Ok(new { success = true, documents, totalCount = documents.Count });
    }

    /// <summary>
    /// Get all clients with folders (Staff only)
    /// </summary>
    [HttpGet("staff/clients")]
    [Authorize(Roles = "Staff,Admin,Auditor,Lawyer")]
    public async Task<IActionResult> GetStaffClientsWithFolders()
    {
        var firmId = GetFirmId();

        var clients = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId && u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Client"))
            .Select(u => new
            {
                id = u.UserID,
                name = u.FullName,
                email = u.Email
            })
            .OrderBy(u => u.name)
            .ToListAsync();

        return Ok(new { success = true, clients });
    }

    /// <summary>
    /// Get all folders with search and filter (Staff only - read only)
    /// </summary>
    [HttpGet("staff/all-folders")]
    [Authorize(Roles = "Staff,Admin,Auditor,Lawyer")]
    public async Task<IActionResult> GetAllFolders(
        [FromQuery] string? search = null,
        [FromQuery] int? clientId = null,
        [FromQuery] string sortBy = "name")
    {
        var firmId = GetFirmId();

        var query = _context.ClientFolders
            .Include(f => f.Client)
            .Include(f => f.ChildFolders)
            .Include(f => f.Documents.Where(d => d.WorkflowStage != "Archived"))
            .Where(f => f.FirmId == firmId && f.ParentFolderId == null); // Only root folders

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(f => 
                f.FolderName.ToLower().Contains(searchLower) ||
                (f.Description != null && f.Description.ToLower().Contains(searchLower)) ||
                (f.Client != null && f.Client.FirstName != null && f.Client.FirstName.ToLower().Contains(searchLower)) ||
                (f.Client != null && f.Client.LastName != null && f.Client.LastName.ToLower().Contains(searchLower)));
        }

        // Apply client filter
        if (clientId.HasValue)
        {
            query = query.Where(f => f.ClientId == clientId);
        }

        // Apply sorting
        query = sortBy switch
        {
            "recent" => query.OrderByDescending(f => f.CreatedAt),
            "documents" => query.OrderByDescending(f => f.Documents.Count),
            _ => query.OrderBy(f => f.FolderName)
        };

        var folders = await query
            .Select(f => new
            {
                id = f.FolderId,
                name = f.FolderName,
                description = f.Description,
                color = f.Color,
                clientId = f.ClientId,
                clientName = f.Client != null ? f.Client.FullName : "Unknown",
                documentCount = f.Documents.Count,
                childFolderCount = f.ChildFolders.Count,
                createdAt = f.CreatedAt
            })
            .ToListAsync();

        // Calculate statistics
        var totalDocuments = await _context.Documents
            .Where(d => d.FirmID == firmId && d.WorkflowStage != "Archived")
            .CountAsync();

        var pendingDocuments = await _context.Documents
            .Where(d => d.FirmID == firmId && d.Status == "Pending")
            .CountAsync();

        var totalClients = await _context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Where(u => u.FirmID == firmId && u.UserRoles.Any(ur => ur.Role != null && ur.Role.RoleName == "Client"))
            .CountAsync();

        var stats = new
        {
            totalFolders = folders.Count,
            totalClients = totalClients,
            totalDocuments = totalDocuments,
            pendingDocuments = pendingDocuments
        };

        return Ok(new { success = true, folders, stats });
    }

    private List<object> BuildFolderTree(List<ClientFolder> folders, int? parentId)
    {
        return folders
            .Where(f => f.ParentFolderId == parentId)
            .Select(f => new
            {
                id = f.FolderId,
                name = f.FolderName,
                color = f.Color,
                children = BuildFolderTree(folders, f.FolderId)
            })
            .Cast<object>()
            .ToList();
    }
}

// DTOs
public class CreateFolderDto
{
    public string FolderName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public int? ParentFolderId { get; set; }
}

public class UpdateFolderDto
{
    public string? FolderName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
}

public class MoveFolderDto
{
    public int? NewParentFolderId { get; set; }
}
