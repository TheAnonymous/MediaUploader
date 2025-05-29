using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Jellyfin.Plugin.MediaUploader;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Jellyfin.Plugin.MediaUploader.Configuration;
using System.Reflection;
using System.Text;

namespace Jellyfin.Plugin.MediaUploader.Tests
{
    [TestClass]
    public class MediaUploadControllerTests
    {
        private Mock<ILogger<MediaUploadController>> _mockLogger = null!;
        private Mock<IServerConfigurationManager> _mockServerConfigurationManager = null!; // Not used by current controller methods but good to have
        private Mock<IFileSystem> _mockFileSystem = null!;
        private Mock<ILibraryManager> _mockLibraryManager = null!; // Not used by current controller methods but good to have for future tests
        private MediaUploadController _controller = null!;

        // Store original Plugin.Instance if we modify it, to restore later
        private static readonly FieldInfo? PluginInstanceField = typeof(Plugin).GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        private Plugin? _originalPluginInstance = null!;
        // private PluginConfiguration _originalPluginConfiguration; // CS0169: Unused


        [TestInitialize]
        public void TestInitialize()
        {
            _mockLogger = new Mock<ILogger<MediaUploadController>>();
            _mockServerConfigurationManager = new Mock<IServerConfigurationManager>();
            _mockFileSystem = new Mock<IFileSystem>();
            _mockLibraryManager = new Mock<ILibraryManager>();

            _controller = new MediaUploadController(
                _mockLogger.Object,
                _mockServerConfigurationManager.Object,
                _mockFileSystem.Object,
                _mockLibraryManager.Object);

            // Backup original Plugin.Instance and its configuration if possible
            if (PluginInstanceField != null)
            {
                _originalPluginInstance = Plugin.Instance; // This might be null if not initialized
                if (_originalPluginInstance != null)
                {
                    // Assuming Configuration is a property that returns a copy or we need to clone it
                    // For simplicity, if direct replacement is the strategy, this might not be needed,
                    // or we might need a deeper clone.
                    // If Plugin.Instance.Configuration is directly mutable, we need to save its state.
                    // Let's assume for now we are replacing the whole Plugin.Instance for tests needing specific config.
                }
            }
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // Restore original Plugin.Instance if it was changed
            if (PluginInstanceField != null && _originalPluginInstance != null) // Only restore if we had an original
            {
                 // Potentially problematic if other tests run in parallel or if Instance has complex state
                PluginInstanceField.SetValue(null, _originalPluginInstance);
            } else if (PluginInstanceField != null) {
                 PluginInstanceField.SetValue(null, null); // Set back to null if it was originally null
            }
        }

        private bool SetPluginUploadPath(string? uploadPath) // Allow null for uploadPath
        {
            if (PluginInstanceField == null)
            {
                System.Diagnostics.Debug.WriteLine("Plugin.Instance field not found via reflection.");
                return false; // Cannot set instance
            }

            try
            {
                // Get current instance or create a new one for testing
                // This part is tricky and depends heavily on Plugin's constructor and accessibility
                // Forcing a new instance might not be feasible if constructor is internal/private
                // or has complex dependencies not easily mocked.
                var currentPlugin = Plugin.Instance;
                if (currentPlugin == null)
                {
                    // Attempt to create a new Plugin instance for testing.
                    // This requires knowledge of Plugin's constructor.
                    // Assuming a parameterless constructor or one that can be handled with nulls for testing.
                    // This is a common pain point for testing singletons.
                    // If the actual Plugin constructor is complex, this will fail or needs more setup.
                    try {
                        // Try to create a new instance. This is often not possible if constructor is internal or has many dependencies.
                        // This is a placeholder for however one might get/set a test instance of Plugin.
                        // For many singletons, you might be stuck without internal changes to the Plugin class.
                        var testPluginInstance = (Plugin)Activator.CreateInstance(typeof(Plugin), true); // true for non-public constructor
                         PluginInstanceField.SetValue(null, testPluginInstance);
                         currentPlugin = testPluginInstance;
                    } catch (Exception ex) {
                        System.Diagnostics.Debug.WriteLine($"Error creating Plugin instance for test: {ex.Message}");
                        // If we can't create an instance, we can't set its config.
                        // This test path would then be inconclusive.
                        // Fallback: if there's an existing instance (e.g. from a previous test or app init), try to use it.
                        // This is dangerous as test state can leak.
                        if (Plugin.Instance == null) return false; // Still null, give up.
                        currentPlugin = Plugin.Instance;

                    }
                }


                var configToSet = new PluginConfiguration { UploadPath = uploadPath ?? string.Empty }; // Use string.Empty if uploadPath is null

                // Try to set the Configuration property on the Plugin instance
                var configProperty = typeof(Plugin).GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance);
                if (configProperty != null && configProperty.CanWrite)
                {
                    configProperty.SetValue(currentPlugin, configToSet);
                    System.Diagnostics.Debug.WriteLine($"Set UploadPath to '{uploadPath}' via Configuration property.");
                    return true;
                }
                
                // Try to call UpdateConfiguration method on the Plugin instance
                var updateMethod = typeof(Plugin).GetMethod("UpdateConfiguration", BindingFlags.Public | BindingFlags.Instance);
                if (updateMethod != null)
                {
                    updateMethod.Invoke(currentPlugin, new object[] { configToSet });
                    System.Diagnostics.Debug.WriteLine($"Set UploadPath to '{uploadPath}' via UpdateConfiguration method.");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("Could not set PluginConfiguration.UploadPath via reflection (Property or Method).");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reflection failed to set UploadPath: {ex.Message}");
                return false;
            }
        }

        [TestMethod]
        public async Task UploadFile_PathNotConfigured_ReturnsInternalServerError()
        {
            // Arrange
            // Ensure Plugin.Instance.Configuration.UploadPath is null or empty.
            // This might be the default state if Plugin.Instance is null or not fully initialized.
            SetPluginUploadPath(string.Empty); // Use string.Empty to represent not configured
            // We are testing the scenario where the path is not configured.
            // If Plugin.Instance or its config is null, controller should handle it.
            // If SetPluginUploadPath fails to *clear* a pre-existing path from another test's static interference,
            // this test might become flaky. TestCleanup should handle this.

            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.FileName).Returns("test.mp4");
            mockFile.Setup(f => f.Length).Returns(1024); // Must be > 0 for this check to pass

            // Act
            var result = await _controller.UploadFile(mockFile.Object);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.IsNotNull(objectResult);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.IsNotNull(objectResult.Value);
            Assert.AreEqual("Upload path is not configured in plugin settings.", objectResult.Value.ToString());
        }

        [TestMethod]
        public async Task UploadFile_NoFileUploaded_ReturnsBadRequest()
        {
            // Arrange
            // No specific configuration needed for UploadPath for this test,
            // as it should fail before that check. But good to have a default valid one.
            var tempUploadPath = Path.Combine(Path.GetTempPath(), "mediauploader_tests_NoFile");
            SetPluginUploadPath(tempUploadPath); // If this fails, test might still pass as it's an earlier check.

            // Act
            var result = await _controller.UploadFile(null!); // Pass null IFormFile, null! to satisfy compiler for IFormFile parameter

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequestResult = result as BadRequestObjectResult;
            Assert.IsNotNull(badRequestResult);
            Assert.IsNotNull(badRequestResult.Value);
            Assert.AreEqual("No file uploaded or file is empty.", badRequestResult.Value.ToString());

            // Cleanup (optional, as no file should be created)
            if (Directory.Exists(tempUploadPath)) Directory.Delete(tempUploadPath, true);
        }

        [TestMethod]
        public async Task UploadFile_EmptyFileUploaded_ReturnsBadRequest()
        {
            // Arrange
            var tempUploadPath = Path.Combine(Path.GetTempPath(), "mediauploader_tests_EmptyFile");
            SetPluginUploadPath(tempUploadPath);

            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.FileName).Returns("empty.mp4");
            mockFile.Setup(f => f.Length).Returns(0); // Empty file

            // Act
            var result = await _controller.UploadFile(mockFile.Object);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequestResult = result as BadRequestObjectResult;
            Assert.IsNotNull(badRequestResult);
            Assert.IsNotNull(badRequestResult.Value);
            Assert.AreEqual("No file uploaded or file is empty.", badRequestResult.Value.ToString());
            
            if (Directory.Exists(tempUploadPath)) Directory.Delete(tempUploadPath, true);
        }

        [TestMethod]
        public async Task UploadFile_ValidFileAndConfiguration_ReturnsOkResult()
        {
            // Arrange
            var tempUploadPath = Path.Combine(Path.GetTempPath(), "mediauploader_tests_Valid");
            Directory.CreateDirectory(tempUploadPath); // Ensure directory exists for Path.GetFullPath

            if (!SetPluginUploadPath(tempUploadPath))
            {
                Assert.Inconclusive($"Could not set upload path to '{tempUploadPath}' via reflection. Test cannot proceed.");
            }
            
            var mockFile = new Mock<IFormFile>();
            var fileName = "test video.mp4"; // Filename with space to test sanitization
            var safeFileName = "test video.mp4"; // Assuming GetValidFilename doesn't change it, or mock accordingly
            var fullTargetPath = Path.Combine(tempUploadPath, safeFileName);

            mockFile.Setup(f => f.FileName).Returns(fileName);
            mockFile.Setup(f => f.Length).Returns(1024);
            mockFile.Setup(f => f.ContentType).Returns("video/mp4");

            // Mock CopyToAsync to actually create a file for more robust testing if needed,
            // or just ensure it's called. For strict unit test, just verify call.
            var tcs = new TaskCompletionSource<bool>();
            mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback((Stream stream, CancellationToken token) => {
                    // Simulate file write by closing the stream immediately for the test
                    // or actually writing some dummy bytes if needed for other checks.
                    stream.Close(); 
                    tcs.SetResult(true);
                })
                .Returns(tcs.Task);


            _mockFileSystem.Setup(fs => fs.GetValidFilename(fileName)).Returns(safeFileName);

            // Act
            var result = await _controller.UploadFile(mockFile.Object);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);
            Assert.AreEqual($"File {safeFileName} uploaded successfully.", okResult.Value.ToString());
            
            mockFile.Verify(f => f.CopyToAsync(It.Is<Stream>(s => s is FileStream && ((FileStream)s).Name == fullTargetPath), It.IsAny<CancellationToken>()), Times.Once);

            // Cleanup
            if (Directory.Exists(tempUploadPath))
            {
                Directory.Delete(tempUploadPath, true);
            }
        }

        [TestMethod]
        public async Task UploadFile_InvalidTargetPath_PathTraversal_ReturnsInternalServerError()
        {
            // Arrange
            var tempUploadPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mediauploader_tests_Traversal"));
            Directory.CreateDirectory(tempUploadPath);

            if (!SetPluginUploadPath(tempUploadPath))
            {
                Assert.Inconclusive("Could not set upload path for testing.");
            }

            var mockFile = new Mock<IFormFile>();
            // Filename that attempts path traversal.
            // The controller uses Path.GetFileName() which mitigates simple "../" in filename part.
            // The IFileSystem.GetValidFilename() is also called.
            // The check is `!Path.GetFullPath(fullTargetPath).StartsWith(fullTargetDirectory, ...)`
            // To fail this, `GetValidFilename` must produce something that, when combined and full-pathed,
            // goes outside `fullTargetDirectory`.
            // var maliciousFileName = "../../../etc/passwd"; // Example - CS0219 Unused
            // var sanitizedMaliciousFileName = "etc_passwd"; // Example of what GetValidFilename might do - CS0219 Unused

            // We need GetValidFilename to return something that *seems* safe, but combined path is tricky.
            // The controller does:
            // 1. originalFileName = Path.GetFileName(file.FileName); -> "passwd" if file.FileName is "../../../etc/passwd"
            // 2. safeFileName = _fileSystem.GetValidFilename(originalFileName); -> mock this
            // 3. fullTargetPath = Path.Combine(targetDirectory, safeFileName);
            // 4. Security check: Path.GetFullPath(fullTargetPath).StartsWith(Path.GetFullPath(targetDirectory))
            // To make this fail, safeFileName needs to be something like "../outside_file.txt"
            // AND Path.GetFullPath(Path.Combine(tempUploadPath, "../outside_file.txt")) must resolve outside tempUploadPath.

            mockFile.Setup(f => f.FileName).Returns("somefile.txt"); // Original filename given by user
            mockFile.Setup(f => f.Length).Returns(1024);

            // This setup makes GetValidFilename return a name that attempts traversal.
            // This relies on the actual Path.Combine and Path.GetFullPath to resolve this.
            // The default Path.GetFullPath on Linux will resolve "../"
            _mockFileSystem.Setup(fs => fs.GetValidFilename("somefile.txt")).Returns("../attempt_traversal.txt");

            // Act
            var result = await _controller.UploadFile(mockFile.Object);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.IsNotNull(objectResult);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.IsNotNull(objectResult.Value);
            Assert.AreEqual("Invalid target path.", objectResult.Value.ToString());

            // Cleanup
            if (Directory.Exists(tempUploadPath))
            {
                Directory.Delete(tempUploadPath, true);
            }
        }

        [TestMethod]
        public async Task UploadFile_IOExceptionOnSave_ReturnsInternalServerError()
        {
            // Arrange
            var tempUploadPath = Path.Combine(Path.GetTempPath(), "mediauploader_tests_IOException");
             Directory.CreateDirectory(tempUploadPath);
            if (!SetPluginUploadPath(tempUploadPath))
            {
                Assert.Inconclusive("Could not set upload path for testing.");
            }

            var mockFile = new Mock<IFormFile>();
            var fileName = "ioexception.txt";
            mockFile.Setup(f => f.FileName).Returns(fileName);
            mockFile.Setup(f => f.Length).Returns(1024);
            _mockFileSystem.Setup(fs => fs.GetValidFilename(fileName)).Returns(fileName);

            mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Disk full"));

            // Act
            var result = await _controller.UploadFile(mockFile.Object);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.IsNotNull(objectResult);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.IsNotNull(objectResult.Value);
            Assert.IsTrue(objectResult.Value.ToString()!.StartsWith($"Error saving file {fileName}."));

            if (Directory.Exists(tempUploadPath)) Directory.Delete(tempUploadPath, true);
        }

        [TestMethod]
        public async Task UploadFile_UnauthorizedAccessExceptionOnSave_ReturnsForbidden()
        {
            // Arrange
            var tempUploadPath = Path.Combine(Path.GetTempPath(), "mediauploader_tests_AuthException");
            Directory.CreateDirectory(tempUploadPath);
            if (!SetPluginUploadPath(tempUploadPath))
            {
                Assert.Inconclusive("Could not set upload path for testing.");
            }

            var mockFile = new Mock<IFormFile>();
            var fileName = "unauthorized.txt";
            mockFile.Setup(f => f.FileName).Returns(fileName);
            mockFile.Setup(f => f.Length).Returns(1024);
            _mockFileSystem.Setup(fs => fs.GetValidFilename(fileName)).Returns(fileName);

            mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new UnauthorizedAccessException("Permission denied"));
            
            // Act
            var result = await _controller.UploadFile(mockFile.Object);

            // Assert
            // The SaveFileAsync catches UnauthorizedAccessException and returns false.
            // The UploadFile method then returns 500.
            // To get 403, the UnauthorizedAccessException needs to be caught by the main try-catch in UploadFile,
            // which means it would need to propagate from SaveFileAsync, or occur before/after SaveFileAsync.
            // Controller's SaveFileAsync now re-throws UnauthorizedAccessException.
            // The main try-catch in UploadFile should catch this and return 403.
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.IsNotNull(objectResult);
            Assert.AreEqual(StatusCodes.Status403Forbidden, objectResult.StatusCode);
            Assert.IsNotNull(objectResult.Value);
            // The exception message comes from the mocked exception.
            Assert.AreEqual("Permission denied: Permission denied", objectResult.Value.ToString());


            if (Directory.Exists(tempUploadPath)) Directory.Delete(tempUploadPath, true);
        }


        // --- GetUploadPage Tests ---

        [TestMethod]
        public async Task GetUploadPage_ResourceExists_ReturnsHtmlContent()
        {
            // Arrange
            // This test relies on the embedded resource being accessible from the main assembly.
            // No specific mocks needed for this success case if resource loading works.

            // Act
            var result = await _controller.GetUploadPage();

            // Assert
            Assert.IsInstanceOfType(result, typeof(ContentResult));
            var contentResult = result as ContentResult;
            Assert.IsNotNull(contentResult);
            Assert.AreEqual("text/html; charset=utf-8", contentResult.ContentType); // ASP.NET Core appends charset=utf-8
            Assert.IsNotNull(contentResult.Content);
            Assert.IsTrue(contentResult.Content.Contains("<html>") && contentResult.Content.Contains("</html>"));
            Assert.IsTrue(contentResult.Content.Contains("<form"));
        }
        
        // Cannot easily mock GetManifestResourceStream returning null without refactoring controller
        // or using more advanced mocking frameworks for static/extension methods or Assembly.
        // So, a direct test for "resource not found" is omitted.
        // It would require changing the controller to allow injection of an Assembly object or similar.
        // For example: new MediaUploadController(..., Func<string, Stream> resourceStreamProvider)
        // Or a protected virtual method GetResourceStream(string name) in the controller.

        // Test class that derives from the controller to mock the GetEmbeddedResourceStream method
        private class MediaUploadControllerWithMockedResourceStream : MediaUploadController
        {
            private readonly Stream? _mockedStream;

            public MediaUploadControllerWithMockedResourceStream(
                ILogger<MediaUploadController> logger,
                IServerConfigurationManager configurationManager,
                IFileSystem fileSystem,
                ILibraryManager libraryManager,
                Stream? mockedStream) // Pass the stream to return
                : base(logger, configurationManager, fileSystem, libraryManager)
            {
                _mockedStream = mockedStream;
            }

            protected override Stream? GetEmbeddedResourceStream(string resourceName)
            {
                // Return the mocked stream that was passed in the constructor
                return _mockedStream;
            }
        }

        [TestMethod]
        public async Task GetUploadPage_ResourceNotFound_ReturnsNotFoundResult()
        {
            // Arrange
            // Use the derived controller where GetEmbeddedResourceStream returns null
            var controllerWithMockedStream = new MediaUploadControllerWithMockedResourceStream(
                _mockLogger.Object,
                _mockServerConfigurationManager.Object,
                _mockFileSystem.Object,
                _mockLibraryManager.Object,
                null); // Provide null to simulate resource not found

            // Act
            var result = await controllerWithMockedStream.GetUploadPage();

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            var notFoundResult = result as NotFoundObjectResult;
            Assert.IsNotNull(notFoundResult);
            Assert.IsNotNull(notFoundResult.Value);
            Assert.AreEqual("Resource not found: Jellyfin.Plugin.MediaUploader.Web.uploadPage.html", notFoundResult.Value.ToString());
        }
    }
}
