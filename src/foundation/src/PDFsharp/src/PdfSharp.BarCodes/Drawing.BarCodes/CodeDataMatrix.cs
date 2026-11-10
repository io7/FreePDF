// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System;
#if GDI
using System.Drawing;
#endif
#if WPF
using System.Windows;
using System.Windows.Media;
#endif

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// A Data Matrix ECC 200 code (ISO/IEC 16022), drawn as vector shapes.
    /// </summary>
    /// <remarks>
    /// <para>Rows and columns give the symbol size, for example 26 x 26 or 12 x 36; 0 x 0 picks the smallest square
    /// size the text fits in. <see cref="CodeBase.Size"/> includes the <see cref="QuietZone"/>, in modules.</para>
    /// <para>Text is written in ASCII encodation (two digits per codeword; Latin-1 letters with Upper Shift; other
    /// text as UTF-8 marked with ECI 26), or in Base256 where the encoding asks for it. C40, Text, X12 and EDIFACT
    /// requests are written in ASCII encodation, which every reader decodes the same way. <see cref="Code128.Fnc1"/>
    /// writes FNC1, as GS1 DataMatrix needs first.</para>
    /// </remarks>
    public class CodeDataMatrix : MatrixCode
    {
        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix()
            : this("", "", 26, 26, 0, XSize.Empty)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, int length)
            : this(code, "", length, length, 0, XSize.Empty)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, int length, XSize size)
            : this(code, "", length, length, 0, size)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, DataMatrixEncoding dmEncoding, int length, XSize size)
            : this(code, CreateEncoding(dmEncoding, code.Length), length, length, 0, size)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, int rows, int columns)
            : this(code, "", rows, columns, 0, XSize.Empty)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, int rows, int columns, XSize size)
            : this(code, "", rows, columns, 0, size)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, DataMatrixEncoding dmEncoding, int rows, int columns, XSize size)
            : this(code, CreateEncoding(dmEncoding, code.Length), rows, columns, 0, size)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, int rows, int columns, int quietZone)
            : this(code, "", rows, columns, quietZone, XSize.Empty)
        {}

        /// <summary>
        /// Initializes a new instance of CodeDataMatrix.
        /// </summary>
        public CodeDataMatrix(string code, string encoding, int rows, int columns, int quietZone, XSize size)
            : base(code, encoding, rows, columns, size)
        {
            QuietZone = quietZone;
        }

        /// <summary>
        /// Sets the encoding of the DataMatrix.
        /// </summary>
        public void SetEncoding(DataMatrixEncoding dmEncoding)
        {
            Encoding = CreateEncoding(dmEncoding, Text.Length);
        }

        static string CreateEncoding(DataMatrixEncoding dmEncoding, int length)
        {
            string tempencoding = "";
            switch (dmEncoding)
            {
                case DataMatrixEncoding.Ascii:
                    tempencoding = new string('a', length);
                    break;
                case DataMatrixEncoding.C40:
                    tempencoding = new string('c', length);
                    break;
                case DataMatrixEncoding.Text:
                    tempencoding = new string('t', length);
                    break;
                case DataMatrixEncoding.X12:
                    tempencoding = new string('x', length);
                    break;
                case DataMatrixEncoding.EDIFACT:
                    tempencoding = new string('e', length);
                    break;
                case DataMatrixEncoding.Base256:
                    tempencoding = new string('b', length);
                    break;
            }
            return tempencoding;
        }

        /// <summary>
        /// Gets or sets the size of the Matrix¹ Quiet Zone.
        /// </summary>
        public int QuietZone
        {
            get => _quietZone;
            set => _quietZone = value;
        }

        int _quietZone;

        /// <summary>
        /// Encodes the text and returns the symbol's modules, finder and clock patterns included, without the
        /// quiet zone, indexed [row, column], true for dark.
        /// </summary>
        public bool[,] GetModules() => DataMatrixEncoder.Encode(Text, Encoding, Rows, Columns);

        /// <summary>
        /// Renders the matrix code: the dark modules of each row, merged into runs, as one filled path. With a quiet
        /// zone, the whole size is painted white first, as before.
        /// </summary>
        protected internal override void Render(XGraphics gfx, XBrush brush, XPoint position)
        {
            if (Size.IsEmpty || Size.Width <= 0 || Size.Height <= 0)
                throw new InvalidOperationException(BcgSR.EmptyBarCodeSize);

            bool[,] modules = GetModules();
            int rows = modules.GetLength(0);
            int columns = modules.GetLength(1);
            double moduleWidth = Size.Width / (columns + 2 * QuietZone);
            double moduleHeight = Size.Height / (rows + 2 * QuietZone);

            XPoint topLeft = position + CalcDistance(Anchor, AnchorType.TopLeft, Size);
            double left = topLeft.X + QuietZone * moduleWidth;
            double top = topLeft.Y + QuietZone * moduleHeight;
            var path = new XGraphicsPath { FillMode = XFillMode.Winding };
            for (int row = 0; row < rows; row++)
            {
                int column = 0;
                while (column < columns)
                {
                    if (!modules[row, column])
                    {
                        column++;
                        continue;
                    }
                    int start = column;
                    while (column < columns && modules[row, column])
                        column++;
                    path.AddRectangle(left + start * moduleWidth, top + row * moduleHeight, (column - start) * moduleWidth, moduleHeight);
                }
            }
            XGraphicsState state = gfx.Save();
            try
            {
                LinearCodeRenderer.ApplyDirection(gfx, Direction, position);
                if (QuietZone > 0)
                    gfx.DrawRectangle(XBrushes.White, topLeft.X, topLeft.Y, Size.Width, Size.Height);
                gfx.DrawPath(brush, path);
            }
            finally
            {
                gfx.Restore(state);
            }
        }

        /// <summary>
        /// Determines whether the specified string can be used as data in the DataMatrix.
        /// </summary>
        /// <param name="text">The code to be checked.</param>
        protected override void CheckCode(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            // Throws when the size is not an ECC 200 size or the text does not fit it.
            DataMatrixEncoder.Encode(text, Encoding ?? "", Rows, Columns);
        }
    }
}
