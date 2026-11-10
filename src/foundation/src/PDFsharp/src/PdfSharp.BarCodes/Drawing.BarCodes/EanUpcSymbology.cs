// FreePDF - MIT, see the LICENSE file in the solution root.

namespace PdfSharp.Drawing.BarCodes
{
    /// <summary>
    /// The EAN-13, EAN-8 and UPC-A symbologies (ISO/IEC 15420): check digits, modules and the drawing with
    /// human-readable digits under the bars.
    /// </summary>
    static class EanUpcSymbology
    {
        internal enum Kind
        {
            Ean13,
            Ean8,
            UpcA,
        }

        // Set A (odd parity) patterns of the digits 0 to 9; each is 7 modules: space, bar, space, bar.
        static readonly string[] SetA =
            ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];

        // Which of the six left-hand digits of an EAN-13 use set B (even parity), by the leading digit.
        static readonly string[] Ean13Parity =
            ["AAAAAA", "AABABB", "AABBAB", "AABBBA", "ABAABB", "ABBAAB", "ABBBAA", "ABABAB", "ABABBA", "ABBABA"];

        /// <summary>
        /// Number of digits without the check digit.
        /// </summary>
        static int DataDigits(Kind kind) => kind switch
        {
            Kind.Ean13 => 12,
            Kind.Ean8 => 7,
            _ => 11,
        };

        /// <summary>
        /// The quiet zones the drawing reserves left and right of the bars, in modules.
        /// </summary>
        internal static (int Left, int Right) QuietZones(Kind kind) => kind switch
        {
            Kind.Ean13 => (11, 7),
            Kind.Ean8 => (7, 7),
            _ => (9, 9),
        };

        static string Name(Kind kind) => kind switch
        {
            Kind.Ean13 => "EAN-13",
            Kind.Ean8 => "EAN-8",
            _ => "UPC-A",
        };

        /// <summary>
        /// Checks that the text is all digits and of the right length, with or without the check digit,
        /// and that a given check digit is right.
        /// </summary>
        internal static void Check(string text, Kind kind)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            int data = DataDigits(kind);
            if ((text.Length != data && text.Length != data + 1) || text.Any(ch => ch is < '0' or > '9'))
                throw new ArgumentException(BcgSR.InvalidEanUpcCode(text, Name(kind), data));
            if (text.Length == data + 1 && text[data] != CheckDigit(text.Substring(0, data)))
                throw new ArgumentException(BcgSR.WrongEanUpcCheckDigit(text, Name(kind), CheckDigit(text.Substring(0, data))));
        }

        /// <summary>
        /// The code with its check digit.
        /// </summary>
        internal static string Complete(string text, Kind kind)
            => text.Length == DataDigits(kind) ? text + CheckDigit(text) : text;

        /// <summary>
        /// The GS1 check digit: weights 3 and 1 alternately from the rightmost data digit, rounded up to ten.
        /// </summary>
        internal static char CheckDigit(string digits)
        {
            int sum = 0;
            for (int i = 0; i < digits.Length; i++)
            {
                int weight = (digits.Length - i) % 2 == 1 ? 3 : 1;
                sum += (digits[i] - '0') * weight;
            }
            return (char)('0' + (10 - sum % 10) % 10);
        }

        /// <summary>
        /// The modules from the start guard to the end guard, true for bar.
        /// </summary>
        internal static bool[] Modules(string code, Kind kind)
        {
            // UPC-A is an EAN-13 with a leading zero.
            if (kind == Kind.UpcA)
                code = "0" + code;

            var modules = new List<bool>(95);
            Append(modules, "101");
            if (kind == Kind.Ean8)
            {
                for (int i = 0; i < 4; i++)
                    Append(modules, SetA[code[i] - '0']);
                Append(modules, "01010");
                for (int i = 4; i < 8; i++)
                    Append(modules, SetC(code[i]));
            }
            else
            {
                string parity = Ean13Parity[code[0] - '0'];
                for (int i = 1; i < 7; i++)
                    Append(modules, parity[i - 1] == 'A' ? SetA[code[i] - '0'] : SetB(code[i]));
                Append(modules, "01010");
                for (int i = 7; i < 13; i++)
                    Append(modules, SetC(code[i]));
            }
            Append(modules, "101");
            return modules.ToArray();
        }

        // Set C (right-hand) patterns are set A with bars and spaces swapped; set B patterns are set C reversed.
        static string SetC(char digit) => new(SetA[digit - '0'].Select(ch => ch == '0' ? '1' : '0').ToArray());

        static string SetB(char digit) => new(SetC(digit).Reverse().ToArray());

        static void Append(List<bool> modules, string pattern)
        {
            foreach (char ch in pattern)
                modules.Add(ch == '1');
        }

        /// <summary>
        /// Whether the bar module at this index reaches down into the text line: the guard bars and,
        /// for UPC-A, the first and last digit.
        /// </summary>
        static bool IsLong(int index, Kind kind)
        {
            int length = kind == Kind.Ean8 ? 67 : 95;
            int center = length / 2;
            bool guard = index < 3 || index >= length - 3 || (index >= center - 2 && index <= center + 2);
            bool upcOuterDigit = kind == Kind.UpcA && (index < 10 || index >= length - 10);
            return guard || upcOuterDigit;
        }

        /// <summary>
        /// Draws the symbol. The size spans the bars and the quiet zones; the digits go under the bars unless
        /// the text location is None.
        /// </summary>
        internal static void Render(BarCode barCode, Kind kind, XGraphics gfx, XBrush brush, XFont? font, XPoint position)
        {
            XSize size = barCode.Size;
            if (size.IsEmpty || size.Width <= 0 || size.Height <= 0)
                throw new InvalidOperationException(BcgSR.EmptyBarCodeSize);

            string code = Complete(barCode.Text, kind);
            bool[] modules = Modules(code, kind);
            var (quietLeft, quietRight) = QuietZones(kind);
            double x = size.Width / (quietLeft + modules.Length + quietRight);

            XPoint topLeft = position + CodeBase.CalcDistance(barCode.Anchor, AnchorType.TopLeft, size);
            double barsLeft = topLeft.X + quietLeft * x;

            if (barCode.TextLocation == TextLocation.None)
            {
                XGraphicsState plain = gfx.Save();
                try
                {
                    LinearCodeRenderer.ApplyDirection(gfx, barCode.Direction, position);
                    LinearCodeRenderer.DrawBars(gfx, brush, modules, barsLeft, topLeft.Y, x, _ => size.Height);
                }
                finally
                {
                    gfx.Restore(plain);
                }
                return;
            }

            // Everything that can throw comes before the graphics state is saved.
            // Digits about 9 modules high; Courier's advance (0.6 em) then fits a 7-module digit slot.
            // UPC-A prints its first and last digit smaller, beside the bars.
            XFont digitFont = LinearCodeRenderer.TextFont(font, 11 * x);
            XFont outerFont = LinearCodeRenderer.TextFont(font, 8 * x);
            double textHeight = gfx.MeasureString("0", digitFont).Height;
            double barBottom = size.Height - textHeight;
            double longBottom = Math.Min(barBottom + 5 * x, size.Height);
            if (barBottom <= 0)
                throw new InvalidOperationException(BcgSR.BarsTooLow);

            XGraphicsState state = gfx.Save();
            try
            {
                LinearCodeRenderer.ApplyDirection(gfx, barCode.Direction, position);
                LinearCodeRenderer.DrawBars(gfx, brush, modules, barsLeft, topLeft.Y, x,
                    index => IsLong(index, kind) ? longBottom : barBottom);
                DrawDigits(gfx, brush, kind, code, barsLeft, topLeft.Y + barBottom, x, textHeight, digitFont, outerFont);
            }
            finally
            {
                gfx.Restore(state);
            }
        }

        static void DrawDigits(XGraphics gfx, XBrush brush, Kind kind, string code, double barsLeft, double textTop,
            double x, double textHeight, XFont digitFont, XFont outerFont)
        {
            void Digit(char digit, double slotLeftModule, XFont digitOrOuterFont)
                => gfx.DrawString(digit.ToString(), digitOrOuterFont, brush,
                    new XRect(barsLeft + slotLeftModule * x, textTop, 7 * x, textHeight), XStringFormats.TopCenter);

            switch (kind)
            {
                case Kind.Ean13:
                    Digit(code[0], -8, digitFont);
                    for (int i = 0; i < 6; i++)
                        Digit(code[1 + i], 3 + 7 * i, digitFont);
                    for (int i = 0; i < 6; i++)
                        Digit(code[7 + i], 50 + 7 * i, digitFont);
                    break;

                case Kind.Ean8:
                    for (int i = 0; i < 4; i++)
                        Digit(code[i], 3 + 7 * i, digitFont);
                    for (int i = 0; i < 4; i++)
                        Digit(code[4 + i], 36 + 7 * i, digitFont);
                    break;

                default:
                    Digit(code[0], -8, outerFont);
                    for (int i = 1; i < 6; i++)
                        Digit(code[i], 3 + 7 * i, digitFont);
                    for (int i = 0; i < 5; i++)
                        Digit(code[6 + i], 50 + 7 * i, digitFont);
                    Digit(code[11], 96, outerFont);
                    break;
            }
        }
    }
}
