using Metdatwip.Core.Models;

namespace Metdatwip.Core.Writers;

/// <summary>
/// Generates realistic, randomized metadata tags for images and documents.
/// </summary>
public static class MetadataRandomizer
{
    private static readonly Random Rnd = new();

    private static readonly (string Make, string Model, string Lens)[] CameraPresets =
    [
        ("Canon", "Canon EOS 5D Mark IV", "EF 24-70mm f/2.8L II USM"),
        ("Canon", "Canon EOS R6", "RF 50mm F1.2 L USM"),
        ("Nikon", "Nikon D850", "AF-S NIKKOR 24-70mm f/2.8E ED VR"),
        ("Nikon", "Nikon Z7 II", "NIKKOR Z 85mm f/1.8 S"),
        ("Sony", "ILCE-7RM4", "FE 24-70mm F2.8 GM II"),
        ("Sony", "ILCE-7M3", "FE 85mm F1.4 GM"),
        ("Apple", "iPhone 14 Pro", "iPhone 14 Pro back camera 6.86mm f/1.78"),
        ("Apple", "iPhone 15 Pro Max", "iPhone 15 Pro Max back camera 6.86mm f/1.78"),
        ("Samsung", "Galaxy S23 Ultra", "Galaxy S23 Ultra Rear Main Camera"),
        ("FUJIFILM", "X-T4", "XF16-55mmF2.8 R LM WR"),
    ];

    private static readonly string[] Artists =
    [
        "Alex Vance",
        "Elena Rostova",
        "Marcus Sterling",
        "Sarah Jenkins",
        "David K. Miller",
        "Hiroshi Tanaka",
        "Claire Dubois",
        "Jordan Reed",
    ];

    private static readonly string[] SoftwarePresets =
    [
        "Adobe Photoshop 25.1 (Windows)",
        "Adobe Lightroom Classic 13.0 (Macintosh)",
        "GIMP 2.10.36",
        "Capture One 23 Pro",
        "iOS 17.4.1",
        "Android 14.0",
    ];

    private static readonly string[] Descriptions =
    [
        "Landscape photograph taken during sunset",
        "High resolution portrait capture",
        "Urban architecture and street perspective",
        "Document scan and digital archive sample",
        "Studio lighting product shot",
        "Natural outdoor scenery",
    ];

    /// <summary>
    /// Generates a set of realistic randomized <see cref="MetadataEdit"/> fields for an image file.
    /// </summary>
    public static List<MetadataEdit> GenerateImageEdits()
    {
        var camera = GetRandom(CameraPresets);
        var artist = GetRandom(Artists);
        var software = GetRandom(SoftwarePresets);
        var description = GetRandom(Descriptions);
        var year = Rnd.Next(2022, 2026);
        var month = Rnd.Next(1, 13);
        var day = Rnd.Next(1, 28);
        var hour = Rnd.Next(8, 20);
        var minute = Rnd.Next(10, 60);
        var second = Rnd.Next(10, 60);

        var dateStr = $"{year}:{month:D2}:{day:D2} {hour:D2}:{minute:D2}:{second:D2}";
        var copyrightStr = $"Copyright {year} {artist}. All rights reserved.";

        return
        [
            new MetadataEdit("EXIF", "Make", camera.Make),
            new MetadataEdit("EXIF", "Model", camera.Model),
            new MetadataEdit("EXIF", "Lens Model", camera.Lens),
            new MetadataEdit("EXIF", "Artist", artist),
            new MetadataEdit("EXIF", "Software", software),
            new MetadataEdit("EXIF", "Image Description", description),
            new MetadataEdit("EXIF", "Date/Time Original", dateStr),
            new MetadataEdit("EXIF", "Date/Time", dateStr),
            new MetadataEdit("EXIF", "Copyright", copyrightStr),
        ];
    }

    /// <summary>
    /// Generates a set of realistic randomized <see cref="MetadataEdit"/> fields for an OOXML document.
    /// </summary>
    public static List<MetadataEdit> GenerateOoxmlEdits()
    {
        var author = GetRandom(Artists);
        var editor = GetRandom(Artists);
        var year = Rnd.Next(2023, 2026);

        return
        [
            new MetadataEdit("OOXML-Core", "creator", author),
            new MetadataEdit("OOXML-Core", "lastModifiedBy", editor),
            new MetadataEdit("OOXML-Core", "title", "Official Project Documentation"),
            new MetadataEdit("OOXML-App", "Company", "Enterprise Technologies Corp."),
            new MetadataEdit("OOXML-App", "Application", "Microsoft Office Word 16.0"),
        ];
    }

    /// <summary>
    /// Generates a set of realistic randomized <see cref="MetadataEdit"/> fields for an audio file (MP3, WAV).
    /// </summary>
    public static List<MetadataEdit> GenerateAudioEdits()
    {
        var artist = GetRandom(Artists);
        var year = Rnd.Next(2020, 2026);

        return
        [
            new MetadataEdit("ID3v2", "Title", "Midnight Horizon"),
            new MetadataEdit("ID3v2", "Artist", artist),
            new MetadataEdit("ID3v2", "Album", "Acoustic Waves Sessions"),
            new MetadataEdit("ID3v2", "Year", year.ToString()),
            new MetadataEdit("ID3v2", "Genre", "Ambient / Lo-Fi"),
            new MetadataEdit("ID3v2", "Software", "Logic Pro X 10.8"),
            new MetadataEdit("ID3v2", "Copyright", $"Copyright {year} {artist}. All rights reserved."),
        ];
    }

    /// <summary>
    /// Generates a set of realistic randomized <see cref="MetadataEdit"/> fields for a video file (MP4, MOV, MKV, WEBM).
    /// </summary>
    public static List<MetadataEdit> GenerateVideoEdits()
    {
        var director = GetRandom(Artists);
        var year = Rnd.Next(2021, 2026);

        return
        [
            new MetadataEdit("MP4-Metadata", "Title", "Cinematic Motion Reel"),
            new MetadataEdit("MP4-Metadata", "Artist", director),
            new MetadataEdit("MP4-Metadata", "Album", "4K Ultra HD Collection"),
            new MetadataEdit("MP4-Metadata", "Year", year.ToString()),
            new MetadataEdit("MP4-Metadata", "Software", "DaVinci Resolve 18.6 (Macintosh)"),
            new MetadataEdit("MP4-Metadata", "Comment", "Color graded in Rec.709 color space"),
            new MetadataEdit("MP4-Metadata", "Copyright", $"Copyright {year} {director}. All rights reserved."),
        ];
    }

    private static T GetRandom<T>(T[] array) => array[Rnd.Next(array.Length)];
}
