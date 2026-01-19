using System.Globalization;
using System.Security.Cryptography;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;

namespace ExifSorter;

class Program
{
    private static readonly string[] SupportedExtensions =
    [
        // JPEG
        ".jpg", ".jpeg",
        // RAW formats
        ".raw", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2",
        ".dng", ".orf", ".pef", ".rw2", ".raf", ".srw", ".x3f", ".erf",
        ".mrw", ".3fr", ".fff", ".rwl", ".dcr", ".kdc", ".awr",
        // Video formats
        ".mp4", ".mov", ".m4v", ".3gp", ".3g2",  // QuickTime/MP4
        ".avi", ".mts", ".m2ts", ".mkv", ".wmv", ".webm"  // Other video formats
    ];

    private static bool _dryRun;
    private static bool _copyMode;
    private static int _processedCount;
    private static int _skippedCount;
    private static int _warningCount;
    private static int _errorCount;

    static int Main(string[] args)
    {
        if (!ParseArguments(args, out var inputDir, out var outputDir, out _dryRun, out _copyMode))
        {
            PrintUsage();
            return 1;
        }

        if (!System.IO.Directory.Exists(inputDir))
        {
            Console.Error.WriteLine($"FEHLER: Eingabeverzeichnis existiert nicht: {inputDir}");
            return 1;
        }

        Console.WriteLine($"ExifSorter - Foto/Video-Sortierung nach Metadaten");
        Console.WriteLine($"================================================");
        Console.WriteLine($"Eingabe:  {inputDir}");
        Console.WriteLine($"Ausgabe:  {outputDir}");
        Console.WriteLine($"Modus:    {(_copyMode ? "KOPIEREN" : "VERSCHIEBEN")}{(_dryRun ? " (DRY-RUN)" : "")}");
        Console.WriteLine();

        ProcessDirectory(inputDir, inputDir, outputDir);

        var actionName = _copyMode ? "Kopiert" : "Verschoben";
        Console.WriteLine();
        Console.WriteLine($"================================================");
        Console.WriteLine($"Zusammenfassung:");
        Console.WriteLine($"  {actionName}:    {_processedCount}");
        Console.WriteLine($"  Übersprungen: {_skippedCount}");
        Console.WriteLine($"  Warnungen:    {_warningCount}");
        Console.WriteLine($"  Fehler:       {_errorCount}");

        return _errorCount > 0 ? 1 : 0;
    }

    private static bool ParseArguments(string[] args, out string inputDir, out string outputDir, out bool dryRun, out bool copyMode)
    {
        inputDir = string.Empty;
        outputDir = string.Empty;
        dryRun = false;
        copyMode = false;

        var positionalArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--dry-run" || args[i] == "-n")
            {
                dryRun = true;
            }
            else if (args[i] == "--copy" || args[i] == "-c")
            {
                copyMode = true;
            }
            else if (args[i] == "--help" || args[i] == "-h")
            {
                return false;
            }
            else
            {
                positionalArgs.Add(args[i]);
            }
        }

        if (positionalArgs.Count != 2)
        {
            return false;
        }

        inputDir = Path.GetFullPath(positionalArgs[0]);
        outputDir = Path.GetFullPath(positionalArgs[1]);
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Verwendung: ExifSorter <Eingabeverzeichnis> <Ausgabeverzeichnis> [Optionen]");
        Console.WriteLine();
        Console.WriteLine("Sortiert Fotos und Videos anhand der Metadaten in Unterordner.");
        Console.WriteLine("Format: yyyy\\MM-dd");
        Console.WriteLine();
        Console.WriteLine("Optionen:");
        Console.WriteLine("  --copy, -c       Kopiert Dateien (Standard: verschieben)");
        Console.WriteLine("  --dry-run, -n    Zeigt an, was passieren würde (keine Änderungen)");
        Console.WriteLine("  --help, -h       Zeigt diese Hilfe an");
        Console.WriteLine();
        Console.WriteLine("Unterstützte Formate:");
        Console.WriteLine("  Fotos: JPG, JPEG, und gängige RAW-Formate");
        Console.WriteLine("         (CR2, CR3, NEF, ARW, DNG, ORF, PEF, RW2, RAF, etc.)");
        Console.WriteLine("  Video: MP4, MOV, M4V, AVI, MKV, MTS, M2TS, WMV, WEBM, 3GP");
    }

    private static void ProcessDirectory(string currentDir, string baseInputDir, string outputDir)
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(currentDir))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (SupportedExtensions.Contains(extension))
            {
                ProcessFile(file, baseInputDir, outputDir);
            }
        }

        foreach (var subDir in System.IO.Directory.EnumerateDirectories(currentDir))
        {
            ProcessDirectory(subDir, baseInputDir, outputDir);
        }
    }

    private static void ProcessFile(string filePath, string baseInputDir, string outputDir)
    {
        var fileName = Path.GetFileName(filePath);
        var relativePath = Path.GetRelativePath(baseInputDir, Path.GetDirectoryName(filePath)!);

        DateTime? exifDate = GetExifDate(filePath);
        string targetSubDir;

        if (exifDate.HasValue)
        {
            // Format: yyyy\MM-dd
            targetSubDir = Path.Combine(
                exifDate.Value.Year.ToString("D4"),
                exifDate.Value.ToString("MM-dd")
            );
        }
        else
        {
            // Kein Metadaten-Datum: Verschiebe nach 0000 mit relativem Pfad
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARNUNG: Keine Datums-Metadaten gefunden: {filePath}");
            Console.ResetColor();
            _warningCount++;

            if (relativePath == ".")
            {
                targetSubDir = "0000";
            }
            else
            {
                targetSubDir = Path.Combine("0000", relativePath);
            }
        }

        var targetDir = Path.Combine(outputDir, targetSubDir);
        var targetPath = Path.Combine(targetDir, fileName);

        // Prüfe ob Zieldatei bereits existiert
        if (File.Exists(targetPath))
        {
            if (FilesAreIdentical(filePath, targetPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARNUNG: Identische Datei existiert bereits, überspringe: {targetPath}");
                Console.ResetColor();
                _warningCount++;
                _skippedCount++;
                return;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FEHLER: Zieldatei existiert mit unterschiedlichem Inhalt: {targetPath}");
                Console.ResetColor();
                _errorCount++;
                return;
            }
        }

        // Kopiere oder verschiebe Datei
        var action = _copyMode ? "kopieren" : "verschieben";
        var actionPast = _copyMode ? "Kopiert" : "Verschoben";

        if (_dryRun)
        {
            Console.WriteLine($"[DRY-RUN] Würde {action}: {filePath}");
            Console.WriteLine($"          -> {targetPath}");
        }
        else
        {
            try
            {
                System.IO.Directory.CreateDirectory(targetDir);
                if (_copyMode)
                {
                    File.Copy(filePath, targetPath);
                }
                else
                {
                    File.Move(filePath, targetPath);
                }
                Console.WriteLine($"{actionPast}: {fileName} -> {targetSubDir}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FEHLER beim {action.TrimEnd('n')}n von {filePath}: {ex.Message}");
                Console.ResetColor();
                _errorCount++;
                return;
            }
        }

        _processedCount++;
    }

    private static DateTime? GetExifDate(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // 1. Suche nach EXIF DateTimeOriginal (Fotos)
            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifSubIfd != null)
            {
                if (exifSubIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTimeOriginal))
                {
                    return dateTimeOriginal;
                }
            }

            // 2. Suche nach XMP-Metadaten (DJI Drohnen, etc.)
            var xmpDirectory = directories.OfType<XmpDirectory>().FirstOrDefault();
            if (xmpDirectory?.XmpMeta != null)
            {
                var xmpDate = GetXmpDate(xmpDirectory);
                if (xmpDate.HasValue)
                {
                    return xmpDate;
                }
            }

            // 4. Suche nach QuickTime/MP4 Created Date (Videos)
            var quickTimeHeader = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
            if (quickTimeHeader != null)
            {
                if (quickTimeHeader.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var createdDate))
                {
                    // QuickTime verwendet manchmal 1904 als Basis-Epoche, prüfe auf gültiges Datum
                    if (createdDate.Year > 1970)
                    {
                        return createdDate;
                    }
                }
            }

            // 5. Fallback: QuickTime Track Header
            var quickTimeTrack = directories.OfType<QuickTimeTrackHeaderDirectory>().FirstOrDefault();
            if (quickTimeTrack != null)
            {
                if (quickTimeTrack.TryGetDateTime(QuickTimeTrackHeaderDirectory.TagCreated, out var trackCreatedDate))
                {
                    if (trackCreatedDate.Year > 1970)
                    {
                        return trackCreatedDate;
                    }
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DateTime? GetXmpDate(XmpDirectory xmpDirectory)
    {
        // XMP-Namespaces für Datumsfelder
        string[] dateProperties =
        [
            "xmp:CreateDate",
            "xmp:DateCreated",
            "photoshop:DateCreated",
            "exif:DateTimeOriginal",
            "xmp:MetadataDate"
        ];

        foreach (var prop in dateProperties)
        {
            try
            {
                var ns = prop.Split(':')[0] switch
                {
                    "xmp" => "http://ns.adobe.com/xap/1.0/",
                    "photoshop" => "http://ns.adobe.com/photoshop/1.0/",
                    "exif" => "http://ns.adobe.com/exif/1.0/",
                    _ => null
                };

                if (ns == null) continue;

                var propName = prop.Split(':')[1];
                if (xmpDirectory.XmpMeta!.DoesPropertyExist(ns, propName))
                {
                    var value = xmpDirectory.XmpMeta.GetPropertyString(ns, propName);
                    if (!string.IsNullOrEmpty(value) && TryParseXmpDate(value, out var date))
                    {
                        return date;
                    }
                }
            }
            catch
            {
                // Ignoriere Fehler bei einzelnen Properties
            }
        }

        return null;
    }

    private static bool TryParseXmpDate(string value, out DateTime date)
    {
        // XMP Datumsformate: 2024-05-15T14:30:00, 2024-05-15T14:30:00+02:00, etc.
        string[] formats =
        [
            "yyyy-MM-ddTHH:mm:ss.fffzzz",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd"
        ];

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out date))
            {
                return true;
            }
        }

        // Fallback: Standard-Parsing
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool FilesAreIdentical(string file1, string file2)
    {
        var info1 = new FileInfo(file1);
        var info2 = new FileInfo(file2);

        // Schneller Vergleich: Dateigröße
        if (info1.Length != info2.Length)
        {
            return false;
        }

        // Hash-Vergleich für gleich große Dateien
        using var stream1 = File.OpenRead(file1);
        using var stream2 = File.OpenRead(file2);
        using var sha256 = SHA256.Create();

        var hash1 = sha256.ComputeHash(stream1);
        stream1.Position = 0;
        var hash2 = sha256.ComputeHash(stream2);

        return hash1.SequenceEqual(hash2);
    }
}
