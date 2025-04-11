<#
.SYNOPSIS
Testet den Jellyfin Media Uploader Plugin API-Endpunkt durch das Hochladen einer Datei.

.DESCRIPTION
Sendet eine POST-Anfrage mit einer Datei (multipart/form-data) an den
angegebenen Jellyfin Media Uploader Plugin Endpunkt. Baut den Body manuell auf
für bessere Kompatibilität mit verschiedenen PowerShell-Versionen.

.PARAMETER FilePath
Der vollständige Pfad zur Mediendatei, die hochgeladen werden soll.

.PARAMETER JellyfinUrl
Die Basis-URL deiner Jellyfin Instanz (z.B. "http://localhost:8096").

.PARAMETER ApiKey
(Optional) Dein Jellyfin API-Schlüssel, falls Authentifizierung benötigt wird.

.EXAMPLE
.\MediaUpload.ps1 -FilePath "C:\pfad\zu\deinem\film.mkv"

.EXAMPLE
.\MediaUpload.ps1 -FilePath "C:\pfad\zu\deinem\film.mkv" -JellyfinUrl "http://192.168.1.100:8096"

.EXAMPLE
.\MediaUpload.ps1 -FilePath "C:\pfad\zu\deinem\film.mkv" -ApiKey "DEIN_API_KEY_HIER"
#>
param(
    [Parameter(Mandatory=$false)]
    [string]$FilePath = "C:\Users\jakob\Downloads\movie-loader\movies.txt",

    [Parameter(Mandatory=$false)]
    [string]$JellyfinUrl = "http://localhost:8096",

    [Parameter(Mandatory=$false)]
    [string]$ApiKey = "f5e9519d0d9b42649d77b76a82d8e46f"
)

# Ziel-URL zusammenbauen
$uploadUrl = "$($JellyfinUrl.TrimEnd('/'))/Plugins/MediaUploader/Upload"

# Prüfen, ob die Datei existiert
if (-not (Test-Path -Path $FilePath -PathType Leaf)) {
    Write-Error "Datei nicht gefunden: $FilePath"
    return # Skript beenden
}

# Header vorbereiten
$headers = @{}
if (-not [string]::IsNullOrEmpty($ApiKey)) {
    $headers.Add("X-Emby-Token", $ApiKey)
    Write-Host "Verwende API Key zur Authentifizierung."
} else {
    Write-Host "Versuche Upload ohne API Key."
}

Write-Host "Versuche '$FilePath' nach '$uploadUrl' hochzuladen..."

# --- Multipart/form-data Body manuell erstellen ---
$boundary = "---------------------------$([System.Guid]::NewGuid().ToString())"
$contentType = "multipart/form-data; boundary=$boundary"
$LF = "`r`n" # Zeilenumbruch für HTTP

# Dateiinformationen und Inhalt holen
$fileItem = Get-Item -Path $FilePath
try {
    $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
} catch {
     Write-Error "Fehler beim Lesen der Datei '$FilePath': $($_.Exception.Message)"
     return
}

# MIME-Typ bestimmen (Basis-Erkennung, kann verbessert werden)
$mimeType = switch ($fileItem.Extension.ToLower()) {
    ".mkv"  { "video/x-matroska" }
    ".mp4"  { "video/mp4" }
    ".avi"  { "video/x-msvideo" }
    ".mov"  { "video/quicktime" }
    ".wmv"  { "video/x-ms-wmv" }
    ".ts"   { "video/mp2t" }
    ".webm" { "video/webm" }
    # Füge weitere Typen hinzu bei Bedarf
    default { "application/octet-stream" } # Standard-Binärtyp
}

# Body-Teile als Text (außer Dateiinhalt)
$bodyLines = @(
    "--$boundary",
    # Der Parametername 'file' muss mit dem im C# Controller übereinstimmen (IFormFile file)
    "Content-Disposition: form-data; name=`"file`"; filename=`"$($fileItem.Name)`"",
    "Content-Type: $mimeType",
    "" # Leere Zeile als Trenner
)

# Header-Teil des Bodys in Bytes umwandeln (UTF8)
$bodyHeaderString = ($bodyLines -join $LF) + $LF
$bodyHeaderBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyHeaderString)

# Footer-Teil des Bodys in Bytes umwandeln (UTF8)
$bodyFooterString = $LF + "--$boundary--" + $LF
$bodyFooterBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyFooterString)

# Alle Teile kombinieren: Header-Bytes + Datei-Bytes + Footer-Bytes
$bodyBytes = $bodyHeaderBytes + $fileBytes + $bodyFooterBytes
# --- Ende Body-Erstellung ---


# Stoppuhr für die Zeitmessung
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    # Invoke-RestMethod verwenden, da es die Antwort oft besser verarbeitet
    # Wir übergeben die manuell erstellten Bytes als Body
    $response = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -ContentType $contentType -Body $bodyBytes

    $stopwatch.Stop()
    Write-Host "`n--- Server Antwort (Dauer: $($stopwatch.Elapsed.TotalSeconds)s) ---"
    # Invoke-RestMethod gibt bei Erfolg oft direkt den Body-Inhalt zurück (hier erwarten wir Text)
    Write-Host $response
    Write-Host "----------------------------------"
    # Da Invoke-RestMethod bei Fehlern eine Exception wirft, ist der Code hier = Erfolg (Status 2xx)
    Write-Host "Upload Befehl erfolgreich gesendet (Status 2xx). Prüfe Server-Logs und Dateisystem!" -ForegroundColor Green

} catch {
    $stopwatch.Stop()
    Write-Error "Fehler während der Web-Anfrage (Dauer: $($stopwatch.Elapsed.TotalSeconds)s):"
    Write-Error $_.Exception.Message

    # Versuche, Statuscode und Antwort aus der Exception zu extrahieren
    $statusCode = $null
    $errorContent = $null
    if ($_.Exception.Response) {
         try { $statusCode = [int]$_.Exception.Response.StatusCode } catch {}
         try {
             $stream = $_.Exception.Response.GetResponseStream()
             $reader = New-Object System.IO.StreamReader($stream)
             $errorContent = $reader.ReadToEnd()
         } catch {
             $errorContent = "Fehlerinhalt konnte nicht gelesen werden."
         }
    }
    if ($statusCode) { Write-Error "HTTP Status Code: $statusCode" }
    if ($errorContent) { Write-Error "Fehler Antwort Inhalt: $errorContent" }
}

Write-Host "`nSkript beendet."
