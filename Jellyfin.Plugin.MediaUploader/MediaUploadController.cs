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

            if (!ValidateConfiguration(out var targetDirectory))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Upload path is not configured in plugin settings.");
            }

            if (!ValidateFile(file))
            {
                return BadRequest("No file uploaded or file is empty.");
            }

            if (!PrepareAndValidateTargetPath(targetDirectory, file.FileName, out var fullTargetPath))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Invalid target path.");
            }

            var safeFileName = Path.GetFileName(fullTargetPath); // Get the sanitized filename for logging and response

            try
            {
                if (!await SaveFileAsync(file, fullTargetPath).ConfigureAwait(false))
                {
                    // Specific error already logged in SaveFileAsync
                    return StatusCode(StatusCodes.Status500InternalServerError, $"Error saving file {safeFileName}.");
                }

                _logger.LogInformation("Media Uploader: File '{SafeFileName}' successfully saved to '{FullTargetPath}'.", safeFileName, fullTargetPath);

                // Optional: Trigger Library Scan (Consider making this configurable or a separate endpoint)
                /*
                try
                {
                    _logger.LogInformation("Media Uploader: Requesting library validation for path: {TargetDirectory}", targetDirectory);
                    _libraryManager.ValidateLibraryPath(targetDirectory);
                    _logger.LogInformation("Media Uploader: Library validation requested.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Media Uploader: Error requesting library validation for path {TargetDirectory}", targetDirectory);
                    // Don't fail the whole upload if scan trigger fails
                }
                */

                return Ok($"File {safeFileName} uploaded successfully.");
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "Media Uploader: IO Error during upload process for file '{SafeFileName}': {ErrorMessage}", safeFileName, ioEx.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"IO Error: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException authEx)
            {
                _logger.LogError(authEx, "Media Uploader: Permission denied during upload process for file '{SafeFileName}': {ErrorMessage}", safeFileName, authEx.Message);
                return StatusCode(StatusCodes.Status403Forbidden, $"Permission denied: {authEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media Uploader: Unexpected error processing file upload for '{SafeFileName}': {ErrorMessage}", safeFileName, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"Unexpected error uploading file: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates the plugin configuration for the upload path.
        /// </summary>
        /// <param name="targetDirectory">The configured upload directory path if valid.</param>
        /// <returns>True if the configuration is valid, otherwise false.</returns>
        private bool ValidateConfiguration(out string targetDirectory)
        {
            targetDirectory = string.Empty; // Initialize out parameter
            var configuredPath = Plugin.Instance?.Configuration.UploadPath;
            if (string.IsNullOrEmpty(configuredPath))
            {
                _logger.LogError("Media Uploader: Upload path is not configured in plugin settings!");
                return false;
            }

            targetDirectory = configuredPath;
            _logger.LogInformation("Media Uploader: Using configured target directory: '{TargetDirectory}'", targetDirectory);
            return true;
        }

        /// <summary>
        /// Validates the uploaded file.
        /// </summary>
        /// <param name="file">The uploaded file.</param>
        /// <returns>True if the file is valid, otherwise false.</returns>
        private bool ValidateFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("Media Uploader: No file uploaded or file is empty.");
                return false;
            }

            _logger.LogInformation("Media Uploader: Received file '{FileName}' ({Length} bytes), type: '{ContentType}'", file.FileName, file.Length, file.ContentType);
            return true;
        }

        /// <summary>
        /// Prepares and validates the target path for the uploaded file.
        /// </summary>
        /// <param name="targetDirectory">The base directory for uploads.</param>
        /// <param name="fileName">The original name of the uploaded file.</param>
        /// <param name="fullTargetPath">The full validated and sanitized path for saving the file.</param>
        /// <returns>True if the path is valid and safe, otherwise false.</returns>
        private bool PrepareAndValidateTargetPath(string targetDirectory, string fileName, out string fullTargetPath)
        {
            fullTargetPath = string.Empty; // Initialize out parameter
            var originalFileName = Path.GetFileName(fileName); // Extract filename only
            var safeFileName = _fileSystem.GetValidFilename(originalFileName); // Sanitize filename
            var combinedPath = Path.Combine(targetDirectory, safeFileName);
            var fullTargetDirectory = Path.GetFullPath(targetDirectory);

            // Security Check: Ensure the resolved path is within the configured directory
            if (!Path.GetFullPath(combinedPath).StartsWith(fullTargetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Media Uploader: Invalid target path generated. Attempted Path: '{AttemptedPath}', Resolved Path: '{ResolvedPath}', Allowed Directory: '{AllowedDirectory}'",
                    combinedPath, // Log the potentially malicious path
                    Path.GetFullPath(combinedPath), // Log the resolved path
                    fullTargetDirectory);
                // fullTargetPath is already string.Empty due to initialization
                return false;
            }

            fullTargetPath = combinedPath;
            _logger.LogInformation("Media Uploader: Target path for file '{SafeFileName}' validated: '{FullTargetPath}'", safeFileName, fullTargetPath);
            return true;
        }

        /// <summary>
        /// Saves the uploaded file to the specified path.
        /// </summary>
        /// <param name="file">The file to save.</param>
        /// <param name="fullTargetPath">The full path where the file should be saved.</param>
        /// <returns>True if the file was saved successfully, otherwise false.</returns>
        private async Task<bool> SaveFileAsync(IFormFile file, string fullTargetPath)
        {
            var safeFileName = Path.GetFileName(fullTargetPath); // Get the sanitized filename for logging
            _logger.LogInformation("Media Uploader: Attempting to save file '{SafeFileName}' to '{FullTargetPath}'", safeFileName, fullTargetPath);
            FileStream? fileStream = null; // CS8600: Make FileStream nullable
            try
            {
                try // Inner try for FileStream operations
                {
                    fileStream = new FileStream(fullTargetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await file.CopyToAsync(fileStream).ConfigureAwait(false);
                }
                finally // Inner finally to dispose the FileStream
                {
                    if (fileStream != null)
                    {
                        await fileStream.DisposeAsync().ConfigureAwait(false);
                    }
                } // SA1513: Add blank line after this closing brace

                // _logger.LogInformation("Media Uploader: File '{SafeFileName}' successfully saved to '{FullTargetPath}'.", safeFileName, fullTargetPath); // Moved to UploadFile method
                return true;
            }
            catch (IOException ioEx) // More specific exception handling for file operations
            {
                _logger.LogError(ioEx, "Media Uploader: IO Error saving file '{SafeFileName}' to '{FullTargetPath}': {ErrorMessage}", safeFileName, fullTargetPath, ioEx.Message);
                return false;
            }
            catch (UnauthorizedAccessException) // Handle permission errors specifically for saving
            {
                // Re-throw to be caught by the main handler in UploadFile for a 403 response
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media Uploader: Error saving file '{SafeFileName}' to '{FullTargetPath}'", safeFileName, fullTargetPath);
                return false;
            }
        }

        /// <summary>
        /// Serves the static HTML page for direct uploads.
        /// </summary>
        /// <returns>An HTML page as ContentResult.</returns>
        [HttpGet("Page")] // Route: /Plugins/MediaUploader/Page
        [Produces("text/html")]
        public async Task<IActionResult> GetUploadPage()
        {
            _logger.LogInformation("Media Uploader: Serving static upload page request.");
            try
            {
                // Ensure this resource name exactly matches Namespace.Folder.FileName.ext
                var resourceName = "Jellyfin.Plugin.MediaUploader.Web.uploadPage.html";

                // Nullability of GetManifestResourceStream is handled by the check below.
                using var stream = GetEmbeddedResourceStream(resourceName);

                if (stream == null)
                {
                    _logger.LogError("Media Uploader: Could not find embedded resource: {ResourceName}. Check file exists, path/namespace, and Build Action='Embedded resource'.", resourceName);
                    return NotFound($"Resource not found: {resourceName}");
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var htmlContent = await reader.ReadToEndAsync().ConfigureAwait(false);

                return Content(htmlContent, "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Media Uploader: Error serving static upload page");
                 return StatusCode(StatusCodes.Status500InternalServerError, "Error serving upload page");
            }
        }

        /// <summary>
        /// Gets an embedded resource stream from the assembly.
        /// Protected virtual for testability.
        /// </summary>
        /// <param name="resourceName">The name of the resource.</param>
        /// <returns>The resource stream, or null if not found.</returns>
        protected virtual Stream? GetEmbeddedResourceStream(string resourceName)
        {
            var assembly = typeof(MediaUploadController).Assembly;
            return assembly.GetManifestResourceStream(resourceName);
        }
    }
}
