# Media Uploader Plugin for Jellyfin

A plugin for Jellyfin media server that allows uploading media files directly via a web interface.

## Overview

This plugin provides two main functionalities:

1.  An API endpoint (`/Plugins/MediaUploader/Upload`) to programmatically upload files.
2.  A simple, standalone web page accessible via a direct link, allowing users (including non-admins) to upload files using their personal Jellyfin API Key.

The target directory for uploads must be configured in the plugin settings.

## Features

* Direct file upload via HTTP POST request.
* Standalone HTML upload page accessible via a direct link (useful for non-admin users).
* Configurable target directory for uploads via the admin dashboard.
* Basic progress bar during upload on the standalone page.
* Option to save API Key in the browser's local storage (use with caution).

## Installation

There are currently two main ways to install this plugin:

**1. Via Release Package (Recommended when available):**

* Download the `Jellyfin.Plugin.MediaUploader_x.x.x.x.zip` file from the [Plugin Releases Page](link-to-your-github-releases-page-later).
* Place the downloaded `.zip` file into your Jellyfin server's plugin directory.
* Restart your Jellyfin server.
* **Important Note:** Due to potential issues with Jellyfin loading plugins from ZIP files in some environments (as experienced during development), you might need to perform a **manual extraction** instead:
    * Stop the Jellyfin server.
    * Create a folder named `Jellyfin.Plugin.MediaUploader` inside your server's `plugins` directory.
    * Extract the **contents** of the downloaded ZIP file directly into this new folder.
    * Start the Jellyfin server.

**Common Plugin Directory Locations:**
    * **Windows:** `C:\ProgramData\Jellyfin\Server\plugins`
    * **Linux (Package):** `/var/lib/jellyfin/plugins`
    * **Linux (Docker):** Typically mapped to `/config/plugins` inside the container.

**2. Building from Source:**

* See the "Building from Source" section below. Follow the build steps and then use the manual extraction method described above to install the built files.

## Configuration

After installing the plugin and restarting Jellyfin:

1.  Navigate to your Jellyfin Dashboard.
2.  Go to **Plugins** in the sidebar.
3.  Find **"Media Uploader"** in the list and click on it (or the settings icon).
4.  **Set the Target Upload Path:** You **must** enter the full path to a directory on your server where you want uploaded files to be saved (e.g., `/srv/jellyfin/uploads` or `D:\Media\Uploads`).
    * **Crucially:** Ensure the user account running the Jellyfin server process has **write permissions** for this directory!
5.  Click **Save**.
6.  On the same settings page, you will find the direct link to the standalone upload page.

## Usage (Direct Upload Page)

1.  Obtain the link to the **Direct Upload Page** from the plugin's configuration page in the Jellyfin Dashboard. Bookmark this link for easy access.
2.  **Generate an API Key:** If you don't have one, go to your Jellyfin user profile (click your profile icon top right) &rarr; Settings &rarr; API Keys &rarr; Click the '+' icon to create a new key. Give it a name (e.g., "MediaUpload Key") and copy the generated key. **Keep this key secure!**
3.  **Open the Upload Page:** Navigate to the bookmarked link.
4.  **Enter API Key:** Paste your generated API Key into the input field. You can optionally check the box to save the key in your browser's local storage for next time.
    * <span style="color:red;">**Security Warning:**</span> Saving API Keys in local storage is less secure. Only use this feature in trusted environments on private computers. For higher security, use a password manager and paste the key each time.
5.  **Select File:** Choose the media file you want to upload.
6.  **Start Upload:** Click the "Start Upload" button. You should see a progress bar.
7.  Wait for the success or error message. Uploaded files will be saved to the directory configured by the administrator.

## Building from Source (Optional)

If you want to build the plugin yourself:

1.  **Prerequisites:**
    * .NET 8 SDK ([https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0))
    * Git
2.  **Clone the repository:**
    ```bash
    git clone [URL-to-your-plugin-repository]
    cd [repository-folder]
    ```
3.  **Restore dependencies:**
    ```bash
    dotnet restore
    ```
4.  **Build and Package:**
    * Ensure the `build.ps1` script is present in the root directory. (Note: This script might need to be created using the code provided during development, as the official template may not include it anymore).
    * Run the build script from PowerShell:
        ```powershell
        # Allow script execution for this process
        Set-ExecutionPolicy Bypass -Scope Process -Force
        # Run the build script
        .\build.ps1
        ```
    * This will create a `dist` folder containing the installable `Jellyfin.Plugin.MediaUploader_x.x.x.x.zip` file.
5.  **Install:** Follow the manual extraction steps under the "Installation" section using the files from the `dist` folder or the generated ZIP.

## Contributing (Optional)

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

## License

This plugin is licensed under the [GPLv3](link-to-your-license-file-or-standard-gpl3), as it links against Jellyfin core components which are also GPLv3 licensed.