// FreePDF - MIT, see the LICENSE file in the solution root.

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.BarCodes;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Renders a barcode shape with the PDFsharp bar code classes. The code fills the shape inside its border line.
    /// </summary>
    sealed class BarcodeRenderer : ShapeRenderer
    {
        internal BarcodeRenderer(XGraphics gfx, Barcode barcode, FieldInfos? fieldInfos)
            : base(gfx, barcode, fieldInfos)
        {
            _barcode = barcode;
            _renderInfo = new BarcodeRenderInfo { DocumentObject = _shape };
        }

        internal BarcodeRenderer(XGraphics gfx, RenderInfo renderInfo, FieldInfos? fieldInfos)
            : base(gfx, renderInfo, fieldInfos)
        {
            _barcode = (Barcode)renderInfo.DocumentObject;
        }

        internal override void Render() => RenderPicture(RenderBarcode);

        void RenderBarcode()
        {
            RenderFilling();

            // The content area is the shape plus one line width; the border is stroked on the shape's edge, so its
            // inner edge lies half a line width inside the area, as the filling does.
            var area = _renderInfo.LayoutInfo.ContentArea;
            double lineWidth = _lineFormatRenderer.GetWidth();
            var box = new XRect(area.X + lineWidth / 2, area.Y + lineWidth / 2, area.Width - 2 * lineWidth, area.Height - 2 * lineWidth);
            if (box.Width > 0 && box.Height > 0)
                DrawCode(box);

            RenderLine();
        }

        void DrawCode(XRect box)
        {
            var direction = _barcode.Orientation switch
            {
                TextOrientation.Upward => CodeDirection.BottomToTop,
                TextOrientation.Downward or TextOrientation.Vertical or TextOrientation.VerticalFarEast => CodeDirection.TopToBottom,
                _ => CodeDirection.LeftToRight,
            };

            // A vertical code is drawn along its own x axis and rotated about its start point.
            bool vertical = direction != CodeDirection.LeftToRight;
            var size = vertical ? new XSize(box.Height, box.Width) : new XSize(box.Width, box.Height);
            var position = direction switch
            {
                CodeDirection.BottomToTop => new XPoint(box.X, box.Y + box.Height),
                CodeDirection.TopToBottom => new XPoint(box.X + box.Width, box.Y),
                _ => new XPoint(box.X, box.Y),
            };

            string code = _barcode.Code;
            if (_barcode.Type == BarcodeType.QRCode)
            {
                _gfx.DrawMatrixCode(new CodeQR(code, size) { Direction = direction }, XBrushes.Black, position);
                return;
            }
            if (_barcode.Type == BarcodeType.DataMatrix)
            {
                // A square symbol, centred in the shape so its modules stay square.
                double side = Math.Min(size.Width, size.Height);
                var square = new XSize(side, side);
                var offset = new XPoint((size.Width - side) / 2, (size.Height - side) / 2);
                var start = direction switch
                {
                    CodeDirection.BottomToTop => new XPoint(position.X + offset.Y, position.Y - offset.X),
                    CodeDirection.TopToBottom => new XPoint(position.X - offset.Y, position.Y + offset.X),
                    _ => new XPoint(position.X + offset.X, position.Y + offset.Y),
                };
                var dataMatrix = new CodeDataMatrix(code, "", 0, 0, 1, square) { Direction = direction };
                _gfx.DrawMatrixCode(dataMatrix, XBrushes.Black, start);
                return;
            }

            BarCode barCode = _barcode.Type switch
            {
                BarcodeType.Barcode25i => new Code2of5Interleaved(code, size, direction),
                BarcodeType.Barcode39 => new Code3of9Standard(code, size, direction),
                BarcodeType.Barcode128 => new Code128(code, size, direction),
                BarcodeType.Ean13 => new Ean13(code, size) { Direction = direction },
                BarcodeType.Ean8 => new Ean8(code, size) { Direction = direction },
                BarcodeType.UpcA => new UpcA(code, size) { Direction = direction },
                _ => throw new ArgumentException($"Unknown barcode type {(int)_barcode.Type}."),
            };

            // LineRatio is the wide-to-narrow ratio of the thick/thin codes; they accept 2 to 3.
            double ratio = _barcode.LineRatio;
            if (ratio is >= 2 and <= 3 && _barcode.Type is BarcodeType.Barcode25i or BarcodeType.Barcode39)
                barCode.WideNarrowRatio = ratio;

            // EAN and UPC sizes include their quiet zones. For the other linear codes the shape keeps a quiet zone
            // of 10 narrow bars on each side, so a border or neighbouring content does not touch the bars.
            double units = _barcode.Type switch
            {
                BarcodeType.Barcode25i => 6 + barCode.WideNarrowRatio + (2 * barCode.WideNarrowRatio + 3) * code.Length,
                BarcodeType.Barcode39 => 13 + 6 * barCode.WideNarrowRatio + (3 * barCode.WideNarrowRatio + 7) * code.Length,
                BarcodeType.Barcode128 => ((Code128)barCode).GetModules().Length,
                _ => 0,
            };
            if (units > 0)
            {
                double quietZone = 10 * size.Width / (units + 20);
                size = new XSize(size.Width - 2 * quietZone, size.Height);
                barCode.Size = size;
                position = direction switch
                {
                    CodeDirection.BottomToTop => new XPoint(position.X, position.Y - quietZone),
                    CodeDirection.TopToBottom => new XPoint(position.X, position.Y + quietZone),
                    _ => new XPoint(position.X + quietZone, position.Y),
                };
            }

            bool eanUpc = _barcode.Type is BarcodeType.Ean13 or BarcodeType.Ean8 or BarcodeType.UpcA;
            if (!_barcode.Text)
            {
                barCode.TextLocation = TextLocation.None;
                _gfx.DrawBarCode(barCode, XBrushes.Black, position);
                return;
            }

            // The digits of EAN and UPC are about 9 modules high; other codes put their text in the lowest sixth.
            barCode.TextLocation = eanUpc ? TextLocation.Below : TextLocation.BelowEmbedded;
            double moduleWidth = _barcode.Type == BarcodeType.Ean8 ? size.Width / 81 : size.Width / 113;
            double emSize = eanUpc ? 11 * moduleWidth : size.Height / 6;
            _gfx.DrawBarCode(barCode, XBrushes.Black, new XFont(TextFontName(), emSize), position);
        }

        /// <summary>
        /// The text under the bars uses the font of the Normal style, which the application's font resolver knows.
        /// </summary>
        string TextFontName()
        {
            string? name = _barcode.Document?.Styles.Normal.Font.Name;
            return String.IsNullOrEmpty(name) ? "Courier New" : name!;
        }

        readonly Barcode _barcode;
    }

    sealed class BarcodeRenderInfo : ShapeRenderInfo
    {
        public override FormatInfo FormatInfo
        {
            get => _formatInfo ??= new ShapeFormatInfo();
            internal set => _formatInfo = (ShapeFormatInfo)value;
        }

        ShapeFormatInfo? _formatInfo;
    }
}
