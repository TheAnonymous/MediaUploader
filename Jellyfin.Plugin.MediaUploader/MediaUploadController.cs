using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading; // Required for CancellationToken (used in commented out code)
using System.Threading.Tasks;
using Jellyfin.Data.Enums; // Required for BaseItemKind
using Jellyfin.Plugin.MediaUploader.Configuration; // Required for PluginConfiguration
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities; // Required for CollectionFolder
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying; // Required for InternalItemsQuery
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaUploader
{
    /// <summary>
    /// API Controller for handling media uploads.
    /// </summary>
    [ApiController]
    [Route("Plugins/MediaUploader")] // Base route for this controller
    public class MediaUploadController : ControllerBase
    {
        private readonly ILogger<MediaUploadController> _logger;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaUploadController"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="configurationManager">The server configuration manager instance.</param>
        /// <param name="fileSystem">The file system abstraction instance.</param>
        /// <param name="libraryManager">The library manager instance.</param>
        public MediaUploadController(
            ILogger<MediaUploadController> logger,
            IServerConfigurationManager configurationManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _configurationManager = configurationManager;
            _fileSystem = fileSystem;
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// Handles the file upload POST request.
        /// Accepts a single file via multipart/form-data with the field name "file".
        /// </summary>
        /// <param name="file">The uploaded file.</param>
        /// <returns>An IActionResult indicating the result of the upload operation.</returns>
        [HttpPost("Upload")] // Route: /Plugins/MediaUploader/Upload
        [RequestSizeLimit(10L * 1024 * 1024 * 1024)] // Explicit 10 GB total request limit
        [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
#pragma warning disable SA1404
        [SuppressMessage("Reliability", "CA2007:Aufruf von \"ConfigureAwait\" für erwarteten Task erwägen", Justification = "<Pending>")] // Matching high limit for multipart section (workaround)
#pragma warning restore SA1404
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            _logger.LogInformation("Media Uploader: UploadFile endpoint hit.");

            // --- 1. Get and Validate Configuration ---
            var configuredPath = Plugin.Instance?.Configuration.UploadPath;
            if (string.IsNullOrEmpty(configuredPath))
            {
                _logger.LogError("Media Uploader: Upload path is not configured in plugin settings!");
                return StatusCode(StatusCodes.Status500InternalServerError, "Upload path is not configured in plugin settings.");
            }

            // Use the configured path as the target directory
            var targetDirectory = configuredPath;
            _logger.LogInformation("Media Uploader: Using configured target directory: '{TargetDirectory}'", targetDirectory);

            try
            {
                // --- 2. Validate Input File ---
                if (file == null || file.Length == 0)
                {
                    _logger.LogWarning("Media Uploader: No file uploaded or file is empty.");
                    return BadRequest("No file uploaded or file is empty.");
                }

                _logger.LogInformation("Media Uploader: Received file '{FileName}' ({Length} bytes), type: '{ContentType}'", file.FileName, file.Length, file.ContentType);

                // --- 3. Prepare and Validate Target Path ---
                var originalFileName = Path.GetFileName(file.FileName); // Extract filename only
                var safeFileName = _fileSystem.GetValidFilename(originalFileName); // Sanitize filename
                var fullTargetPath = Path.Combine(targetDirectory, safeFileName);
                var fullTargetDirectory = Path.GetFullPath(targetDirectory);

                // Security Check: Ensure the resolved path is within the configured directory
                if (!Path.GetFullPath(fullTargetPath).StartsWith(fullTargetDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(
                        "Media Uploader: Invalid target path generated. Attempted Path: '{AttemptedPath}', Resolved Path: '{ResolvedPath}', Allowed Directory: '{AllowedDirectory}'",
                        fullTargetPath, // Log the potentially malicious path
                        Path.GetFullPath(fullTargetPath), // Log the resolved path
                        fullTargetDirectory);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Invalid target path.");
                }

                // --- 4. Save the File ---
                _logger.LogInformation("Media Uploader: Attempting to save file '{SafeFileName}' to '{FullTargetPath}'", safeFileName, fullTargetPath);
                try
                {
                    // Use async stream operations
                    await using (var fileStream = new FileStream(fullTargetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        // Copy the uploaded file's stream to the file stream asynchronously
                        await file.CopyToAsync(fileStream);
                    }
                }
                catch (Exception ex)
                {
                    // Log specific file saving errors
                    _logger.LogError(ex, "Media Uploader: Error saving file '{SafeFileName}' to '{FullTargetPath}'", safeFileName, fullTargetPath);
                    // Re-throw to be caught by the outer catch block, or return a specific error
                    return StatusCode(StatusCodes.Status500InternalServerError, $"Error saving file: {ex.Message}");
                }

                _logger.LogInformation("Media Uploader: File '{SafeFileName}' successfully saved to '{FullTargetPath}'.", safeFileName, fullTargetPath);

                // --- Optional: Trigger Library Scan ---
                // Requires ILibraryManager injection in constructor (already done)
                // Consider making this configurable
                /*
                try
                {
                    _logger.LogInformation("Media Uploader: Requesting library validation for path: {TargetDirectory}", targetDirectory);
                    // ValidateLibraryPath might trigger scans if the path is part of a library
                    _libraryManager.ValidateLibraryPath(targetDirectory); // Removed CancellationToken for simplicity, add if needed
                    _logger.LogInformation("Media Uploader: Library validation requested.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Media Uploader: Error requesting library validation for path {TargetDirectory}", targetDirectory);
                    // Don't fail the whole upload if scan trigger fails
                }
                */

                // --- 5. Return Success Response ---
                return Ok($"File {safeFileName} uploaded successfully.");
            }
            catch (IOException ioEx) // Handle specific IO errors during file operations
            {
                _logger.LogError(ioEx, "Media Uploader: IO Error during upload process: {ErrorMessage}", ioEx.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"IO Error: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException authEx) // Handle permission errors
            {
                _logger.LogError(authEx, "Media Uploader: Permission denied during upload process: {ErrorMessage}", authEx.Message);
                return StatusCode(StatusCodes.Status403Forbidden, $"Permission denied: {authEx.Message}");
            }
            catch (Exception ex) // Catch-all for other unexpected errors
            {
                _logger.LogError(ex, "Media Uploader: Unexpected error processing file upload: {ErrorMessage}", ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"Unexpected error uploading file: {ex.Message}");
            }
        }

        /// <summary>
        /// Serves the static HTML page for direct uploads.
        /// </summary>
        /// <returns>An HTML page as ContentResult.</returns>
        [HttpGet("Page")] // Route: /Plugins/MediaUploader/Page
        [Produces("text/html")]
        public IActionResult GetUploadPage()
        {
            _logger.LogInformation("Media Uploader: Serving static upload page request.");
            try
            {
                var assembly = typeof(MediaUploadController).Assembly;
                // Ensure this resource name exactly matches Namespace.Folder.FileName.ext
                var resourceName = "Jellyfin.Plugin.MediaUploader.Web.uploadPage.html";

                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    _logger.LogError("Media Uploader: Could not find embedded resource: {ResourceName}. Check file exists, path/namespace, and Build Action='Embedded resource'.", resourceName);
                    return NotFound($"Resource not found: {resourceName}");
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var htmlContent = reader.ReadToEnd();

                return Content(htmlContent, "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Media Uploader: Error serving static upload page");
                 return StatusCode(StatusCodes.Status500InternalServerError, "Error serving upload page");
            }
        }
    }
}
