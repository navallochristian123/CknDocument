using CKNDocument.Data;
using CKNDocument.Models.LawFirmDMS;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;

namespace CKNDocument.Services;

/// <summary>
/// AI Service for document analysis using OpenAI GPT
/// - Reads document content (PDF, DOCX, TXT, images, etc.)
/// - Calls OpenAI to detect document type
/// - Generates dynamic compliance checklist
/// - Identifies missing items and deficiencies
/// - Stores results for staff/admin review
/// </summary>
public class DocumentAIService
{
    private readonly LawFirmDMSDbContext _context;
    private readonly ILogger<DocumentAIService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

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
        ["Certificate"] = new List<string> { "certificate", "certify", "hereby certifies", "issued to" },
        ["Motion"] = new List<string> { "motion", "movant", "court order", "hearing", "relief" },
        ["Pleading"] = new List<string> { "pleading", "complaint", "answer", "counterclaim", "cross-claim" },
        ["Subpoena"] = new List<string> { "subpoena", "commanded", "appear", "testify", "produce" },
        ["Court Order"] = new List<string> { "ordered", "court order", "judgment", "decree", "ruling" }
    };

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
        ["Deed of Sale"] = new List<string>
        {
            "Seller information is complete",
            "Buyer information is complete",
            "Property description is accurate",
            "Purchase price is stated",
            "Payment terms are specified",
            "Transfer conditions are clear",
            "Signatures of all parties present",
            "Notarization is complete"
        },
        ["Affidavit"] = new List<string>
        {
            "Affiant name is clearly stated",
            "Purpose of affidavit is clear",
            "Facts are clearly stated",
            "Date of execution is present",
            "Affiant signature is present",
            "Notarization is completed",
            "Jurat is properly executed"
        },
        ["Power of Attorney"] = new List<string>
        {
            "Principal name is complete",
            "Agent/Attorney-in-fact is identified",
            "Powers granted are clearly specified",
            "Effective date is stated",
            "Expiration/revocation clause present",
            "Principal signature is present",
            "Witnesses signatures (if required)",
            "Notarization is complete"
        },
        ["Certificate"] = new List<string>
        {
            "Issuing authority is identified",
            "Certificate number/reference present",
            "Subject of certificate is clear",
            "Date of issuance is present",
            "Validity period is stated",
            "Official seal/stamp present",
            "Authorized signature present"
        },
        ["Agreement"] = new List<string>
        {
            "All parties are identified",
            "Purpose of agreement is clear",
            "Terms and conditions stated",
            "Rights and obligations defined",
            "Duration/term is specified",
            "Signatures of all parties present",
            "Date of execution is present"
        },
        ["Lease Agreement"] = new List<string>
        {
            "Lessor information is complete",
            "Lessee information is complete",
            "Property description is accurate",
            "Rental amount is specified",
            "Payment schedule is clear",
            "Lease term/duration stated",
            "Security deposit terms present",
            "Termination conditions specified",
            "Signatures of all parties present"
        },
        ["Court Filing"] = new List<string>
        {
            "Case number is correct",
            "Court and branch information",
            "Parties names are correct",
            "Caption is properly formatted",
            "Filing fee verification",
            "Signature of counsel present",
            "Service requirements noted"
        },
        ["Motion"] = new List<string>
        {
            "Case number is correct",
            "Motion title is clear",
            "Relief sought is specified",
            "Legal basis is provided",
            "Supporting facts are stated",
            "Prayer/request is clear",
            "Counsel signature present",
            "Notice of hearing attached"
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

    public DocumentAIService(
        LawFirmDMSDbContext context,
        ILogger<DocumentAIService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    #region Text Extraction

    public async Task<string> ExtractTextFromFileAsync(string filePath, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        try
        {
            return extension switch
            {
                ".pdf" => ExtractTextFromPdf(filePath),
                ".docx" => ExtractTextFromDocx(filePath),
                ".doc" => ExtractTextFromDocx(filePath),
                ".txt" or ".csv" or ".log" => await File.ReadAllTextAsync(filePath),
                ".xlsx" or ".xls" => ExtractTextFromExcel(filePath),
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => await ExtractTextFromImageAsync(filePath),
                _ => $"[File type {extension} - content extraction not supported. Filename: {fileName}]"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract text from {FileName}", fileName);
            return $"[Text extraction failed for {fileName}: {ex.Message}]";
        }
    }

    private string ExtractTextFromPdf(string filePath)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine($"--- Page {page.Number} ---");
                sb.AppendLine(text);
            }
        }

        var result = sb.ToString();
        if (string.IsNullOrWhiteSpace(result))
            return "[PDF document - no extractable text found. May be a scanned/image PDF.]";

        return result.Length > 8000 ? result[..8000] + "\n[... content truncated for analysis ...]" : result;
    }

    private string ExtractTextFromDocx(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null) return "[Empty Word document]";

        var text = body.InnerText;
        if (string.IsNullOrWhiteSpace(text))
            return "[Word document - no text content found]";

        return text.Length > 8000 ? text[..8000] + "\n[... content truncated for analysis ...]" : text;
    }

    private string ExtractTextFromExcel(string filePath)
    {
        var sb = new StringBuilder();
        using var doc = SpreadsheetDocument.Open(filePath, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart == null) return "[Empty Excel document]";

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            var sheet = worksheetPart.Worksheet;
            var rows = sheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>();
            foreach (var row in rows.Take(100))
            {
                var cells = row.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>();
                var cellTexts = new List<string>();
                foreach (var cell in cells)
                {
                    var value = cell.InnerText;
                    if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString
                        && sharedStrings != null && int.TryParse(value, out var index))
                    {
                        value = sharedStrings.ElementAt(index).InnerText;
                    }
                    cellTexts.Add(value);
                }
                sb.AppendLine(string.Join(" | ", cellTexts));
            }
        }

        var result = sb.ToString();
        return result.Length > 8000 ? result[..8000] + "\n[... content truncated ...]" : result;
    }

    private async Task<string> ExtractTextFromImageAsync(string filePath)
    {
        var apiKey = _configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return "[Image file - OpenAI API key not configured for OCR]";

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(filePath);
            var base64Image = Convert.ToBase64String(imageBytes);
            var extension = Path.GetExtension(filePath).ToLower();
            var mimeType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };

            var client = _httpClientFactory.CreateClient("OpenAI");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var request = new
            {
                model = "gpt-4o-mini",
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "Extract all text content from this document image. Return the raw text only, preserving layout." },
                            new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                        }
                    }
                },
                max_tokens = 2000
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseBody);
                var text = doc.RootElement.GetProperty("choices")[0]
                    .GetProperty("message").GetProperty("content").GetString();
                return text ?? "[No text extracted from image]";
            }

            return $"[Image - could not extract text: {response.StatusCode}]";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting text from image");
            return $"[Image - text extraction failed: {ex.Message}]";
        }
    }

    #endregion

    #region OpenAI Analysis

    public async Task<OpenAIAnalysisResult> AnalyzeWithOpenAIAsync(string textContent, string fileName, string fileExtension)
    {
        var apiKey = _configuration["OpenAI:ApiKey"];
        var model = _configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var maxTokens = int.TryParse(_configuration["OpenAI:MaxTokens"], out var mt) ? mt : 4000;

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("OpenAI API key not configured - falling back to basic analysis");
            return CreateFallbackAnalysis(textContent, fileName);
        }

        try
        {
            var client = _httpClientFactory.CreateClient("OpenAI");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var systemPrompt = @"You are an expert legal document analyst AI for a law firm document management system. 
Your task is to analyze uploaded documents and provide a comprehensive review.

You MUST respond with valid JSON in exactly this format:
{
  ""documentType"": ""the specific type of document (e.g., Contract, Affidavit, NDA, Invoice, Legal Brief, Power of Attorney, Will, Deed, Lease Agreement, Motion, Pleading, Court Order, Certificate, Memorandum, Report, etc.)"",
  ""confidence"": 85,
  ""summary"": ""A brief 2-3 sentence summary of the document content and purpose"",
  ""checklist"": [
    {""item"": ""checklist item description"", ""status"": ""pass"", ""detail"": ""reason or detail""},
    {""item"": ""another item"", ""status"": ""fail"", ""detail"": ""what is wrong or missing""},
    {""item"": ""another item"", ""status"": ""warning"", ""detail"": ""potential concern""}
  ],
  ""issues"": [
    {""severity"": ""high"", ""issue"": ""description of issue"", ""recommendation"": ""what to fix""},
    {""severity"": ""medium"", ""issue"": ""description"", ""recommendation"": ""suggestion""}
  ],
  ""missingItems"": [
    {""item"": ""missing item"", ""importance"": ""required"", ""detail"": ""why it is needed""},
    {""item"": ""another missing item"", ""importance"": ""recommended"", ""detail"": ""why it helps""}
  ]
}

For the CHECKLIST, always include these checks relevant to the document type:
- Document completeness (all pages, sections present)
- Proper formatting and structure
- Required signatures/notarization
- Dates and deadlines accuracy
- Party identification (names, addresses, roles)
- Legal language correctness
- Required clauses for the document type
- Compliance with standard requirements
- Any financial figures accuracy
- Supporting documents/attachments

Set status to:
- ""pass"" if the item appears properly present/satisfied
- ""fail"" if the item is clearly missing or incorrect
- ""warning"" if there is a potential concern needing human verification

For ISSUES, flag anything a legal staff member should pay special attention to.
For MISSING ITEMS, identify required documents or information that should be present but are not.";

            var userPrompt = $@"Analyze this document:
Filename: {fileName}
File Type: {fileExtension}

Document Content:
{textContent}

Provide your analysis as JSON.";

            var request = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = maxTokens,
                temperature = 0.3,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", httpContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API error: {Status} - {Body}", response.StatusCode, responseBody);
                return CreateFallbackAnalysis(textContent, fileName);
            }

            using var responseDoc = JsonDocument.Parse(responseBody);
            var root = responseDoc.RootElement;

            var aiContent = root.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString();

            var tokensUsed = root.TryGetProperty("usage", out var usage)
                ? usage.GetProperty("total_tokens").GetInt32() : 0;

            if (string.IsNullOrEmpty(aiContent))
                return CreateFallbackAnalysis(textContent, fileName);

            var analysisResult = ParseOpenAIResponse(aiContent);
            analysisResult.RawResponse = aiContent;
            analysisResult.ModelUsed = model;
            analysisResult.TokensUsed = tokensUsed;
            analysisResult.Success = true;

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API");
            return CreateFallbackAnalysis(textContent, fileName);
        }
    }

    private OpenAIAnalysisResult ParseOpenAIResponse(string jsonContent)
    {
        var result = new OpenAIAnalysisResult();
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            result.DocumentType = root.TryGetProperty("documentType", out var dt) ? dt.GetString() ?? "Unknown" : "Unknown";
            result.Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 70;
            result.Summary = root.TryGetProperty("summary", out var sum) ? sum.GetString() ?? "" : "";

            if (root.TryGetProperty("checklist", out var checklist) && checklist.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in checklist.EnumerateArray())
                {
                    result.Checklist.Add(new AIChecklistItem
                    {
                        Item = item.TryGetProperty("item", out var i) ? i.GetString() ?? "" : "",
                        Status = item.TryGetProperty("status", out var s) ? s.GetString() ?? "warning" : "warning",
                        Detail = item.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : ""
                    });
                }
            }

            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issues.EnumerateArray())
                {
                    result.Issues.Add(new AIDocumentIssue
                    {
                        Severity = item.TryGetProperty("severity", out var sv) ? sv.GetString() ?? "medium" : "medium",
                        Issue = item.TryGetProperty("issue", out var iss) ? iss.GetString() ?? "" : "",
                        Recommendation = item.TryGetProperty("recommendation", out var rec) ? rec.GetString() ?? "" : ""
                    });
                }
            }

            if (root.TryGetProperty("missingItems", out var missing) && missing.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in missing.EnumerateArray())
                {
                    result.MissingItems.Add(new AIMissingItem
                    {
                        Item = item.TryGetProperty("item", out var mi) ? mi.GetString() ?? "" : "",
                        Importance = item.TryGetProperty("importance", out var imp) ? imp.GetString() ?? "recommended" : "recommended",
                        Detail = item.TryGetProperty("detail", out var det) ? det.GetString() ?? "" : ""
                    });
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing OpenAI response JSON");
            result.Success = false;
            result.ErrorMessage = "Failed to parse AI response: " + ex.Message;
        }

        return result;
    }

    private OpenAIAnalysisResult CreateFallbackAnalysis(string? textContent, string fileName)
    {
        var detectedType = DetectDocumentType(fileName, textContent);
        var checklistItems = GetChecklistItemsForType(detectedType);

        var result = new OpenAIAnalysisResult
        {
            Success = true,
            DocumentType = detectedType,
            Confidence = 60,
            Summary = $"Basic analysis (AI unavailable): Document detected as {detectedType} based on filename and content keywords.",
            ModelUsed = "Fallback (keyword-based)",
            RawResponse = "{\"note\": \"Fallback analysis - OpenAI API unavailable\"}"
        };

        foreach (var item in checklistItems)
        {
            result.Checklist.Add(new AIChecklistItem
            {
                Item = item,
                Status = "warning",
                Detail = "Requires manual verification - AI analysis unavailable"
            });
        }

        result.Issues.Add(new AIDocumentIssue
        {
            Severity = "medium",
            Issue = "AI analysis could not be completed - manual review recommended",
            Recommendation = "Please review the document thoroughly as automated analysis was not available"
        });

        return result;
    }

    #endregion

    #region Main Processing

    public async Task<DocumentAIResult> ProcessDocumentAsync(int documentId, Stream fileStream, string fileName)
    {
        var result = new DocumentAIResult
        {
            DocumentId = documentId,
            ProcessedAt = DateTime.UtcNow
        };

        DocumentAIAnalysis? analysisRecord = null;

        try
        {
            result.FileHash = await CalculateFileHashAsync(fileStream);
            fileStream.Position = 0;

            var document = await _context.Documents.FindAsync(documentId);
            if (document == null)
            {
                result.Success = false;
                result.ErrorMessage = "Document not found";
                return result;
            }

            var firmId = document.FirmID;

            var duplicateCheck = await CheckForDuplicatesAsync(documentId, result.FileHash, firmId);
            result.IsDuplicate = duplicateCheck.IsDuplicate;
            result.DuplicateOfDocumentId = duplicateCheck.DuplicateDocumentId;

            var version = await _context.DocumentVersions
                .Where(v => v.DocumentId == documentId && v.IsCurrentVersion == true)
                .FirstOrDefaultAsync();

            string extractedText = "";
            if (version?.FilePath != null && File.Exists(version.FilePath))
            {
                extractedText = await ExtractTextFromFileAsync(version.FilePath, fileName);
            }
            else
            {
                var extension = Path.GetExtension(fileName).ToLower();
                if (extension == ".txt" || extension == ".csv")
                {
                    using var reader = new StreamReader(fileStream, leaveOpen: true);
                    extractedText = await reader.ReadToEndAsync();
                    fileStream.Position = 0;
                }
            }

            var aiAnalysis = await AnalyzeWithOpenAIAsync(extractedText, fileName, Path.GetExtension(fileName));

            result.DetectedDocumentType = aiAnalysis.DocumentType;
            result.SuggestedChecklistItems = aiAnalysis.Checklist.Select(c => c.Item).ToList();

            // Try to save AI analysis to database (may fail if table doesn't exist yet)
            try
            {
                analysisRecord = new DocumentAIAnalysis
                {
                    DocumentId = documentId,
                    FirmId = firmId,
                    DetectedDocumentType = aiAnalysis.DocumentType,
                    Confidence = aiAnalysis.Confidence,
                    Summary = aiAnalysis.Summary,
                    ChecklistJson = JsonSerializer.Serialize(aiAnalysis.Checklist),
                    IssuesJson = JsonSerializer.Serialize(aiAnalysis.Issues),
                    MissingItemsJson = JsonSerializer.Serialize(aiAnalysis.MissingItems),
                    RawResponseJson = aiAnalysis.RawResponse,
                    ExtractedText = extractedText.Length > 10000 ? extractedText[..10000] : extractedText,
                    IsProcessed = aiAnalysis.Success,
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = aiAnalysis.ErrorMessage,
                    ModelUsed = aiAnalysis.ModelUsed,
                    TokensUsed = aiAnalysis.TokensUsed,
                    CreatedAt = DateTime.UtcNow
                };

                _context.DocumentAIAnalyses.Add(analysisRecord);

                document.IsAIProcessed = aiAnalysis.Success;
                document.DocumentType = aiAnalysis.DocumentType;
                document.IsDuplicate = result.IsDuplicate;
                document.DuplicateOfDocumentId = result.DuplicateOfDocumentId;
                document.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await CreateAIChecklistItemsAsync(firmId, documentId, aiAnalysis);
            }
            catch (Exception dbEx)
            {
                _logger.LogWarning(dbEx, "Failed to save AI analysis to database for document {DocumentId}. Table may not exist yet.", documentId);

                // Detach the failed analysis entity to prevent poisoning subsequent SaveChangesAsync calls
                if (analysisRecord != null)
                {
                    _context.Entry(analysisRecord).State = EntityState.Detached;
                }

                // Reload document to reset any unsaved modifications
                var docEntry = _context.Entry(document);
                if (docEntry.State == EntityState.Modified)
                {
                    await docEntry.ReloadAsync();
                }

                // Still update basic document fields without AI analysis table dependency
                try
                {
                    document.DocumentType = aiAnalysis.DocumentType;
                    document.IsDuplicate = result.IsDuplicate;
                    document.DuplicateOfDocumentId = result.DuplicateOfDocumentId;
                    document.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
                catch (Exception innerEx)
                {
                    _logger.LogWarning(innerEx, "Failed to update document fields after AI analysis DB failure");
                }
            }

            result.SignatureVerificationStatus = "Pending Manual Verification";
            result.SignatureConfidenceScore = null;

            result.Success = true;
            _logger.LogInformation(
                "AI processing completed for document {DocumentId}. Type: {Type}, Confidence: {Conf}%, Duplicate: {IsDuplicate}",
                documentId, aiAnalysis.DocumentType, aiAnalysis.Confidence, result.IsDuplicate);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "AI processing failed for document {DocumentId}", documentId);

            // Clean up any tracked entities in Added state to prevent poisoning subsequent SaveChangesAsync
            foreach (var entry in _context.ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        return result;
    }

    private async Task CreateAIChecklistItemsAsync(int firmId, int documentId, OpenAIAnalysisResult aiAnalysis)
    {
        try
        {
            var docType = aiAnalysis.DocumentType ?? "Other";
            var existingItems = await _context.DocumentChecklistItems
                .Where(c => c.FirmId == firmId && c.DocumentType == docType && c.IsActive == true)
                .Select(c => c.ItemName)
                .ToListAsync();

            var order = existingItems.Count;

            foreach (var checkItem in aiAnalysis.Checklist)
            {
                if (!existingItems.Contains(checkItem.Item))
                {
                    var checklistItem = new DocumentChecklistItem
                    {
                        FirmId = firmId,
                        ItemName = checkItem.Item,
                        Description = checkItem.Detail,
                        DocumentType = docType,
                        IsRequired = checkItem.Status == "fail",
                        DisplayOrder = order++,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.DocumentChecklistItems.Add(checklistItem);
                    existingItems.Add(checkItem.Item);
                }
            }

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creating AI checklist items for document {DocumentId}", documentId);
        }
    }

    #endregion

    #region Analysis Retrieval

    public async Task<DocumentAIAnalysis?> GetAnalysisAsync(int documentId)
    {
        return await _context.DocumentAIAnalyses
            .Where(a => a.DocumentId == documentId)
            .OrderByDescending(a => a.ProcessedAt)
            .FirstOrDefaultAsync();
    }

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

            var aiAnalysis = await GetAnalysisAsync(documentId);

            result.DocumentId = documentId;
            result.Success = true;

            if (aiAnalysis != null && aiAnalysis.IsProcessed)
            {
                result.DocumentType = aiAnalysis.DetectedDocumentType ?? "Unknown";
                result.Confidence = aiAnalysis.Confidence ?? 70;
                result.Summary = aiAnalysis.Summary;

                if (!string.IsNullOrEmpty(aiAnalysis.ChecklistJson))
                    result.AIChecklist = JsonSerializer.Deserialize<List<AIChecklistItem>>(aiAnalysis.ChecklistJson) ?? new();
                if (!string.IsNullOrEmpty(aiAnalysis.IssuesJson))
                    result.AIIssues = JsonSerializer.Deserialize<List<AIDocumentIssue>>(aiAnalysis.IssuesJson) ?? new();
                if (!string.IsNullOrEmpty(aiAnalysis.MissingItemsJson))
                    result.AIMissingItems = JsonSerializer.Deserialize<List<AIMissingItem>>(aiAnalysis.MissingItemsJson) ?? new();
            }
            else
            {
                result.DocumentType = document.DocumentType ?? "Unknown";
                result.Confidence = document.IsAIProcessed == true ? 85.0 : 60.0;
            }

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

    #endregion

    #region Helper Methods

    public string DetectDocumentType(string fileName, string? textContent)
    {
        var fileNameLower = fileName.ToLower();
        foreach (var (docType, keywords) in _documentTypeKeywords)
        {
            if (keywords.Any(k => fileNameLower.Contains(k.ToLower())))
                return docType;
        }

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

            if (maxMatches >= 2) return detectedType;
        }

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

    public async Task<string> CalculateFileHashAsync(Stream fileStream)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(fileStream);
        return Convert.ToHexString(hashBytes);
    }

    public async Task<(bool IsDuplicate, int? DuplicateDocumentId)> CheckForDuplicatesAsync(
        int currentDocumentId, string fileHash, int firmId)
    {
        var currentDoc = await _context.Documents
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.DocumentID == currentDocumentId);

        if (currentDoc?.Versions.Any() == true)
        {
            var duplicate = await _context.Documents
                .Include(d => d.Versions)
                .Where(d => d.FirmID == firmId &&
                            d.DocumentID != currentDocumentId &&
                            d.OriginalFileName == currentDoc.OriginalFileName &&
                            d.TotalFileSize == currentDoc.TotalFileSize &&
                            d.WorkflowStage != "Archived")
                .FirstOrDefaultAsync();

            if (duplicate != null) return (true, duplicate.DocumentID);
        }

        return (false, null);
    }

    public List<string> GetChecklistItemsForType(string documentType)
    {
        if (_defaultChecklistItems.TryGetValue(documentType, out var items)) return items;
        return _defaultChecklistItems["Default"];
    }

    /// <summary>
    /// Verifies signature in a document using OpenAI Vision API
    /// Compares detected signature with client's stored signature
    /// </summary>
    public async Task<SignatureVerificationResult> VerifySignatureAsync(
        int documentId, Stream fileStream, string expectedSignerName)
    {
        var result = new SignatureVerificationResult
        {
            DocumentId = documentId,
            IsVerified = null,
            ConfidenceScore = null,
            SignerNameDetected = null,
            VerificationStatus = "Pending",
            Message = "Processing..."
        };

        try
        {
            var apiKey = _configuration["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                result.VerificationStatus = "Pending Manual Review";
                result.Message = "AI signature verification not available - API key not configured.";
                return result;
            }

            // Get document and uploader info
            var document = await _context.Documents
                .Include(d => d.Uploader)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId);

            if (document == null)
            {
                result.VerificationStatus = "Error";
                result.Message = "Document not found";
                return result;
            }

            // Get client's stored signature
            var clientSignaturePath = document.Uploader?.SignaturePath;
            var clientSignatureName = document.Uploader?.SignatureName;

            // Read file bytes for Vision API
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await fileStream.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }

            // Check if it's an image-based document (can use Vision API)
            var extension = (document.FileExtension ?? "").ToLower();
            var isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" }.Contains(extension);
            var isPdf = extension == ".pdf";

            if (!isImage && !isPdf)
            {
                // For non-image documents, check text content for signature patterns
                return await VerifySignatureFromTextAsync(documentId, expectedSignerName);
            }

            // Call OpenAI Vision API for signature detection
            var client = _httpClientFactory.CreateClient("OpenAI");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var base64Image = Convert.ToBase64String(fileBytes);
            var mimeType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "image/png"
            };

            var signaturePrompt = $@"Analyze this document image and look for signatures.

Expected signer name: {expectedSignerName}
{(clientSignatureName != null ? $"Client's registered name for signature: {clientSignatureName}" : "")}

Please analyze and respond ONLY with valid JSON in this exact format:
{{
  ""hasSignature"": true,
  ""signatureCount"": 1,
  ""signatures"": [
    {{
      ""location"": ""bottom right"",
      ""appearsToBeHandwritten"": true,
      ""nameReadable"": true,
      ""detectedName"": ""the name if readable"",
      ""matchesExpectedName"": true,
      ""confidenceScore"": 85
    }}
  ],
  ""datePresent"": true,
  ""dateValue"": ""date if visible"",
  ""overallConfidence"": 85,
  ""recommendation"": ""Signature appears valid"" or ""Needs manual review"" or ""Signature missing""
}}

Look for:
1. Handwritten signatures (cursive, initials, full name)
2. Digital/typed signatures
3. Date near signature
4. Whether the signature matches the expected signer name
5. Any indication of forgery or inconsistency";

            object requestContent;
            
            if (isImage)
            {
                requestContent = new
                {
                    model = "gpt-4o-mini",
                    messages = new object[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = signaturePrompt },
                                new
                                {
                                    type = "image_url",
                                    image_url = new { url = $"data:{mimeType};base64,{base64Image}" }
                                }
                            }
                        }
                    },
                    max_tokens = 1000,
                    response_format = new { type = "json_object" }
                };
            }
            else
            {
                // For PDF, we need to use text extraction + simple analysis
                return await VerifySignatureFromTextAsync(documentId, expectedSignerName);
            }

            var jsonRequest = JsonSerializer.Serialize(requestContent);
            var httpContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", httpContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI Vision API error for signature detection: {Status} - {Body}", response.StatusCode, responseBody);
                result.VerificationStatus = "Pending Manual Review";
                result.Message = "AI analysis failed - please verify signature manually.";
                return result;
            }

            using var responseDoc = JsonDocument.Parse(responseBody);
            var aiContent = responseDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(aiContent))
            {
                result.VerificationStatus = "Pending Manual Review";
                result.Message = "Could not analyze signature.";
                return result;
            }

            // Parse AI response
            using var analysisDoc = JsonDocument.Parse(aiContent);
            var analysis = analysisDoc.RootElement;

            var hasSignature = analysis.TryGetProperty("hasSignature", out var hasSig) && hasSig.GetBoolean();
            var overallConfidence = analysis.TryGetProperty("overallConfidence", out var conf) ? conf.GetDouble() : 0;
            var recommendation = analysis.TryGetProperty("recommendation", out var rec) ? rec.GetString() : null;
            string? detectedName = null;
            var matchesExpected = false;

            if (analysis.TryGetProperty("signatures", out var sigs) && sigs.GetArrayLength() > 0)
            {
                var firstSig = sigs[0];
                detectedName = firstSig.TryGetProperty("detectedName", out var dn) ? dn.GetString() : null;
                matchesExpected = firstSig.TryGetProperty("matchesExpectedName", out var me) && me.GetBoolean();
            }

            result.IsVerified = hasSignature && matchesExpected && overallConfidence > 70;
            result.ConfidenceScore = overallConfidence;
            result.SignerNameDetected = detectedName;
            
            if (!hasSignature)
            {
                result.VerificationStatus = "NoSignature";
                result.Message = "No signature was detected in this document.";
            }
            else if (matchesExpected && overallConfidence >= 80)
            {
                result.VerificationStatus = "Verified";
                result.Message = $"Signature verified with {overallConfidence}% confidence.";
            }
            else if (matchesExpected && overallConfidence >= 60)
            {
                result.VerificationStatus = "LikelyValid";
                result.Message = $"Signature likely valid ({overallConfidence}% confidence). Manual verification recommended.";
            }
            else if (hasSignature && !matchesExpected)
            {
                result.VerificationStatus = "NameMismatch";
                result.Message = $"Signature detected but name does not match expected. Detected: {detectedName ?? "unreadable"}";
            }
            else
            {
                result.VerificationStatus = "Pending Manual Review";
                result.Message = recommendation ?? "Please verify signature manually.";
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signature for document {DocumentId}", documentId);
            result.VerificationStatus = "Error";
            result.Message = "An error occurred during signature verification.";
            return result;
        }
    }

    /// <summary>
    /// Verify signature using text-based analysis (for documents where Vision API can't be used)
    /// </summary>
    private async Task<SignatureVerificationResult> VerifySignatureFromTextAsync(int documentId, string expectedSignerName)
    {
        var result = new SignatureVerificationResult
        {
            DocumentId = documentId,
            VerificationStatus = "Pending Manual Review"
        };

        try
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d => d.DocumentID == documentId);

            if (document == null)
            {
                result.Message = "Document not found";
                return result;
            }

            // Get the current version's file
            var version = await _context.DocumentVersions
                .Where(v => v.DocumentId == documentId && v.IsCurrentVersion == true)
                .FirstOrDefaultAsync();

            if (version == null || string.IsNullOrEmpty(version.FilePath))
            {
                result.Message = "Document file not found";
                return result;
            }

            // Extract text from document
            var fileName = version.OriginalFileName ?? Path.GetFileName(version.FilePath);
            var textContent = await ExtractTextFromFileAsync(version.FilePath, fileName);

            if (string.IsNullOrEmpty(textContent))
            {
                result.Message = "Could not extract text from document for signature analysis.";
                return result;
            }

            // Simple heuristic: look for signature indicators in text
            var textLower = textContent.ToLower();
            var signatureIndicators = new List<string> { "signature", "signed by", "signed:", "___________", "electronically signed", "digital signature" };
            var hasSignatureSection = signatureIndicators.Any(s => textLower.Contains(s));

            // Look for the expected name near signature sections
            var nameFound = !string.IsNullOrEmpty(expectedSignerName) && 
                            textLower.Contains(expectedSignerName.ToLower());

            if (hasSignatureSection && nameFound)
            {
                result.IsVerified = true;
                result.ConfidenceScore = 65;
                result.SignerNameDetected = expectedSignerName;
                result.VerificationStatus = "LikelyValid";
                result.Message = "Signature section found with expected name present. Manual verification recommended.";
            }
            else if (hasSignatureSection)
            {
                result.IsVerified = null;
                result.ConfidenceScore = 40;
                result.VerificationStatus = "Pending Manual Review";
                result.Message = "Signature section found but could not verify signer name.";
            }
            else
            {
                result.IsVerified = false;
                result.ConfidenceScore = 20;
                result.VerificationStatus = "NoSignature";
                result.Message = "No signature section detected in document text.";
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in text-based signature verification for document {DocumentId}", documentId);
            result.Message = "Error during text-based signature analysis.";
            return result;
        }
    }

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
                _context.DocumentChecklistItems.Add(new DocumentChecklistItem
                {
                    FirmId = firmId,
                    ItemName = itemName,
                    Description = $"Default checklist item for {documentType}",
                    DocumentType = documentType,
                    IsRequired = true,
                    DisplayOrder = order++,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
    }

    private bool DetectConfidentiality(Document document)
    {
        var indicators = new[] { "confidential", "private", "restricted", "secret", "sensitive", "nda", "privileged" };
        var title = document.Title?.ToLower() ?? "";
        var description = document.Description?.ToLower() ?? "";
        var documentType = document.DocumentType?.ToLower() ?? "";
        return indicators.Any(i => title.Contains(i) || description.Contains(i) || documentType.Contains(i));
    }

    private List<string> ExtractKeywords(Document document)
    {
        var keywords = new List<string>();
        if (!string.IsNullOrEmpty(document.DocumentType)) keywords.Add(document.DocumentType);
        if (!string.IsNullOrEmpty(document.Category)) keywords.Add(document.Category);
        if (!string.IsNullOrEmpty(document.Title))
        {
            keywords.AddRange(document.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3).Take(5));
        }
        return keywords.Distinct().Take(10).ToList();
    }

    private List<DocumentIssue> DetectIssues(Document document)
    {
        var issues = new List<DocumentIssue>();
        if (string.IsNullOrEmpty(document.Description))
            issues.Add(new DocumentIssue { Type = "warning", Message = "Document is missing a description" });
        if (string.IsNullOrEmpty(document.DocumentType))
            issues.Add(new DocumentIssue { Type = "warning", Message = "Document type has not been classified" });
        if (document.TotalFileSize > 50 * 1024 * 1024)
            issues.Add(new DocumentIssue { Type = "info", Message = "Large file size may affect processing" });
        if (document.IsDuplicate == true)
            issues.Add(new DocumentIssue { Type = "error", Message = "This document may be a duplicate" });
        if (document.IsAIProcessed != true)
            issues.Add(new DocumentIssue { Type = "info", Message = "Document has not been fully processed by AI" });
        return issues;
    }

    #endregion
}

#region DTOs

public class OpenAIAnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string DocumentType { get; set; } = "Unknown";
    public double Confidence { get; set; } = 70;
    public string? Summary { get; set; }
    public List<AIChecklistItem> Checklist { get; set; } = new();
    public List<AIDocumentIssue> Issues { get; set; } = new();
    public List<AIMissingItem> MissingItems { get; set; } = new();
    public string? RawResponse { get; set; }
    public string? ModelUsed { get; set; }
    public int TokensUsed { get; set; }
}

public class AIChecklistItem
{
    [JsonPropertyName("item")]
    public string Item { get; set; } = "";
    [JsonPropertyName("status")]
    public string Status { get; set; } = "warning";
    [JsonPropertyName("detail")]
    public string Detail { get; set; } = "";
}

public class AIDocumentIssue
{
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "medium";
    [JsonPropertyName("issue")]
    public string Issue { get; set; } = "";
    [JsonPropertyName("recommendation")]
    public string Recommendation { get; set; } = "";
}

public class AIMissingItem
{
    [JsonPropertyName("item")]
    public string Item { get; set; } = "";
    [JsonPropertyName("importance")]
    public string Importance { get; set; } = "recommended";
    [JsonPropertyName("detail")]
    public string Detail { get; set; } = "";
}

public class DocumentAnalysisResult
{
    public int DocumentId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string DocumentType { get; set; } = "Unknown";
    public double Confidence { get; set; }
    public string? Summary { get; set; }
    public bool IsConfidential { get; set; }
    public bool IsDuplicate { get; set; }
    public int? DuplicateOfDocumentId { get; set; }
    public List<string> Keywords { get; set; } = new();
    public List<DocumentIssue> Issues { get; set; } = new();
    public List<AIChecklistItem> AIChecklist { get; set; } = new();
    public List<AIDocumentIssue> AIIssues { get; set; } = new();
    public List<AIMissingItem> AIMissingItems { get; set; } = new();
}

public class DocumentIssue
{
    public string Type { get; set; } = "info";
    public string Message { get; set; } = "";
}

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

public class SignatureVerificationResult
{
    public int DocumentId { get; set; }
    public bool? IsVerified { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? SignerNameDetected { get; set; }
    public string? VerificationStatus { get; set; }
    public string? Message { get; set; }
}

#endregion
