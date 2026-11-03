// FreePDF: typefaces from the fonts installed on the machine, for the Core build. MIT; see LICENSE.

using System.Text;
using Microsoft.Extensions.Logging;
using PdfSharp.Logging;

namespace PdfSharp.Fonts
{
    /// <summary>
    /// Finds typefaces among the fonts installed on the machine and in the folders added with
    /// <see cref="GlobalFontSettings.AddFontDirectory"/>. The Core build asks it last, when no custom font resolver is
    /// set and neither the Windows font table nor the fallback font resolver knows the family.
    /// Only fonts FreePDF can embed are used: TrueType outlines in a single-font file (.ttf or .otf), whose OS/2
    /// embedding permissions allow embedding a subset. A family that is not installed is replaced by a substitute
    /// when it is one of the common Microsoft or PostScript families: first a font with the same widths, such as
    /// Liberation Sans for Arial, then a common font of the same kind, such as DejaVu Sans.
    /// </summary>
    sealed class SystemFontResolver : IFontResolver
    {
        public static readonly SystemFontResolver Instance = new();

        const string FaceNamePrefix = "FreePDF.SystemFont:";

        readonly List<string> _addedDirectories = new();
        Dictionary<string, List<Face>>? _families;
        HashSet<string>? _notEmbeddableFamilies;

        sealed class Face
        {
            public string Path = "";
            public bool Bold;
            public bool Italic;
            public int Weight;
        }

        /// <summary>
        /// Adds a folder that is searched before the system's font folders. Called under the font management lock.
        /// </summary>
        public void AddDirectory(string path)
        {
            var fullPath = System.IO.Path.GetFullPath(path);
            if (_addedDirectories.Contains(fullPath))
                return;
            _addedDirectories.Add(fullPath);
            _families = null;
        }

        /// <summary>
        /// Forgets the added folders and the index. Called when the font management is reset.
        /// </summary>
        public void Reset()
        {
            _addedDirectories.Clear();
            _families = null;
            _notEmbeddableFamilies = null;
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var families = GetFamilies();
            foreach (var candidate in Candidates(familyName))
            {
                if (!families.TryGetValue(candidate, out var faces))
                    continue;
                var face = ChooseFace(faces, isBold, isItalic);
                if (!String.Equals(candidate, familyName, StringComparison.OrdinalIgnoreCase))
                {
                    PdfSharpLogHost.Logger.LogWarning("Font family '{familyName}' is not installed; FreePDF uses '{substitute}' instead.",
                        familyName, candidate);
                }
                return new FontResolverInfo(FaceNamePrefix + face.Path, isBold && !face.Bold, isItalic && !face.Italic);
            }
            return null;
        }

        public byte[]? GetFont(string faceName)
        {
            if (!faceName.StartsWith(FaceNamePrefix, StringComparison.Ordinal))
                return null;
            try
            {
                return File.ReadAllBytes(faceName.Substring(FaceNamePrefix.Length));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PdfSharpLogHost.Logger.LogError("FreePDF cannot read the font file '{faceName}': {message}", faceName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Describes why a family could not be found, for the exception FreePDF throws.
        /// </summary>
        public string DescribeMissingFamily(string familyName)
        {
            GetFamilies();
            if (_notEmbeddableFamilies!.Contains(familyName))
                return $"The font '{familyName}' is installed, but its licence does not allow embedding it in a PDF.";
            return $"No font named '{familyName}' is installed on this machine or in a folder added with GlobalFontSettings.AddFontDirectory.";
        }

        static IEnumerable<string> Candidates(string familyName)
        {
            yield return familyName;
            if (Substitutes.TryGetValue(familyName, out var substitutes))
            {
                foreach (var substitute in substitutes)
                    yield return substitute;
            }
        }

        /// <summary>
        /// Chooses the face that needs the least simulation: an exact match, then a face that lacks only italic, then
        /// one that lacks only bold, then the regular face. A face with a style that was not asked for comes last.
        /// </summary>
        static Face ChooseFace(List<Face> faces, bool isBold, bool isItalic)
        {
            Face best = faces[0];
            int bestScore = Int32.MaxValue;
            foreach (var face in faces)
            {
                int score = 0;
                if (face.Bold && !isBold)
                    score += 100;
                if (face.Italic && !isItalic)
                    score += 100;
                if (isBold && !face.Bold)
                    score += 20;
                if (isItalic && !face.Italic)
                    score += 10;
                int targetWeight = 400;
                if (isBold)
                    targetWeight = 700;
                score += Math.Abs(face.Weight - targetWeight) / 100;
                if (score < bestScore)
                {
                    best = face;
                    bestScore = score;
                }
            }
            return best;
        }

        Dictionary<string, List<Face>> GetFamilies()
        {
            if (_families != null)
                return _families;

            var families = new Dictionary<string, List<Face>>(StringComparer.OrdinalIgnoreCase);
            var notEmbeddable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new List<string>(_addedDirectories);
            directories.AddRange(SystemDirectories());
            foreach (var directory in directories)
            {
                foreach (var file in FontFiles(directory))
                    AddFont(file, families, notEmbeddable);
            }
            _notEmbeddableFamilies = notEmbeddable;
            _families = families;
            return families;
        }

        static void AddFont(string file, Dictionary<string, List<Face>> families, HashSet<string> notEmbeddable)
        {
            FontFileInfo? info;
            try
            {
                info = FontFileInfo.Read(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
            {
                return;
            }
            if (info == null)
                return;
            if (!info.Embeddable)
            {
                notEmbeddable.Add(info.Family);
                return;
            }

            if (!families.TryGetValue(info.Family, out var faces))
            {
                faces = new List<Face>();
                families.Add(info.Family, faces);
            }
            // The first folder wins: added folders, then the user's, then the system's.
            foreach (var existing in faces)
            {
                if (existing.Bold == info.Bold && existing.Italic == info.Italic)
                    return;
            }
            faces.Add(new Face { Path = file, Bold = info.Bold, Italic = info.Italic, Weight = info.Weight });
        }

        static IEnumerable<string> SystemDirectories()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Capabilities.OperatingSystem.IsWindows)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (localAppData.Length > 0)
                    yield return Path.Combine(localAppData, "Microsoft", "Windows", "Fonts");
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (windows.Length > 0)
                    yield return Path.Combine(windows, "Fonts");
                yield break;
            }
            if (Capabilities.OperatingSystem.IsOSX)
            {
                if (home.Length > 0)
                    yield return Path.Combine(home, "Library", "Fonts");
                yield return "/Library/Fonts";
                yield return "/System/Library/Fonts";
                yield break;
            }
            // Linux and other Unix systems: the XDG user folder, the legacy user folder, then the system folders.
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!String.IsNullOrEmpty(dataHome))
                yield return Path.Combine(dataHome, "fonts");
            else if (home.Length > 0)
                yield return Path.Combine(home, ".local", "share", "fonts");
            if (home.Length > 0)
                yield return Path.Combine(home, ".fonts");
            yield return "/usr/local/share/fonts";
            yield return "/usr/share/fonts";
        }

        /// <summary>
        /// Lists .ttf and .otf files below a folder in ordinal order, skipping folders that cannot be read.
        /// The depth limit also ends loops through symbolic links.
        /// </summary>
        static IEnumerable<string> FontFiles(string directory, int depth = 0)
        {
            if (depth > 8 || !Directory.Exists(directory))
                yield break;

            string[] files;
            string[] subdirectories;
            try
            {
                files = Directory.GetFiles(directory);
                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                yield break;
            }
            Array.Sort(files, StringComparer.Ordinal);
            Array.Sort(subdirectories, StringComparer.Ordinal);

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                    yield return file;
            }
            foreach (var subdirectory in subdirectories)
            {
                foreach (var file in FontFiles(subdirectory, depth + 1))
                    yield return file;
            }
        }

        static readonly string[] SansSerif =
            ["Liberation Sans", "Arimo", "Arial", "Helvetica", "FreeSans", "DejaVu Sans", "Noto Sans", "Bitstream Vera Sans"];
        static readonly string[] Serif =
            ["Liberation Serif", "Tinos", "Times New Roman", "Times", "FreeSerif", "DejaVu Serif", "Noto Serif", "Bitstream Vera Serif"];
        static readonly string[] Monospace =
            ["Liberation Mono", "Cousine", "Courier New", "Courier", "FreeMono", "DejaVu Sans Mono", "Noto Sans Mono", "Bitstream Vera Sans Mono"];

        /// <summary>
        /// Substitutes for common families, those with the same widths first (Liberation, Croscore, Carlito and
        /// Caladea, GNU FreeFont), then common fonts of the same kind.
        /// </summary>
        static readonly Dictionary<string, string[]> Substitutes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = SansSerif,
            ["Helvetica"] = SansSerif,
            ["Verdana"] = SansSerif,
            ["Tahoma"] = SansSerif,
            ["Segoe UI"] = SansSerif,
            ["Microsoft Sans Serif"] = SansSerif,
            ["sans-serif"] = SansSerif,
            ["Calibri"] = ["Carlito", .. SansSerif],
            ["Times New Roman"] = Serif,
            ["Times"] = Serif,
            ["Georgia"] = Serif,
            ["serif"] = Serif,
            ["Cambria"] = ["Caladea", .. Serif],
            ["Courier New"] = Monospace,
            ["Courier"] = Monospace,
            ["Consolas"] = Monospace,
            ["Lucida Console"] = Monospace,
            ["monospace"] = Monospace,
        };

        /// <summary>
        /// The names, style and embedding permission of a font file, read from its table directory and its name, OS/2
        /// and head tables without loading the whole file.
        /// </summary>
        sealed class FontFileInfo
        {
            public string Family = "";
            public bool Bold;
            public bool Italic;
            public int Weight = 400;
            public bool Embeddable = true;

            /// <summary>
            /// Returns null for a file FreePDF cannot use: a collection, CFF outlines or a damaged font.
            /// </summary>
            public static FontFileInfo? Read(string path)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var header = ReadBytes(stream, 0, 12);
                if (header == null)
                    return null;
                uint version = UInt32At(header, 0);
                // TrueType outlines only: 0x00010000 or 'true'. 'OTTO' (CFF) and 'ttcf' (collections) are not embeddable.
                if (version != 0x00010000 && version != 0x74727565)
                    return null;

                int tableCount = UInt16At(header, 4);
                var directory = ReadBytes(stream, 12, tableCount * 16);
                if (directory == null)
                    return null;
                var tables = new Dictionary<string, (long Offset, int Length)>(StringComparer.Ordinal);
                for (int index = 0; index < tableCount; index++)
                {
                    int record = index * 16;
                    var tag = Encoding.ASCII.GetString(directory, record, 4);
                    tables[tag] = (UInt32At(directory, record + 8), (int)Math.Min(UInt32At(directory, record + 12), Int32.MaxValue));
                }
                if (!tables.ContainsKey("glyf") || !tables.TryGetValue("name", out var nameTable))
                    return null;

                var info = new FontFileInfo();
                var name = ReadBytes(stream, nameTable.Offset, nameTable.Length);
                if (name == null)
                    return null;
                var family = FamilyName(name);
                if (String.IsNullOrWhiteSpace(family))
                    return null;
                info.Family = family!.Trim();

                if (tables.TryGetValue("OS/2", out var os2Table) && os2Table.Length >= 64)
                {
                    var os2 = ReadBytes(stream, os2Table.Offset, 64);
                    if (os2 == null)
                        return null;
                    info.Weight = UInt16At(os2, 4);
                    info.Embeddable = AllowsSubsetEmbedding(UInt16At(os2, 8));
                    int selection = UInt16At(os2, 62);
                    info.Italic = (selection & 0x0001) != 0;
                    info.Bold = (selection & 0x0020) != 0;
                }
                else if (tables.TryGetValue("head", out var headTable) && headTable.Length >= 46)
                {
                    var head = ReadBytes(stream, headTable.Offset, 46);
                    if (head == null)
                        return null;
                    int macStyle = UInt16At(head, 44);
                    info.Bold = (macStyle & 0x0001) != 0;
                    info.Italic = (macStyle & 0x0002) != 0;
                    if (info.Bold)
                        info.Weight = 700;
                }
                return info;
            }

            /// <summary>
            /// OS/2 fsType: restricted-licence (bit 1) without preview-and-print or editable permission, bitmap-only
            /// (bit 9) and no-subsetting (bit 8) fonts must not be embedded the way FreePDF embeds them.
            /// </summary>
            static bool AllowsSubsetEmbedding(int fsType)
            {
                if ((fsType & 0x0002) != 0 && (fsType & 0x000C) == 0)
                    return false;
                return (fsType & 0x0300) == 0;
            }

            /// <summary>
            /// The font family name (name ID 1): Windows Unicode US English first, then any Windows Unicode, then
            /// Unicode, then Macintosh Roman.
            /// </summary>
            static string? FamilyName(byte[] table)
            {
                if (table.Length < 6)
                    return null;
                int count = UInt16At(table, 2);
                int storage = UInt16At(table, 4);
                string? best = null;
                int bestRank = Int32.MaxValue;
                for (int index = 0; index < count; index++)
                {
                    int record = 6 + index * 12;
                    if (record + 12 > table.Length)
                        break;
                    if (UInt16At(table, record + 6) != 1)
                        continue;
                    int platform = UInt16At(table, record);
                    int encoding = UInt16At(table, record + 2);
                    int language = UInt16At(table, record + 4);
                    int rank = NameRank(platform, encoding, language);
                    if (rank >= bestRank)
                        continue;
                    int length = UInt16At(table, record + 8);
                    int offset = storage + UInt16At(table, record + 10);
                    if (offset + length > table.Length)
                        continue;
                    if (platform == 1)
                        best = Encoding.GetEncoding(28591).GetString(table, offset, length);
                    else
                        best = Encoding.BigEndianUnicode.GetString(table, offset, length);
                    bestRank = rank;
                }
                return best;
            }

            static int NameRank(int platform, int encoding, int language)
            {
                if (platform == 3 && (encoding == 1 || encoding == 10))
                {
                    if (language == 0x0409)
                        return 0;
                    return 1;
                }
                if (platform == 0)
                    return 2;
                if (platform == 1 && encoding == 0)
                    return 3;
                return Int32.MaxValue;
            }

            static byte[]? ReadBytes(Stream stream, long offset, int count)
            {
                if (offset < 0 || count < 0 || offset + count > stream.Length)
                    return null;
                stream.Position = offset;
                var buffer = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int chunk = stream.Read(buffer, read, count - read);
                    if (chunk == 0)
                        return null;
                    read += chunk;
                }
                return buffer;
            }

            static int UInt16At(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

            static uint UInt32At(byte[] data, int offset)
                => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
