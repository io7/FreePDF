// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Interprets a page's content for text extraction: the graphics and text state of ISO 32000-1 9.3 and 9.4,
    /// the text-showing operators, Form XObjects and, when asked, the straight segments of painted paths.
    /// One reader reads one page.
    /// </summary>
    sealed class TextContentReader
    {
        // A TJ adjustment that moves the next glyph more than this fraction of an em to the right is a word gap.
        const double TjSpace = 0.15;
        // Saved graphics states beyond this are counted but not stored, so a flood of q costs no memory.
        const int MaxSavedStates = 4096;
        // Page tree levels searched for inherited /Resources.
        const int MaxPageTreeDepth = 64;

        sealed class GraphicsState
        {
            internal TextMatrix Ctm = TextMatrix.Identity;
            internal double CharSpacing, WordSpacing, Scale = 1, Leading, Rise, FontSize;
            internal TextFont? Font;

            internal GraphicsState Clone() => (GraphicsState)MemberwiseClone();
        }

        sealed class RunStart
        {
            internal readonly StringBuilder Text = new();
            internal double X, Y, Size;
            internal string Font = "";
        }

        readonly PdfTextExtractionOptions _options;
        readonly TextBudget _budget;
        readonly bool _collectLines;
        readonly Dictionary<PdfDictionary, TextFont> _fonts = new();
        readonly HashSet<PdfDictionary> _activeForms = new();
        readonly Stack<GraphicsState> _saved = new();
        readonly List<PdfTextRun> _runs = new();
        readonly List<PdfLineSegment> _lines = new();
        readonly List<PdfLineSegment> _path = new();
        // The closing edges of the path's open subpaths, which only a fill draws.
        readonly List<PdfLineSegment> _closures = new();
        GraphicsState _state = new();
        TextMatrix _tm = TextMatrix.Identity, _tlm = TextMatrix.Identity;
        int _uncountedSaves, _floor;
        double _currentX, _currentY, _startX, _startY;
        // The current subpath has a segment or curve and has not been closed.
        bool _open;

        internal TextContentReader(PdfTextExtractionOptions options, bool collectLines)
        {
            _options = options;
            _budget = new TextBudget(options.MaxDecodedBytes);
            _collectLines = collectLines;
        }

        internal IReadOnlyList<PdfTextRun> Runs => _runs.AsReadOnly();

        internal IReadOnlyList<PdfLineSegment> Lines => _lines.AsReadOnly();

        internal void Read(PdfPage page)
        {
            using var content = new MemoryStream();
            // A page's content may be split into several streams at any token boundary (ISO 32000-1 7.8.2).
            foreach (var item in TextItems.Items(page.Elements["/Contents"]))
            {
                if (TextItems.Resolve(item) is PdfDictionary { Stream: not null } stream)
                {
                    var bytes = _budget.Decode(stream);
                    content.Write(bytes, 0, bytes.Length);
                    content.WriteByte((byte)'\n');
                }
            }
            Run(content.ToArray(), InheritedResources(page), 0);
        }

        // A file opened in Append mode keeps its page tree, so /Resources can sit on an ancestor (ISO 32000-1 7.7.3.4).
        static PdfDictionary? InheritedResources(PdfDictionary page)
        {
            var seen = new HashSet<PdfDictionary>();
            for (var node = page; node != null && seen.Count < MaxPageTreeDepth && seen.Add(node); node = TextItems.Dict(node, "/Parent"))
            {
                var resources = TextItems.Dict(node, "/Resources");
                if (resources != null)
                    return resources;
            }
            return null;
        }

        void Run(byte[] content, PdfDictionary? resources, int depth)
        {
            try
            {
                foreach (var op in new CParser(content, extracting: true).ReadOperators())
                    Execute(op, resources, depth);
            }
            catch (ContentReaderException)
            {
                // A syntax error ends this stream. What was read before it is kept, as viewers draw it.
            }
        }

        void Execute(COperator op, PdfDictionary? resources, int depth)
        {
            var o = op.Operands;
            switch (op.OpCode.OpCodeName)
            {
                case OpCodeName.q:
                    Save();
                    break;
                case OpCodeName.Q:
                    Restore();
                    break;
                case OpCodeName.cm:
                    if (Matrix(o) is TextMatrix m)
                        _state.Ctm = m.Then(_state.Ctm);
                    break;
                case OpCodeName.BT:
                    _tm = _tlm = TextMatrix.Identity;
                    break;
                case OpCodeName.Tc:
                    _state.CharSpacing = Number(o, 0);
                    break;
                case OpCodeName.Tw:
                    _state.WordSpacing = Number(o, 0);
                    break;
                case OpCodeName.Tz:
                    _state.Scale = Number(o, 0) / 100;
                    break;
                case OpCodeName.TL:
                    _state.Leading = Number(o, 0);
                    break;
                case OpCodeName.Ts:
                    _state.Rise = Number(o, 0);
                    break;
                case OpCodeName.Tf:
                    SetFont(o, resources);
                    break;
                case OpCodeName.gs:
                    SetGraphicsState(o, resources);
                    break;
                case OpCodeName.Td:
                    MoveLine(Number(o, 0), Number(o, 1));
                    break;
                case OpCodeName.TD:
                    _state.Leading = -Number(o, 1);
                    MoveLine(Number(o, 0), Number(o, 1));
                    break;
                case OpCodeName.Tm:
                    if (Matrix(o) is TextMatrix t)
                        _tm = _tlm = t;
                    break;
                case OpCodeName.Tx:
                    MoveLine(0, -_state.Leading);
                    break;
                case OpCodeName.Tj:
                    Show(Last(o));
                    break;
                case OpCodeName.QuoteSingle:
                    MoveLine(0, -_state.Leading);
                    Show(Last(o));
                    break;
                case OpCodeName.QuoteDouble:
                    _state.WordSpacing = Number(o, 0);
                    _state.CharSpacing = Number(o, 1);
                    MoveLine(0, -_state.Leading);
                    Show(Last(o));
                    break;
                case OpCodeName.TJ:
                    ShowArray(Last(o) as CArray);
                    break;
                case OpCodeName.Do:
                    DrawObject(o, resources, depth);
                    break;
                default:
                    if (_collectLines)
                        PathOperator(op.OpCode.OpCodeName, o);
                    break;
            }
        }

        static CObject? Last(CSequence operands) => operands.Count > 0 ? operands[operands.Count - 1] : null;

        static double Number(CSequence operands, int index)
        {
            if (index >= operands.Count)
                return 0;
            switch (operands[index])
            {
                case CInteger integer:
                    return integer.Value;
                case CReal real:
                    return real.Value;
                default:
                    return 0;
            }
        }

        static TextMatrix? Matrix(CSequence o)
        {
            if (o.Count < 6)
                return null;
            return new TextMatrix(Number(o, 0), Number(o, 1), Number(o, 2), Number(o, 3), Number(o, 4), Number(o, 5));
        }

        void Save()
        {
            if (_saved.Count >= MaxSavedStates)
            {
                _uncountedSaves++;
                return;
            }
            _saved.Push(_state.Clone());
        }

        void Restore()
        {
            if (_uncountedSaves > 0)
            {
                _uncountedSaves--;
                return;
            }
            // A form cannot restore a state its caller saved.
            if (_saved.Count > _floor)
                _state = _saved.Pop();
        }

        void SetFont(CSequence o, PdfDictionary? resources)
        {
            _state.FontSize = Number(o, 1);
            _state.Font = null;
            if (o.Count == 0 || o[0] is not CName name)
                return;
            var font = TextItems.Dict(TextItems.Dict(resources, "/Font"), name.Name);
            if (font != null)
                _state.Font = Loaded(font);
        }

        // A graphics state parameter dictionary can set the font and size (ISO 32000-1 8.4.5, /Font [font size]).
        void SetGraphicsState(CSequence o, PdfDictionary? resources)
        {
            if (o.Count == 0 || o[0] is not CName name)
                return;
            var parameters = TextItems.Dict(TextItems.Dict(resources, "/ExtGState"), name.Name);
            if (TextItems.Resolve(parameters?.Elements["/Font"]) is not PdfArray { Elements.Count: >= 2 } pair)
                return;
            if (TextItems.Resolve(pair.Elements[0]) is not PdfDictionary font)
                return;
            _state.Font = Loaded(font);
            _state.FontSize = TextItems.Number(pair.Elements[1]) ?? 0;
        }

        TextFont Loaded(PdfDictionary font)
        {
            if (!_fonts.TryGetValue(font, out var loaded))
            {
                loaded = TextFont.Load(font, _budget);
                _fonts.Add(font, loaded);
            }
            return loaded;
        }

        void MoveLine(double x, double y)
        {
            _tlm = TextMatrix.Translation(x, y).Then(_tlm);
            _tm = _tlm;
        }

        // ISO 32000-1 9.4.4: Trm = [Tfs×Th 0 0 Tfs 0 Trise] × Tm × CTM.
        TextMatrix RenderingMatrix()
            => new TextMatrix(_state.FontSize * _state.Scale, 0, 0, _state.FontSize, 0, _state.Rise).Then(_tm).Then(_state.Ctm);

        RunStart BeginRun()
        {
            var trm = RenderingMatrix();
            return new RunStart
            {
                X = trm.E,
                Y = trm.F,
                Size = System.Math.Sqrt(trm.C * trm.C + trm.D * trm.D),
                Font = _state.Font!.Name,
            };
        }

        void EndRun(RunStart run)
        {
            if (run.Text.Length == 0)
                return;
            if (_runs.Count >= _options.MaxResults)
                throw Parser.LimitExceeded("Text extraction found more than MaxResults text runs on the page.");
            var trm = RenderingMatrix();
            _runs.Add(new PdfTextRun(run.Text.ToString(), run.X, run.Y, trm.E, trm.F, run.Size, run.Font));
        }

        void Show(CObject? operand)
        {
            if (operand is not CString text || _state.Font == null)
                return;
            var run = BeginRun();
            Append(run, Bytes(text));
            EndRun(run);
        }

        void ShowArray(CArray? array)
        {
            if (array == null || _state.Font == null)
                return;
            // The run starts where its first glyph is drawn, after any adjustments before it.
            RunStart? run = null;
            foreach (var item in array)
            {
                if (item is CString text)
                {
                    var bytes = Bytes(text);
                    if (bytes.Length > 0)
                    {
                        run ??= BeginRun();
                        Append(run, bytes);
                    }
                    continue;
                }
                double adjustment;
                switch (item)
                {
                    case CInteger integer:
                        adjustment = integer.Value;
                        break;
                    case CReal real:
                        adjustment = real.Value;
                        break;
                    default:
                        continue;
                }
                _tm = TextMatrix.Translation(-adjustment / 1000 * _state.FontSize * _state.Scale, 0).Then(_tm);
                if (run != null && -adjustment / 1000 > TjSpace && run.Text.Length > 0 && run.Text[run.Text.Length - 1] != ' ')
                {
                    _budget.Spend(2);
                    run.Text.Append(' ');
                }
            }
            if (run != null)
                EndRun(run);
        }

        // ISO 32000-1 9.4.4: tx = ((w0 × Tfs) + Tc + Tw) × Th, with Tw only for the single-byte code 32.
        void Append(RunStart run, byte[] bytes)
        {
            var font = _state.Font!;
            int offset = 0;
            while (offset < bytes.Length)
            {
                offset += font.Read(bytes, offset, out var text, out var width, out var wordSpace);
                _budget.Spend(2L * text.Length);
                run.Text.Append(text);
                double spacing = _state.CharSpacing;
                if (wordSpace)
                    spacing += _state.WordSpacing;
                _tm = TextMatrix.Translation((width * _state.FontSize + spacing) * _state.Scale, 0).Then(_tm);
            }
        }

        // The parser keeps each string byte as one character; octal escapes above 255 lose their high bits (7.3.4.2).
        static byte[] Bytes(CString text)
        {
            var value = text.Value;
            var bytes = new byte[value.Length];
            for (int i = 0; i < value.Length; i++)
                bytes[i] = (byte)value[i];
            return bytes;
        }

        void DrawObject(CSequence o, PdfDictionary? resources, int depth)
        {
            if (o.Count == 0 || o[0] is not CName name)
                return;
            var form = TextItems.Dict(TextItems.Dict(resources, "/XObject"), name.Name);
            if (form?.Stream == null || TextItems.NameOf(form, "/Subtype") != "/Form")
                return;
            if (!_activeForms.Add(form))
                return;
            try
            {
                if (depth + 1 > _options.MaxFormDepth)
                    throw Parser.LimitExceeded("Form XObjects are nested more than MaxFormDepth levels.");
                var state = _state;
                var tm = _tm;
                var tlm = _tlm;
                int floor = _floor;
                _state = _state.Clone();
                _floor = _saved.Count;
                var matrix = TextItems.Matrix(form.Elements["/Matrix"]);
                if (matrix != null)
                    _state.Ctm = matrix.Value.Then(_state.Ctm);
                // A form without /Resources uses its caller's (ISO 32000-1 7.8.3, for compatibility).
                Run(_budget.Decode(form), TextItems.Dict(form, "/Resources") ?? resources, depth + 1);
                while (_saved.Count > _floor)
                    _saved.Pop();
                _state = state;
                _tm = tm;
                _tlm = tlm;
                _floor = floor;
            }
            finally
            {
                _activeForms.Remove(form);
            }
        }

        void PathOperator(OpCodeName name, CSequence o)
        {
            switch (name)
            {
                case OpCodeName.m:
                    MoveTo(Number(o, 0), Number(o, 1));
                    break;
                case OpCodeName.l:
                    LineTo(Number(o, 0), Number(o, 1));
                    break;
                case OpCodeName.c:
                case OpCodeName.v:
                case OpCodeName.y:
                    // Curves are not segments; only their end point moves the current point.
                    if (o.Count >= 2)
                        SetCurrent(Number(o, o.Count - 2), Number(o, o.Count - 1));
                    _open = true;
                    break;
                case OpCodeName.re:
                    Rectangle(Number(o, 0), Number(o, 1), Number(o, 2), Number(o, 3));
                    break;
                case OpCodeName.h:
                    ClosePath();
                    break;
                case OpCodeName.S:
                    Paint(stroked: true, filled: false);
                    break;
                case OpCodeName.B:
                case OpCodeName.Bx:
                    Paint(stroked: true, filled: true);
                    break;
                case OpCodeName.s:
                    ClosePath();
                    Paint(stroked: true, filled: false);
                    break;
                case OpCodeName.b:
                case OpCodeName.bx:
                    ClosePath();
                    Paint(stroked: true, filled: true);
                    break;
                case OpCodeName.f:
                case OpCodeName.F:
                case OpCodeName.fx:
                    Paint(stroked: false, filled: true);
                    break;
                case OpCodeName.n:
                    ClearPath();
                    break;
            }
        }

        // Path coordinates are user space; the CTM cannot change while a path is built (ISO 32000-1 8.5.2).
        void SetCurrent(double x, double y)
        {
            _currentX = _state.Ctm.X(x, y);
            _currentY = _state.Ctm.Y(x, y);
        }

        void MoveTo(double x, double y)
        {
            EndSubpath();
            SetCurrent(x, y);
            _startX = _currentX;
            _startY = _currentY;
        }

        void LineTo(double x, double y)
        {
            double fromX = _currentX, fromY = _currentY;
            SetCurrent(x, y);
            AddSegment(fromX, fromY, _currentX, _currentY);
            _open = true;
        }

        // Filling closes every open subpath with a straight edge (ISO 32000-1 8.5.3); stroking does not.
        void EndSubpath()
        {
            if (_open && (_currentX != _startX || _currentY != _startY))
            {
                // A closing edge counts as a result only when a fill paints it; until then it is bounded on its own.
                if (_closures.Count >= _options.MaxResults)
                    throw SegmentLimit();
                _closures.Add(new PdfLineSegment(_currentX, _currentY, _startX, _startY, false));
            }
            _open = false;
        }

        void Rectangle(double x, double y, double width, double height)
        {
            MoveTo(x, y);
            LineTo(x + width, y);
            LineTo(x + width, y + height);
            LineTo(x, y + height);
            ClosePath();
        }

        void ClosePath()
        {
            if (_currentX != _startX || _currentY != _startY)
                AddSegment(_currentX, _currentY, _startX, _startY);
            _currentX = _startX;
            _currentY = _startY;
            _open = false;
        }

        void AddSegment(double x1, double y1, double x2, double y2)
        {
            if (_lines.Count + _path.Count >= _options.MaxResults)
                throw SegmentLimit();
            _path.Add(new PdfLineSegment(x1, y1, x2, y2, false));
        }

        static PdfReaderException SegmentLimit() => Parser.LimitExceeded("Text extraction found more than MaxResults line segments on the page.");

        void Paint(bool stroked, bool filled)
        {
            if (filled)
                EndSubpath();
            foreach (var segment in _path)
                _lines.Add(new PdfLineSegment(segment.X1, segment.Y1, segment.X2, segment.Y2, stroked));
            // An implicit closing edge is filled, never stroked.
            if (filled)
            {
                if (_lines.Count + _closures.Count > _options.MaxResults)
                    throw SegmentLimit();
                _lines.AddRange(_closures);
            }
            ClearPath();
        }

        void ClearPath()
        {
            _path.Clear();
            _closures.Clear();
            _open = false;
        }
    }
}
