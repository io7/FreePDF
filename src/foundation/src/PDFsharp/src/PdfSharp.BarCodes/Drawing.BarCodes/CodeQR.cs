// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// A QR code (ISO/IEC 18004, model 2), drawn as vector shapes so it stays sharp at any size.
    /// </summary>
    /// <remarks>
    /// <para><see cref="CodeBase.Size"/> is the whole symbol including the <see cref="QuietZone"/>. The symbol is
    /// square; when the size is not, the symbol is centred in it.</para>
    /// <para>Text of digits only is encoded in the numeric mode, text of digits, upper-case letters and
    /// <c> $%*+-./:</c> in the alphanumeric mode, and any other text in the byte mode: ASCII as is, everything
    /// else as UTF-8.</para>
    /// <para>Draw it with <c>XGraphics.DrawMatrixCode</c>, or in MigraDoc with a <c>Barcode</c> of type <c>QRCode</c>.</para>
    /// </remarks>
    public class CodeQR : MatrixCode
    {
        /// <summary>
        /// Initializes a new QR code for the text, with error correction level M.
        /// Set <see cref="CodeBase.Size"/> before drawing it.
        /// </summary>
        public CodeQR(string text)
            : this(text, XSize.Empty, QRErrorCorrectionLevel.M)
        { }

        /// <summary>
        /// Initializes a new QR code for the text and size, with error correction level M.
        /// </summary>
        public CodeQR(string text, XSize size)
            : this(text, size, QRErrorCorrectionLevel.M)
        { }

        /// <summary>
        /// Initializes a new QR code for the text, size and error correction level.
        /// </summary>
        public CodeQR(string text, XSize size, QRErrorCorrectionLevel errorCorrectionLevel)
            : base(text ?? throw new ArgumentNullException(nameof(text)), "", 0, 0, size)
        {
            ErrorCorrectionLevel = errorCorrectionLevel;
        }

        /// <summary>
        /// Gets or sets the error correction level. The default is M.
        /// </summary>
        public QRErrorCorrectionLevel ErrorCorrectionLevel
        {
            get => _errorCorrectionLevel;
            set
            {
                if (value is < QRErrorCorrectionLevel.L or > QRErrorCorrectionLevel.H)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _errorCorrectionLevel = value;
            }
        }
        QRErrorCorrectionLevel _errorCorrectionLevel;

        /// <summary>
        /// Gets or sets the version (1 to 40), which sets the number of modules: 21 for version 1, 4 more for each
        /// further version. 0, the default, picks the smallest version the text fits in. Drawing throws when the
        /// text does not fit the version set here.
        /// </summary>
        public int Version
        {
            get => _version;
            set
            {
                if (value is < 0 or > QREncoder.MaxVersion)
                    throw new ArgumentOutOfRangeException(nameof(value), BcgSR.QRVersionOutOfRange);
                _version = value;
            }
        }
        int _version;

        /// <summary>
        /// Gets or sets the width of the light margin around the symbol, in modules. The standard asks for 4,
        /// the default. Set it to 0 when the surrounding layout already leaves that space light.
        /// The quiet zone is not painted: it shows whatever lies beneath, which should be light.
        /// </summary>
        public int QuietZone
        {
            get => _quietZone;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), BcgSR.QRQuietZoneNegative);
                _quietZone = value;
            }
        }
        int _quietZone = 4;

        /// <summary>
        /// Gets or sets whether text that is not ASCII is marked as UTF-8 with an ECI (extended channel
        /// interpretation) designator, as the standard asks. The default is true. Some payment and banking apps
        /// expect UTF-8 without the marker; set it to false for them.
        /// </summary>
        public bool Utf8Eci { get; set; } = true;

        /// <summary>
        /// Encodes the text and returns the symbol's modules without the quiet zone,
        /// indexed [row, column], true for dark. The array is a new copy on every call.
        /// </summary>
        public bool[,] GetModules()
            => QREncoder.Encode(Text, ErrorCorrectionLevel, Version, Utf8Eci);

        /// <summary>
        /// Renders the QR code: the dark modules of each row, merged into runs, as one filled path.
        /// </summary>
        protected internal override void Render(XGraphics gfx, XBrush brush, XPoint position)
        {
            if (Size.IsEmpty || Size.Width <= 0 || Size.Height <= 0)
                throw new InvalidOperationException(BcgSR.EmptyBarCodeSize);

            bool[,] modules = GetModules();
            int count = modules.GetLength(0);
            double side = Math.Min(Size.Width, Size.Height);
            double module = side / (count + 2 * QuietZone);

            XGraphicsState state = gfx.Save();
            try
            {
                LinearCodeRenderer.ApplyDirection(gfx, Direction, position);
                DrawModules(gfx, brush, position, modules, side, module);
            }
            finally
            {
                gfx.Restore(state);
            }
        }

        void DrawModules(XGraphics gfx, XBrush brush, XPoint position, bool[,] modules, double side, double module)
        {
            int count = modules.GetLength(0);
            XPoint topLeft = position + CalcDistance(Anchor, AnchorType.TopLeft, Size);
            double left = topLeft.X + (Size.Width - side) / 2 + QuietZone * module;
            double top = topLeft.Y + (Size.Height - side) / 2 + QuietZone * module;

            // One path for the whole symbol, so viewers do not show seams between neighbouring modules.
            var path = new XGraphicsPath { FillMode = XFillMode.Winding };
            for (int row = 0; row < count; row++)
            {
                int column = 0;
                while (column < count)
                {
                    if (!modules[row, column])
                    {
                        column++;
                        continue;
                    }
                    int start = column;
                    while (column < count && modules[row, column])
                        column++;
                    path.AddRectangle(left + start * module, top + row * module, (column - start) * module, module);
                }
            }
            gfx.DrawPath(brush, path);
        }

        /// <summary>
        /// Any text that is not empty can be encoded; its length is checked when the code is drawn.
        /// </summary>
        protected override void CheckCode(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            if (text.Length == 0)
                throw new ArgumentException(BcgSR.QREmptyText, nameof(text));
        }
    }
}
