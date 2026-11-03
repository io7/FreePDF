# Regenerates Pdf.Forms/StandardFontWidths.cs: the advance widths of the WinAnsiEncoding characters 32-255 in the
# twelve Latin standard fonts (Helvetica, Times, Courier and their styles). The widths come from Adobe's Core14 AFM
# files as distributed with Apache PDFBox 3.0.3; glyph names are matched to characters through the Adobe Glyph List.
# Every download is pinned by SHA-256. The generated file carries Adobe's notices, as the AFM licence requires, and
# src/packages/Adobe-Core14-AFM.txt carries them into every package under THIRD-PARTY/.
param(
    [string]$OutputFile = "$PSScriptRoot/../src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Forms/StandardFontWidths.cs",
    [string]$NoticeFile = "$PSScriptRoot/../src/packages/Adobe-Core14-AFM.txt"
)
$ErrorActionPreference = 'Stop'

$afmBase = 'https://raw.githubusercontent.com/apache/pdfbox/3.0.3/pdfbox/src/main/resources/org/apache/pdfbox/resources/afm'
$fonts = [ordered]@{
    'Helvetica'             = '5654761eab868ccb3080122accf13b4fb79edf37af09e1c57a881c35c334ee03'
    'Helvetica-Bold'        = '59ac93091bc7e76eb6baf60210526a4df88fe107dd5e13eb0ae70c9fc6d91fb9'
    'Helvetica-Oblique'     = '8c00ac46ca5963df22d4fc1153ea1246c8778475820378640c8d54d4fd3a46fe'
    'Helvetica-BoldOblique' = '1f848276efd76c0e5020ef98ccb459005f5a022f56cb8579e52c98e33ffab03b'
    'Times-Roman'           = 'b6dbde33196ff61f051f8468fb81a0dece63997a80b7fba058e8295f49480026'
    'Times-Bold'            = '37efa084183acfb06f45ad73bd75f6dd09b20d1e498c66ce1b9aa72b00db4ac7'
    'Times-Italic'          = '2f34d3bc49530532daa690aec165b68368162d4c27d24fbe6a3fb24aae49a74a'
    'Times-BoldItalic'      = '1bf83cee5300d4de1ce90681a2a2d70fd1876c664e91c1c4c6b6b5b12873b6da'
    'Courier'               = 'e72833dda79161e3aaf385adfeb59c61c89a4a3e184f27d21c178056eea30e17'
    'Courier-Bold'          = '1e86f0a949e316c08df32adc2fc2e2a92ed9902ffd54e3516e5a66a32db856da'
    'Courier-Oblique'       = '9e3494b4abb127a6586c095a377924514dd6956973f8f3b0bd7d6d00fcc89ea6'
    'Courier-BoldOblique'   = 'e1349f5f306b16338972eaf7f43d7913535cc075a15189b5f3efc841aee3d02e'
}
$mustRead = @{ Url = "$afmBase/MustRead.html"; Sha256 = 'b226bfc00e1b8b8a80c7b3cfbbc322d13b4b0401f94cdeafdd93b2210ad802eb' }
$glyphList = @{
    Url    = 'https://raw.githubusercontent.com/adobe-type-tools/agl-aglfn/4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6/glyphlist.txt'
    Sha256 = 'a3b2f61ced9f3644cc0d4ecde5c59df34ca286c689d9484a43a710a81c466789'
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("freepdf-afm-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
function Get-Pinned([string]$Url, [string]$Sha256) {
    $file = Join-Path $work ([IO.Path]::GetFileName($Url))
    Invoke-WebRequest $Url -OutFile $file
    if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $Sha256.ToUpperInvariant()) { throw "Hash mismatch: $Url" }
    return [IO.File]::ReadAllText($file, [Text.Encoding]::Latin1)
}

try {
    # Glyph name for each Unicode scalar value (single code point entries only).
    $namesByCode = @{}
    foreach ($line in (Get-Pinned $glyphList.Url $glyphList.Sha256) -split "`n") {
        if ($line -match '^([A-Za-z0-9_.]+);([0-9A-F]{4})\s*$') {
            $code = [Convert]::ToInt32($Matches[2], 16)
            if (-not $namesByCode.ContainsKey($code)) { $namesByCode[$code] = [Collections.Generic.List[string]]::new() }
            $namesByCode[$code].Add($Matches[1])
        }
    }

    [Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
    $winAnsi = [Text.Encoding]::GetEncoding(1252)

    $notice = (Get-Pinned $mustRead.Url $mustRead.Sha256) -replace '\s+', ' '
    if ($notice -notmatch '(This file and the 14 PostScript\(R\) AFM files .*? support the use of the AFM files\.)') { throw 'MustRead paragraph not found.' }
    $paragraph = $Matches[1]

    $notices = [Collections.Generic.List[string]]::new()
    $tables = [Text.StringBuilder]::new()
    foreach ($font in $fonts.Keys) {
        $afm = Get-Pinned "$afmBase/$font.afm" $fonts[$font]
        $widthByName = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
        foreach ($line in $afm -split "`n") {
            if ($line -match '^(?:Comment |Notice )(Copyright .+?)\s*$' -and -not $notices.Contains($Matches[1])) { $notices.Add($Matches[1]) }
            if ($line -match '^C -?\d+ ; WX (\d+) ; N ([^ ;]+) ;') { $widthByName[$Matches[2]] = [int]$Matches[1] }
        }
        $widths = foreach ($code in 32..255) {
            $character = [int][char]$winAnsi.GetString([byte[]]@($code))[0]
            $width = 0
            # ISO 32000-1 Annex D: WinAnsiEncoding also draws the space glyph at 240 octal and the hyphen at 255 octal.
            $glyphNames = @($namesByCode[$character])
            if ($code -eq 0xA0) { $glyphNames += 'space' }
            if ($code -eq 0xAD) { $glyphNames += 'hyphen' }
            foreach ($name in $glyphNames) {
                if ($widthByName.ContainsKey($name)) { $width = $widthByName[$name]; break }
            }
            $width
        }
        [void]$tables.AppendLine("            [`"$font`"] = new short[] { $($widths -join ', ') },")
    }

    $header = @(
        '// <auto-generated>'
        '// Generated by eng/Update-StandardFontWidths.ps1; do not edit. Regenerate with that script.'
        '//'
        '// The widths below are taken from Adobe''s Core14 AFM files, as distributed with Apache PDFBox 3.0.3.'
        '// Modified by FreePDF: reduced to the advance widths of the WinAnsiEncoding characters 32 to 255, matched to'
        '// glyph names through the Adobe Glyph List; kerning, bounding boxes and the other metrics are left out.'
        '//'
        '// Notices from the AFM files:'
    ) + ($notices | ForEach-Object { "//   $_" }) + @(
        '//'
        '// From the MustRead.html file that accompanies the AFM files:'
        "//   $paragraph"
        '// </auto-generated>'
    )
    $source = ($header -join "`n") + @"


namespace PdfSharp.Pdf.Forms
{
    /// <summary>
    /// Advance widths in 1/1000 em of the WinAnsiEncoding characters 32 to 255 in the twelve Latin standard fonts.
    /// </summary>
    static class StandardFontWidths
    {
        /// <summary>
        /// Gets the widths of the standard font with the specified PostScript name, indexed by character code - 32,
        /// or null if it is not one of the twelve Latin standard fonts.
        /// </summary>
        public static short[]? Get(string fontName)
        {
            Widths.TryGetValue(fontName, out var widths);
            return widths;
        }

        static readonly Dictionary<string, short[]> Widths = new()
        {
$($tables.ToString().TrimEnd())
        };
    }
}

"@
    [IO.File]::WriteAllText($OutputFile, $source.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $OutputFile"

    $packageNotice = @(
        'Adobe Core14 font metrics (AFM files)'
        ''
        'FreePDF contains the advance widths of the WinAnsiEncoding characters in the twelve Latin standard fonts'
        '(Helvetica, Times and Courier with their bold and italic styles), taken from Adobe''s Core14 AFM files as'
        'distributed with Apache PDFBox 3.0.3. Modified by FreePDF: only those widths are kept; kerning, bounding'
        'boxes and the other metrics are left out. They are compiled into the PdfSharp assembly as'
        'PdfSharp.Pdf.Forms.StandardFontWidths.'
        ''
        'Notices from the AFM files:'
    ) + ($notices | ForEach-Object { "  $_" }) + @(
        ''
        'From the MustRead.html file that accompanies the AFM files:'
        ''
        $paragraph
    )
    [IO.File]::WriteAllText($NoticeFile, ($packageNotice -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $NoticeFile"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
