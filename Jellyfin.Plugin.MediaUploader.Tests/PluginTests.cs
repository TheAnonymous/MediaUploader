using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Jellyfin.Plugin.MediaUploader;
using MediaBrowser.Common.Configuration; // For IApplicationPaths
using MediaBrowser.Model.Plugins; // For PluginPageInfo
using MediaBrowser.Model.Serialization; // For IXmlSerializer
using System.Linq;
using System; // For Guid

namespace Jellyfin.Plugin.MediaUploader.Tests
{
    [TestClass]
    public class PluginTests
    {
        private Mock<IApplicationPaths> _mockAppPaths = null!;
        private Mock<IXmlSerializer> _mockXmlSerializer = null!;
        private Plugin _plugin = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockAppPaths = new Mock<IApplicationPaths>();
            _mockXmlSerializer = new Mock<IXmlSerializer>();

            // The Plugin constructor requires these arguments.
            // Specific setups for these mocks can be added if tests rely on their behavior.
            _plugin = new Plugin(_mockAppPaths.Object, _mockXmlSerializer.Object);
        }

        [TestMethod]
        public void Plugin_Metadata_ReturnsCorrectValues()
        {
            // Arrange (Plugin instance is created in TestInitialize)

            // Act & Assert
            Assert.AreEqual("Media Uploader", _plugin.Name, "Plugin name is incorrect.");
            Assert.AreEqual("Allows uploading media files directly via the web interface.", _plugin.Description, "Plugin description is incorrect.");
            Assert.AreEqual(new Guid("514d4276-bf23-4a85-b074-66b4cd38fd90"), _plugin.Id, "Plugin ID is incorrect.");
            Assert.AreEqual(true, _plugin.CanUninstall, "Plugin CanUninstall property is incorrect.");
            
            // Assuming version is static or part of constructor/properties.
            // Let's check Plugin.cs to confirm how version is handled.
            // For now, if there's a fixed version string, assert it.
            // If it's dynamic (e.g., from assembly), this might need a different approach or be omitted.
            // Assert.AreEqual("1.0.0.0", _plugin.Version, "Plugin version is incorrect."); // Example, will verify
        }

        [TestMethod]
        public void GetPages_ReturnsCorrectPluginPageInfo()
        {
            // Arrange (Plugin instance is created in TestInitialize)

            // Act
            var pages = _plugin.GetPages();

            // Assert
            Assert.IsNotNull(pages, "GetPages should not return null.");
            Assert.AreEqual(1, pages.Count(), "GetPages should return exactly one page.");

            var pluginPageInfo = pages.First();
            Assert.IsNotNull(pluginPageInfo, "PluginPageInfo should not be null.");
            Assert.AreEqual("Media Uploader", pluginPageInfo.Name, "PluginPageInfo name is incorrect.");
            Assert.AreEqual("Jellyfin.Plugin.MediaUploader.Configuration.configPage.html", pluginPageInfo.EmbeddedResourcePath, "PluginPageInfo EmbeddedResourcePath is incorrect.");
            // PluginId is not a direct property of PluginPageInfo. The association is implicit.
        }
    }
}
