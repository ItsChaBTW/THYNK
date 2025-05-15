using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using THYNK.Models;
using WkHtmlToPdfDotNet;
using WkHtmlToPdfDotNet.Contracts;

namespace THYNK.Services
{
    public class PdfService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<PdfService> _logger;
        private readonly IConverter _converter;

        public PdfService(IWebHostEnvironment webHostEnvironment, ILogger<PdfService> logger, IConverter converter)
        {
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
            _converter = converter;
        }

        public byte[] GenerateResourcePdf(EducationalResource resource)
        {
            try
            {
                string htmlContent = GenerateHtmlContent(resource);
                
                var doc = new HtmlToPdfDocument()
                {
                    GlobalSettings = {
                        ColorMode = ColorMode.Color,
                        Orientation = Orientation.Portrait,
                        PaperSize = PaperKind.A4,
                        Margins = new MarginSettings { Top = 10, Bottom = 10, Left = 10, Right = 10 },
                        DocumentTitle = resource.Title,
                    },
                    Objects = {
                        new ObjectSettings {
                            PagesCount = true,
                            HtmlContent = htmlContent,
                            WebSettings = { DefaultEncoding = "utf-8" },
                            HeaderSettings = { FontName = "Arial", FontSize = 9, Right = "Page [page] of [toPage]", Line = true },
                            FooterSettings = { FontName = "Arial", FontSize = 9, Center = "THYNK Educational Resource", Line = true }
                        }
                    }
                };

                return _converter.Convert(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating PDF for resource {ResourceId}: {ErrorMessage}", resource.Id, ex.Message);
                throw;
            }
        }

        private string GenerateHtmlContent(EducationalResource resource)
        {
            // Start with the HTML template
            string html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>{resource.Title}</title>
    <style>
        body {{ 
            font-family: Arial, sans-serif; 
            line-height: 1.6; 
            color: #333; 
            margin: 0; 
            padding: 20px; 
        }}
        h1 {{ 
            color: #2a5885; 
            border-bottom: 1px solid #ddd; 
            padding-bottom: 10px; 
            font-size: 24px; 
        }}
        h2 {{ 
            color: #3a5795; 
            margin-top: 20px; 
            font-size: 20px; 
        }}
        .resource-type {{
            display: inline-block;
            background-color: #f0f0f0;
            color: #555;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 14px;
            margin-bottom: 15px;
        }}
        .metadata {{
            background-color: #f9f9f9;
            border-left: 4px solid #ddd;
            padding: 10px 15px;
            margin-bottom: 20px;
        }}
        .metadata p {{
            margin: 5px 0;
            font-size: 14px;
        }}
        .content {{
            margin-top: 20px;
        }}
        img {{
            max-width: 100%;
            height: auto;
            margin: 10px 0;
        }}
        .resource-image {{
            text-align: center;
            margin: 20px 0;
        }}
        .tags {{
            margin-top: 30px;
            border-top: 1px solid #eee;
            padding-top: 10px;
        }}
        .tag {{
            display: inline-block;
            background-color: #e0e0e0;
            padding: 3px 8px;
            margin-right: 5px;
            border-radius: 3px;
            font-size: 12px;
        }}
    </style>
</head>
<body>
    <h1>{resource.Title}</h1>
    <div class='resource-type'>{resource.Type}</div>
    
    <div class='metadata'>
        <p><strong>Added:</strong> {resource.DateAdded.ToString("MMMM dd, yyyy")}</p>
        <p><strong>Created by:</strong> {(resource.CreatedBy != null ? $"{resource.CreatedBy.FirstName} {resource.CreatedBy.LastName}, {resource.CreatedBy.OrganizationName}" : "Unknown")}</p>
        <p><strong>Offline Access:</strong> {(resource.IsOfflineAccessible ? "Yes" : "No")}</p>
    </div>
    
    <h2>Description</h2>
    <p>{resource.Description}</p>";

            // Add the image if it exists
            if (!string.IsNullOrEmpty(resource.FileUrl) && resource.FileUrl != string.Empty)
            {
                // Get image file extension
                string fileExt = Path.GetExtension(resource.FileUrl).ToLower();
                bool isImage = fileExt == ".jpg" || fileExt == ".jpeg" || fileExt == ".png" || fileExt == ".gif";

                if (isImage)
                {
                    // Convert the relative path to an absolute file system path
                    string webRootPath = _webHostEnvironment.WebRootPath;
                    string fileRelativePath = resource.FileUrl.TrimStart('/');
                    string filePath = Path.Combine(webRootPath, fileRelativePath);

                    // Check if the file exists
                    if (File.Exists(filePath))
                    {
                        // Convert the image to base64 for inline embedding
                        byte[] fileBytes = File.ReadAllBytes(filePath);
                        string base64Image = Convert.ToBase64String(fileBytes);
                        string mimeType = GetMimeType(fileExt);

                        html += $@"
    <div class='resource-image'>
        <img src='data:{mimeType};base64,{base64Image}' alt='Resource Image' />
    </div>";
                    }
                }
                else
                {
                    // For non-image files, just mention the attachment
                    string fileName = Path.GetFileName(resource.FileUrl);
                    html += $@"
    <p><strong>Attached File:</strong> {fileName} ({resource.FileSizeKB} KB)</p>";
                }
            }

            // Add the content section
            html += $@"
    <h2>Content</h2>
    <div class='content'>
        {resource.Content}
    </div>";

            // Add external URL if it exists
            if (!string.IsNullOrEmpty(resource.ExternalUrl) && resource.ExternalUrl != string.Empty)
            {
                html += $@"
    <p><strong>External Resource:</strong> <a href='{resource.ExternalUrl}'>{resource.ExternalUrl}</a></p>";
            }

            // Add tags if they exist
            if (!string.IsNullOrEmpty(resource.Tags) && resource.Tags != string.Empty)
            {
                html += $@"
    <div class='tags'>
        <p><strong>Tags:</strong></p>";

                foreach (var tag in resource.Tags.Split(','))
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        html += $@"
        <span class='tag'>{tag.Trim()}</span>";
                    }
                }

                html += @"
    </div>";
            }

            // Close the HTML document
            html += @"
</body>
</html>";

            return html;
        }

        private string GetMimeType(string extension)
        {
            return extension.ToLower() switch
            {
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
        }
    }
} 