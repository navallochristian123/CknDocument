using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CKNDocument.Services;

/// <summary>
/// AI Service for document analysis
/// - Document type detection
/// - Duplicate detection
/// - Signature verification (placeholder for AI integration)
/// - Auto-generate checklist items
/// </summary>
public class DocumentAIService
{
    private readonly LawFirmDMSDbContext _context;
    private readonly ILogger<DocumentAIService> _logger;

    // Common document types that can be detected
    private readonly Dictionary<string, List<string>> _documentTypeKeywords = new()
    {
        ["Contract"] = new List<string> { "contract", "agreement", "terms and conditions", "parties agree", "binding agreement" },
        ["Invoice"] = new List<string> { "invoice", "bill", "amount due", "payment terms", "total amount" },
        ["Legal Brief"] = new List<string> { "legal brief", "court", "plaintiff", "defendant", "hereby" },
        ["Affidavit"] = new List<string> { "affidavit", "sworn statement", "oath", "notarized", "deponent" },
        ["Power of Attorney"] = new List<string> { "power of attorney", "attorney-in-fact", "principal", "hereby appoint" },
        ["Will"] = new List<string> { "last will", "testament", "bequeath", "executor", "beneficiary" },
        ["Deed"] = new List<string> { "deed", "property", "convey", "grantor", "grantee", "real estate" },
        ["Lease Agreement"] = new List<string> { "lease", "tenant", "landlord", "rent", "premises" },
        ["NDA"] = new List<string> { "non-disclosure", "confidential", "proprietary information", "trade secret" },
        ["Certificate"] = new List<string> { "certificate", "certify", "hereby certifies", "issued to" }
    };

    // Default checklist items per document type
    private readonly Dictionary<string, List<string>> _defaultChecklistItems = new()
    {
        ["Contract"] = new List<string>
        {
            "All parties are identified",
            "Signatures are present and valid",
            "Date is clearly stated",
            "Terms and conditions are clear",
            "Payment terms specified (if applicable)",
            "Duration/Term specified",
            "Termination clause present"
        },
        ["Invoice"] = new List<string>
        {
            "Invoice number is present",
            "Client details are correct",
            "Items/services are listed",
            "Amounts are accurate",
            "Payment due date specified"
        },
        ["Legal Brief"] = new List<string>
        {
            "Case number is correct",
            "Court information is accurate",
            "Legal arguments are clear",
            "Citations are properly formatted",
            "Filing deadline verified"
        },
        ["Default"] = new List<string>
        {
            "Document is legible",
            "All pages are present",
            "Required signatures present",
            "Dates are accurate",
            "Content is complete"
        }
    };

    public DocumentAIService(LawFirmDMSDbContext context, ILogger<DocumentAIService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Process document with AI analysis
    /// </summary>
    public async Task<DocumentAIResult> ProcessDocumentAsync(int documentId, Stream fileStream, string fileName)
    {
        var result = new DocumentAIResult
        {
            DocumentId = documentId,
            ProcessedAt = DateTime.UtcNow
        };

        try
        {
            // Read file content for analysis (for text-based files)
            string? textContent = null;
            var extension = Path.GetExtension(fileName).ToLower();

            if (extension == ".txt" || extension == ".csv")
            {
                using var reader = new StreamReader(fileStream, leaveOpen: true);
                textContent = await reader.ReadToEndAsync();
                fileStream.Position = 0; // Reset stream position
            }

            // Calculate file hash for duplicate detection
            result.FileHash = await CalculateFileHashAsync(fileStream);
            fileStream.Position = 0;

            // Detect document type
            result.DetectedDocumentType = DetectDocumentType(fileName, textContent);

            // Check for duplicates
            var duplicateCheck = await CheckForDuplicatesAsync(documentId, result.FileHash, 
                await GetFirmIdAsync(documentId));
            result.IsDuplicate = duplicateCheck.IsDuplicate;
            result.DuplicateOfDocumentId = duplicateCheck.DuplicateDocumentId;

            // Get suggested checklist items
            result.SuggestedChecklistItems = GetChecklistItemsForType(result.DetectedDocumentType);

            // Signature verification placeholder
            // In production, integrate with signature verification AI service
            result.SignatureVerificationStatus = "Pending Manual Verification";
            result.SignatureConfidenceScore = null;

            // Update document with AI results
            await UpdateDocumentWithAIResultsAsync(documentId, result);

            result.Success = true;
            _logger.LogInformation("AI processing completed for document {DocumentId}. Type: {Type}, Duplicate: {IsDuplicate}",
                documentId, result.DetectedDocumentType, result.IsDuplicate);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "AI processing failed for document {DocumentId}", documentId);
        }

        return result;
    }

    /// <summary>
    /// Detect document type based on filename and content
    /// </summary>
    public string DetectDocumentType(string fileName, string? textContent)
    {
        var fileNameLower = fileName.ToLower();

        // First try to detect from filename
        foreach (var (docType, keywords) in _documentTypeKeywords)
        {
            if (keywords.Any(k => fileNameLower.Contains(k.ToLower())))
            {
                return docType;
            }
        }

        // Then try to detect from content (if available)
        if (!string.IsNullOrEmpty(textContent))
        {
            var contentLower = textContent.ToLower();
            var maxMatches = 0;
            var detectedType = "Other";

            foreach (var (docType, keywords) in _documentTypeKeywords)
            {
                var matches = keywords.Count(k => contentLower.Contains(k.ToLower()));
                if (matches > maxMatches)
                {
                    maxMatches = matches;
                    detectedType = docType;
                }
            }

            if (maxMatches >= 2) // At least 2 keyword matches
            {
                return detectedType;
            }
        }

        // Default based on file extension
        var extension = Path.GetExtension(fileName).ToLower();
        return extension switch
        {
            ".pdf" => "PDF Document",
            ".doc" or ".docx" => "Word Document",
            ".xls" or ".xlsx" => "Spreadsheet",
            ".ppt" or ".pptx" => "Presentation",
            ".jpg" or ".jpeg" or ".png" or ".gif" => "Image",
            _ => "Other"
        };
    }

    /// <summary>
    /// Calculate SHA256 hash of file for duplicate detection
    /// </summary>
    public async Task<string> CalculateFileHashAsync(Stream fileStream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(fileStream);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Check if document is a duplicate
    /// </summary>
    public async Task<(bool IsDuplicate, int? DuplicateDocumentId)> CheckForDuplicatesAsync(
        int currentDocumentId, string fileHash, int firmId)
    {
        // Look for documents with same hash in the firm (excluding current document)
        var existingDoc = await _context.DocumentVersions
            .Include(v => v.Document)
            .Where(v => v.Document != null &&
                        v.Document.FirmID == firmId &&
                        v.Document.DocumentID != currentDocumentId &&
                        v.Document.WorkflowStage != "Archived")
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        // For now, using file size + filename as a basic duplicate check
        // In production, store hash in DocumentVersion table
        var currentDoc = await _context.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.DocumentID == currentDocumentId);

        if (currentDoc?.Versions.Any() == true)
        {
            var currentVersion = currentDoc.Versions.OrderByDescending(v => v.VersionNumber).First();

            var duplicate = await _context.Documents
                .Include(d => d.Versions)
                .Where(d => d.FirmID == firmId &&
                            d.DocumentID != currentDocumentId &&
                            d.OriginalFileName == currentDoc.OriginalFileName &&
                            d.TotalFileSize == currentDoc.TotalFileSize &&
                            d.WorkflowStage != "Archived")
                .FirstOrDefaultAsync();

            if (duplicate != null)
            {
                return (true, duplicate.DocumentID);
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Get checklist items for document type
    /// </summary>
    public List<string> GetChecklistItemsForType(string documentType)
    {
        if (_defaultChecklistItems.TryGetValue(documentType, out var items))
        {
            return items;
        }
        return _defaultChecklistItems["Default"];
    }

    /// <summary>
    /// Verify signature (placeholder for AI integration)
    /// </summary>
    public async Task<SignatureVerificationResult> VerifySignatureAsync(
        int documentId, Stream fileStream, string expectedSignerName)
    {
        // Placeholder for actual AI signature verification
        // In production, integrate with signature verification service like:
        // - Azure Form Recognizer
        // - AWS Textract
        // - Google Document AI
        // - Custom ML model

        await Task.Delay(100); // Simulate processing

        return new SignatureVerificationResult
        {
            DocumentId = documentId,
            IsVerified = null, // Requires manual verification
            ConfidenceScore = null,
            SignerNameDetected = null,
            VerificationStatus = "Pending Manual Review",
            Message = "Automatic signature verification requires AI service integration. Please verify manually."
        };
    }

    /// <summary>
    /// Create or update checklist items for a firm based on document type
    /// </summary>
    public async Task EnsureChecklistItemsExistAsync(int firmId, string documentType)
    {
        var existingItems = await _context.DocumentChecklistItems
            .Where(c => c.FirmId == firmId && c.DocumentType == documentType && c.IsActive == true)
            .Select(c => c.ItemName)
            .ToListAsync();

        var suggestedItems = GetChecklistItemsForType(documentType);
        var order = existingItems.Count;

        foreach (var itemName in suggestedItems)
        {
            if (!existingItems.Contains(itemName))
            {
                var checklistItem = new DocumentChecklistItem
                {
                    FirmId = firmId,
                    ItemName = itemName,
                    Description = $"Default checklist item for {documentType}",
                    DocumentType = documentType,
                    IsRequired = true,
                    DisplayOrder = order++,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.DocumentChecklistItems.Add(checklistItem);
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task<int> GetFirmIdAsync(int documentId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        return document?.FirmID ?? 0;
    }

    private async Task UpdateDocumentWithAIResultsAsync(int documentId, DocumentAIResult result)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document != null)
        {
            document.IsAIProcessed = true;
            document.DocumentType = result.DetectedDocumentType;
            document.IsDuplicate = result.IsDuplicate;
            document.DuplicateOfDocumentId = result.DuplicateOfDocumentId;
            document.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Analyze a document by ID and return AI analysis results
    /// Used for real-time document analysis in the UI
    /// </summary>
    public async Task<DocumentAnalysisResult> AnalyzeDocumentAsync(int documentId)
    {
        var result = new DocumentAnalysisResult();

        try
        {
            var document = await _context.Documents
                .Include(d => d.Uploader)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId);

            if (document == null)
            {
                result.Success = false;
                result.ErrorMessage = "Document not found";
                return result;
            }

            result.DocumentId = documentId;
            result.Success = true;
            result.DocumentType = document.DocumentType ?? "Unknown";
            result.Confidence = document.IsAIProcessed == true ? 85.0 : 60.0; // Simulated confidence
            result.IsConfidential = DetectConfidentiality(document);
            result.IsDuplicate = document.IsDuplicate ?? false;
            result.DuplicateOfDocumentId = document.DuplicateOfDocumentId;
            result.Keywords = ExtractKeywords(document);
            result.Issues = DetectIssues(document);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing document {DocumentId}", documentId);
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private bool DetectConfidentiality(Document document)
    {
        var confidentialIndicators = new[] { "confidential", "private", "restricted", "secret", "sensitive", "nda", "privileged" };
        var title = document.Title?.ToLower() ?? "";
        var description = document.Description?.ToLower() ?? "";
        var documentType = document.DocumentType?.ToLower() ?? "";

        return confidentialIndicators.Any(indicator =>
            title.Contains(indicator) || description.Contains(indicator) || documentType.Contains(indicator));
    }

    private List<string> ExtractKeywords(Document document)
    {
        var keywords = new List<string>();

        // Add document type as keyword
        if (!string.IsNullOrEmpty(document.DocumentType))
            keywords.Add(document.DocumentType);

        // Add category as keyword
        if (!string.IsNullOrEmpty(document.Category))
            keywords.Add(document.Category);

        // Extract words from title
        if (!string.IsNullOrEmpty(document.Title))
        {
            var titleWords = document.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .Take(5);
            keywords.AddRange(titleWords);
        }

        return keywords.Distinct().Take(10).ToList();
    }

    private List<DocumentIssue> DetectIssues(Document document)
    {
        var issues = new List<DocumentIssue>();

        // Check for missing description
        if (string.IsNullOrEmpty(document.Description))
        {
            issues.Add(new DocumentIssue
            {
                Type = "warning",
                Message = "Document is missing a description"
            });
        }

        // Check for missing document type
        if (string.IsNullOrEmpty(document.DocumentType))
        {
            issues.Add(new DocumentIssue
            {
                Type = "warning",
                Message = "Document type has not been classified"
            });
        }

        // Check for large file size (> 50MB)
        if (document.TotalFileSize > 50 * 1024 * 1024)
        {
            issues.Add(new DocumentIssue
            {
                Type = "info",
                Message = "Large file size may affect processing"
            });
        }

        // Check for duplicate
        if (document.IsDuplicate == true)
        {
            issues.Add(new DocumentIssue
            {
                Type = "error",
                Message = "This document may be a duplicate"
            });
        }

        // Check for unprocessed document
        if (document.IsAIProcessed != true)
        {
            issues.Add(new DocumentIssue
            {
                Type = "info",
                Message = "Document has not been fully processed by AI"
            });
        }

        return issues;
    }
}

/// <summary>
/// Result of document analysis
/// </summary>
public class DocumentAnalysisResult
{
    public int DocumentId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string DocumentType { get; set; } = "Unknown";
    public double Confidence { get; set; }
    public bool IsConfidential { get; set; }
    public bool IsDuplicate { get; set; }
    public int? DuplicateOfDocumentId { get; set; }
    public List<string> Keywords { get; set; } = new();
    public List<DocumentIssue> Issues { get; set; } = new();
}

/// <summary>
/// Represents an issue detected in a document
/// </summary>
public class DocumentIssue
{
    public string Type { get; set; } = "info"; // info, warning, error
    public string Message { get; set; } = "";
}

/// <summary>
/// Result of AI document processing
/// </summary>
public class DocumentAIResult
{
    public int DocumentId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string? DetectedDocumentType { get; set; }
    public string? FileHash { get; set; }
    public bool IsDuplicate { get; set; }
    public int? DuplicateOfDocumentId { get; set; }
    public string? SignatureVerificationStatus { get; set; }
    public double? SignatureConfidenceScore { get; set; }
    public List<string> SuggestedChecklistItems { get; set; } = new();
}

/// <summary>
/// Result of signature verification
/// </summary>
public class SignatureVerificationResult
{
    public int DocumentId { get; set; }
    public bool? IsVerified { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? SignerNameDetected { get; set; }
    public string? VerificationStatus { get; set; }
    public string? Message { get; set; }
}
