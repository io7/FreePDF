// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Actions;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Internal;

namespace PdfSharp.Pdf
{
    /// <summary>
    /// Represents an outline item in the outlines tree. An 'outline' is also known as a 'bookmark'.
    /// </summary>
    public sealed class PdfOutline : PdfDictionary
    {
        // Reference: 8.2.2  Document Outline / Page 584

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfOutline"/> class.
        /// </summary>
        public PdfOutline()
        {
            // Create _outlines on demand.
            //_outlines = new PdfOutlineCollection(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfOutline"/> class.
        /// </summary>
        /// <param name="document">The document.</param>
        internal PdfOutline(PdfDocument document)
            : base(document)
        {
            // Create _outlines on demand.
            //_outlines = new PdfOutlineCollection(this);
        }

        /// <summary>
        /// Initializes a new instance of this class using the elements of the specified dictionary.
        /// After this type transformation the specified dictionary is dead and cannot be used anymore.
        /// </summary>
        internal PdfOutline(PdfDictionary dict)
            : this(dict, 0)
        { }

        PdfOutline(PdfDictionary dict, int depth)
            : base(dict)
        {
            _fromFile = true;
            _depth = depth;
            Initialize();
            // Reading added the children; that is not a change.
            _countChanged = false;
        }

        /// <summary>
        /// The deepest level of a file's outline that is read; deeper items are left as they are in the file and not
        /// read, as in a damaged file. Reading, saving and counting outline items recurse once per level, and a hostile
        /// file nested thousands of levels deep would overflow the stack, which no exception handler stops.
        /// </summary>
        internal const int MaxDepth = 200;

        // The level of an item read from a file: 0 for the outline root.
        readonly int _depth;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfOutline"/> class.
        /// </summary>
        /// <param name="title">The outline text.</param>
        /// <param name="destinationPage">The destination page.</param>
        /// <param name="opened">Specifies whether the node is displayed expanded (opened) or collapsed.</param>
        /// <param name="style">The font style used to draw the outline text.</param>
        /// <param name="textColor">The color used to draw the outline text.</param>
        public PdfOutline(string title, PdfPage destinationPage, bool opened, PdfOutlineStyle style, XColor textColor)
        {
            Title = title;
            DestinationPage = destinationPage;
            Opened = opened;
            Style = style;
            TextColor = textColor;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfOutline"/> class.
        /// </summary>
        /// <param name="title">The outline text.</param>
        /// <param name="destinationPage">The destination page.</param>
        /// <param name="opened">Specifies whether the node is displayed expanded (opened) or collapsed.</param>
        /// <param name="style">The font style used to draw the outline text.</param>
        public PdfOutline(string title, PdfPage destinationPage, bool opened, PdfOutlineStyle style)
        {
            Title = title;
            DestinationPage = destinationPage;
            Opened = opened;
            Style = style;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfOutline"/> class.
        /// </summary>
        /// <param name="title">The outline text.</param>
        /// <param name="destinationPage">The destination page.</param>
        /// <param name="opened">Specifies whether the node is displayed expanded (opened) or collapsed.</param>
        public PdfOutline(string title, PdfPage destinationPage, bool opened)
        {
            Title = title;
            DestinationPage = destinationPage;
            Opened = opened;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfOutline"/> class.
        /// </summary>
        /// <param name="title">The outline text.</param>
        /// <param name="destinationPage">The destination page.</param>
        public PdfOutline(string title, PdfPage destinationPage)
        {
            Title = title;
            DestinationPage = destinationPage;
        }

        /// <summary>
        /// Sets /Count on this item and its descendants in one pass from the bottom up, and returns the number of
        /// descendants shown while this item is open (its children, and the visible descendants of each open child;
        /// for the root, the number of visible items, ISO 32000-1 12.3.3) and whether anything here changed. An item
        /// read from a file keeps the /Count it was written with unless it or an item below it changed: signature
        /// checks compare a document's revisions by their written form, so rewriting an unchanged item would hide a
        /// later change to it or report one that never happened.
        /// </summary>
        (int Visible, bool Changed) UpdateCounts()
        {
            int visible = 0;
            bool changed = !_fromFile || _countChanged;
            if (_outlines != null)
            {
                foreach (var child in _outlines)
                {
                    var (childVisible, childChanged) = child.UpdateCounts();
                    visible++;
                    if (child._opened)
                        visible += childVisible;
                    changed |= childChanged;
                }
            }

            if (changed)
            {
                // Required on an item with children (Table 153): positive while open, negative while closed. The root's
                // is the number of visible items (Table 152). Neither has one without children.
                if (!HasChildren)
                    Elements.Remove(Keys.Count);
                else if (_parent == null || _opened)
                    Elements[Keys.Count] = new PdfInteger(visible);
                else
                    Elements[Keys.Count] = new PdfInteger(-visible);
            }
            return (visible, changed);
        }

        // Read from a file, and whether this item's children or open state changed since (PdfOutlineCollection sets it).
        readonly bool _fromFile;
        internal bool _countChanged;

        /// <summary>
        /// Gets the parent of this outline item. The root item has no parent and returns null.
        /// </summary>
        public PdfOutline Parent
        {
            get => _parent!; // ?? NRT.ThrowOnNull<PdfOutline>(); Can be null.
            internal set => _parent = value;
        }
        PdfOutline? _parent;  // TODO: No more caching fields.

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        public string Title
        {
            get => Elements.GetString(Keys.Title);
            set
            {
                var s = new PdfString(value, PdfStringEncoding.Unicode);
                Elements.SetValue(Keys.Title, s);
            }
        }

        /// <summary>
        /// Gets or sets the destination page.
        /// Can be null if destination page is not given directly.
        /// </summary>
        public PdfPage? DestinationPage
        {
            get => _destinationPage; /*?? NRT.ThrowOnNull<PdfPage>(); // BUG*/
            set => _destinationPage = value;
        }
        PdfPage? _destinationPage;

        /// <summary>
        /// Gets or sets the left position of the page positioned on the left side of the window.
        /// Applies only if PageDestinationType is Xyz, FitV, FitR, or FitBV.
        /// </summary>
        public double? Left { get; set; }

        /// <summary>
        /// Gets or sets the top position of the page positioned on the top side of the window.
        /// Applies only if PageDestinationType is Xyz, FitH, FitR, ob FitBH.
        /// </summary>
        public double? Top { get; set; }

        /// <summary>
        /// Gets or sets the right position of the page positioned on the right side of the window.
        /// Applies only if PageDestinationType is FitR.
        /// </summary>
        public double Right // Cannot be null in a valid PDF.
        {
            get; set;
        } = Double.NaN;

        /// <summary>
        /// Gets or sets the bottom position of the page positioned on the bottom side of the window.
        /// Applies only if PageDestinationType is FitR.
        /// </summary>
        public double Bottom // Cannot be null in a valid PDF.
        {
            get; set;
        } = Double.NaN;

        /// <summary>
        /// Gets or sets the zoom faction of the page.
        /// Applies only if PageDestinationType is Xyz.
        /// </summary>
        public double? Zoom
        {
            get => _zoom;
            set => _zoom = value is 0 ? null : value;
        }
        double? _zoom; // PDF treats 0 and null equally.

        /// <summary>
        /// Gets or sets whether the outline item is opened (or expanded). An item read from a file is open when its
        /// /Count is positive; saving writes /Count for every item with children, negative while it is closed.
        /// </summary>
        public bool Opened
        {
            get => _opened;
            set
            {
                if (_opened == value)
                    return;
                _opened = value;
                _countChanged = true;
            }
        }
        bool _opened;

        /// <summary>
        /// Gets or sets the style of the outline text.
        /// </summary>
        public PdfOutlineStyle Style
        {
            get => (PdfOutlineStyle)Elements.GetInteger(Keys.F);
            set => Elements.SetInteger(Keys.F, (int)value);
        }

        /// <summary>
        /// Gets or sets the type of the page destination.
        /// </summary>
        public PdfPageDestinationType PageDestinationType
        {
            get => _pageDestinationType;
            set => _pageDestinationType = value;
        }
        PdfPageDestinationType _pageDestinationType = PdfPageDestinationType.Xyz;

        /// <summary>
        /// Gets or sets the color of the text.
        /// </summary>
        /// <value>The color of the text.</value>
        public XColor TextColor { get; set; }

        /// <summary>
        /// Gets a value indicating whether this outline object has child items.
        /// </summary>
        public bool HasChildren => _outlines is { Count: > 0 };

        /// <summary>
        /// Gets the outline collection of this node.
        /// </summary>
        public PdfOutlineCollection Outlines => _outlines ??= new PdfOutlineCollection(Owner, this);

        PdfOutlineCollection? _outlines;

        /// <summary>
        /// Initializes this instance from an existing PDF document.
        /// </summary>
        void Initialize()
        {
            if (Elements.TryGetString(Keys.Title, out var title))
                Title = title;

            var parentRef = Elements.GetReference(Keys.Parent);
            if (parentRef != null)
            {
                //if (parentRef.Value is PdfOutline parent)
                //    Parent = parent;

                if (parentRef.Value is PdfOutline parent)
                    Parent = parent;
            }

            // A positive /Count is an open item. A /Count or /C of the wrong type in a damaged file is ignored rather
            // than failing the document.
            Elements.TryGetInteger(Keys.Count, out var count);
            _opened = count > 0;

            if (Elements.GetValue(Keys.C) is PdfArray { Elements.Count: 3 } colors
                && colors.Elements.TryGetReal(0, out var r) && colors.Elements.TryGetReal(1, out var g) && colors.Elements.TryGetReal(2, out var b))
            {
                TextColor = ColorFromComponents(r, g, b);
            }

            // /Dest is not permitted if /A is present (ISO 32000-1 Table 153), but files can have both; then the
            // GoTo action's destination is the one read.
            var dest = Elements.GetValue(Keys.Dest);
            var a = Elements.GetValue(Keys.A);
            _fileAction = ActionText();

            //PdfArray? destArray;
            if (dest != null)
            {
#if true
                if (dest is PdfArray destArray)
                {
                    if (ParsePageDestination(destArray))
                        RememberFileDestination(fromAction: false);
                }
                else if (dest is PdfString str)
                {
                    // Destination may be in a different PDF file, so we cannot determine the target page.
                }
                else if (dest is PdfName name)
                {
                }
                else
                {
                    Debug.Assert(false, "See what to do when this happened.");
                }
#else
                destArray = dest as PdfArray;
                if (destArray != null)
                {
                    ParsePageDestination(destArray);
                    goto Done;
                }
                else
                {
                    // Do nothing here.
                    // Debug.Assert(false, "See what to do when this happened.");  // TODO
                }
#endif
            }

            if (a != null)
            {
                // Case: If we found an /A entry we use it if it is a /GoTo action to get the destination.

                // The dictionary should be a GoTo action.
                if (a is PdfDictionary action && action.Elements.GetName(PdfAction.Keys.S) == "/GoTo")
                {
                    //dest = action.Elements[PdfGoToAction.Keys.D]; // #US373: Should we expect references here?
                    dest = action.Elements.GetValue(PdfGoToAction.Keys.D); // #US373
                    if (dest is PdfArray array)
                    {
                        if (ParsePageDestination(array))
                            RememberFileDestination(fromAction: true);
                        goto Done;
                    }
                    if (dest is PdfString namedDestination)
                    {
                        PdfArray? destArray = null;
                        // Look in Destinations and name-tree.
                        // GetDestinations() can be null.
                        if (Owner.Catalog.GetDestinations()?.Contains(namedDestination.Value) ?? false)
                        {
                            // IMPROVE: Avoid "Contains()" and call "GetDestination()" only once.
                            destArray = Owner.Catalog.GetDestinations()!.GetDestination(namedDestination.Value);
                        }
                        else if (Owner.Catalog.Names.NameTree != null)
                        {
                            var item = Owner.Catalog.Names.NameTree.GetValue(namedDestination.Value, true);
                            // from PdfReference 1.7, Chapter 12.3.2.3 (Named Destinations):
                            // "...value is either an array defining the destination, ...
                            // or a dictionary with a D entry whose value is such an array."
                            if (item is PdfDictionary itemDict)
                            {
                                destArray = itemDict.Elements.GetArray(PdfGoToAction.Keys.D);
                            }
                            else
                                destArray = item as PdfArray;
                        }
                        if (destArray != null)
                        {
                            if (ParsePageDestination(destArray))
                                RememberFileDestination(fromAction: true);
                            goto Done;
                        }
                    }
                    else
                    {
                        // throw new Exception("Destination Array or Name expected."); // #KeepA
                    }
                }
                else
                {
                    //Debug.Assert(false, "See what to do when this happened.");
                }
            }
            else
            {
                // Neither destination page nor GoTo action.
            }

        Done:
            InitializeChildren();
        }

        /// <summary>
        /// Makes the colour of an outline item's /C entry. Components outside 0 to 1 (some producers write 0 to 255)
        /// are clamped, and NaN reads as 0, instead of throwing. Components are rounded, so that a colour read back
        /// from the three decimals FreePDF writes is the colour written.
        /// </summary>
        internal static XColor ColorFromComponents(double r, double g, double b)
        {
            return XColor.FromArgb(Component(r), Component(g), Component(b));

            static int Component(double value)
            {
                if (Double.IsNaN(value) || value <= 0)
                    return 0;
                if (value >= 1)
                    return 255;
                return (int)Math.Round(value * 255, MidpointRounding.AwayFromZero);
            }
        }

        /// <summary>
        /// Records the destination just read and where it came from, so that saving can tell an unchanged
        /// destination from a new one.
        /// </summary>
        void RememberFileDestination(bool fromAction)
        {
            _fileDestination = Destination();
            _fileDestinationIsAction = fromAction;
        }

        /// <summary>
        /// Gets the destination as one value. Equals, not ==, compares it: Right and Bottom are NaN when unset.
        /// </summary>
        (PdfPage?, PdfPageDestinationType, double?, double?, double, double, double?) Destination()
            => (DestinationPage, PageDestinationType, Left, Top, Right, Bottom, Zoom);

        // The destination as read from the file, and whether it came from a GoTo action (/A) rather than /Dest.
        (PdfPage?, PdfPageDestinationType, double?, double?, double, double, double?)? _fileDestination;
        bool _fileDestinationIsAction;

        // The /A entry as read from the file: an action set by the application differs from it.
        string? _fileAction;

        string? ActionText() => Elements[Keys.A]?.ToString();

        bool ParsePageDestination(PdfArray destination)  // Reference: 8.2  Destination syntax / Page 582
        {
            // A destination FreePDF cannot use (no page, a page number for a remote document, an unknown type) is
            // ignored; missing or non-numeric parameters read as null, which viewers treat as "unchanged".
            if (destination.Elements.Count < 2 || destination.Elements[1] is not PdfName type)
                return false;
            if (!Enum.TryParse(type.Value.Substring(1), true, out PdfPageDestinationType destinationType)
                || !Enum.IsDefined(typeof(PdfPageDestinationType), destinationType))
            {
                return false;
            }
            var page = DestinationPageOf(destination.Elements[0]);
            if (page == null)
                return false;

            DestinationPage = page;
            PageDestinationType = destinationType;
            switch (destinationType)
            {
                // [page /XYZ left top zoom] -- left, top, and zoom can be null.
                case PdfPageDestinationType.Xyz:
                    Left = Number(destination, 2);
                    Top = Number(destination, 3);
                    Zoom = Number(destination, 4); // For this parameter, null and 0 have the same meaning.
                    break;

                // [page /FitH top] and [page /FitBH top] -- top can be null.
                case PdfPageDestinationType.FitH:
                case PdfPageDestinationType.FitBH:
                    Top = Number(destination, 2);
                    break;

                // [page /FitV left] and [page /FitBV left] -- left can be null.
                case PdfPageDestinationType.FitV:
                case PdfPageDestinationType.FitBV:
                    Left = Number(destination, 2);
                    break;

                // [page /FitR left bottom right top] -- all four are required; a missing one reads as 0.
                case PdfPageDestinationType.FitR:
                    Left = Number(destination, 2) ?? 0;
                    Bottom = Number(destination, 3) ?? 0;
                    Right = Number(destination, 4) ?? 0;
                    Top = Number(destination, 5) ?? 0;
                    break;
            }
            return true;
        }

        /// <summary>
        /// Gets the page a destination names, or null if it names no page of this document. A page dictionary that
        /// has no type yet is typed; any other object is left alone, because converting it would destroy it.
        /// </summary>
        static PdfPage? DestinationPageOf(PdfItem item)
        {
            if (item is not PdfReference reference)
                return null;
            if (reference.Value is PdfPage page)
                return page;
            if (reference.Value is PdfDictionary dict && dict.GetType() == typeof(PdfDictionary) && dict.Elements.GetName("/Type") == "/Page")
                return new PdfPage(dict);
            return null;
        }

        static double? Number(PdfArray array, int index)
        {
            if (index < array.Elements.Count && array.Elements.TryGetReal(index, out var value))
                return value;
            return null;
        }

        void InitializeChildren()
        {
            // Follow /First and /Next only to dictionaries that have no type yet. A link to a non-dictionary, or to an
            // object that already has one (the catalog, a page, an item read earlier in a loop), ends the chain:
            // converting it would destroy that object or never stop. Below MaxDepth nothing is read.
            if (_depth >= MaxDepth)
                return;
            var current = Elements.GetReference(Keys.First);
            while (current?.Value is PdfDictionary dict && dict.GetType() == typeof(PdfDictionary))
            {
                var item = new PdfOutline(dict, _depth + 1);
                Outlines.Add(item);
                current = item.Elements.GetReference(Keys.Next);
            }
        }

        /// <summary>
        /// Creates key/values pairs according to the object structure.
        /// </summary>
        internal override void PrepareForSave()
        {
            bool hasKids = HasChildren;
            // Is something to do at all?
            if (_parent != null || hasKids)
            {
                if (_parent == null)
                {
                    // Case: This is the outline dictionary (the root).
                    // Reference: TABLE 8.3  Entries in the outline dictionary / Page 585
                    Debug.Assert(_outlines is { Count: > 0 } && _outlines[0] != null);
                    Elements[Keys.First] = _outlines[0].RequiredReference;
                    Elements[Keys.Last] = _outlines[_outlines.Count - 1].RequiredReference;

                    // /Count of every item, once, from the bottom up.
                    UpdateCounts();
                }
                else
                {
                    // Case: This is an outline item dictionary.
                    // Reference: TABLE 8.4  Entries in the outline item dictionary / Page 585
                    Elements[Keys.Parent] = _parent.RequiredReference;

                    int count = _parent!._outlines!.Count;
                    int index = _parent._outlines.IndexOf(this);
                    Debug.Assert(index != -1);

                    // Has destination? /Dest is not permitted next to /A (ISO 32000-1 Table 153). An action the
                    // application set wins, and a GoTo action read from the file stays while its destination is
                    // unchanged. Otherwise the destination is written as /Dest; a new or changed one replaces the
                    // action read from the file.
                    if (DestinationPage != null!)
                    {
                        var action = ActionText();
                        bool asRead = _fileDestination.HasValue && _fileDestination.Value.Equals(Destination());
                        bool actionSetByApplication = action != null && action != _fileAction;
                        bool unchangedGoToAction = asRead && _fileDestinationIsAction && action != null;
                        if (actionSetByApplication)
                        {
                            Elements.Remove(Keys.Dest);
                        }
                        else if (!unchangedGoToAction)
                        {
                            if (!asRead)
                                Elements.Remove(Keys.A);
                            Elements[Keys.Dest] = CreateDestArray();
                        }
                    }

                    // Not the first element?
                    if (index > 0)
                        Elements[Keys.Prev] = _parent._outlines[index - 1].RequiredReference;

                    // Not the last element?
                    if (index < count - 1)
                        Elements[Keys.Next] = _parent._outlines[index + 1].RequiredReference;

                    if (hasKids && _outlines != null)
                    {
                        Elements[Keys.First] = _outlines[0].RequiredReference;
                        Elements[Keys.Last] = _outlines[_outlines.Count - 1].RequiredReference;
                    }

                    if (TextColor != XColor.Empty && Owner.HasVersion("1.4"))
                        Elements[Keys.C] = new PdfLiteral("[{0}]", PdfEncoders.ToString(TextColor, PdfColorMode.Rgb));  // #US309

                    // if (Style != PdfOutlineStyle.Regular && Document.HasVersion("1.4"))
                    //  //pdf.AppendFormat("/F {0}\n", (int)_style);
                    //  Elements[Keys.F] = new PdfInteger((int)_style);
                }

                // Prepare child elements.
                if (hasKids)
                {
                    foreach (var outline in _outlines!)
                        outline.PrepareForSave();
                }
            }
        }

        PdfArray CreateDestArray()
        {
            // Here I learned that “0.ToString("#.##")” evaluates to the empty string.

            var pageRef = DestinationPage?.Reference;
            if (pageRef == null)
                throw new InvalidOperationException("Cannot create PdfArray because this Outline has no destination page.");

            // Only called if DestinationPage is not null.
            PdfArray? dest = PageDestinationType switch  // TODO: Do not use PdfLiteral here.  // #US309
            {
                // [page /XYZ left top zoom]
                PdfPageDestinationType.Xyz => new(Owner, pageRef,
                    new PdfLiteral(Invariant($"/XYZ {Fd2(Left)} {Fd2(Top)} {Fd2(Zoom)}"))),

                // [page /Fit]
                PdfPageDestinationType.Fit => new PdfArray(Owner, DestinationPage!.RequiredReference,
                    new PdfLiteral("/Fit")),

                // [page /FitH top]
                PdfPageDestinationType.FitH => new PdfArray(Owner, DestinationPage!.RequiredReference,
                    new PdfLiteral(Invariant($"/FitH {Fd2(Top)}"))),

                // [page /FitV left]
                PdfPageDestinationType.FitV => new PdfArray(Owner, DestinationPage!.RequiredReference,
                    new PdfLiteral(Invariant($"/FitV {Fd2(Left)}"))),

                // [page /FitR left bottom right top]
                PdfPageDestinationType.FitR => new PdfArray(Owner, DestinationPage!.RequiredReference,
                    new PdfLiteral(Invariant($"/FitR {Fd2(Left)} {Fd1(Bottom)} {Fd1(Right)} {Fd2(Top)}"))),

                // [page /FitB]
                PdfPageDestinationType.FitB => new PdfArray(Owner, DestinationPage!.RequiredReference,
                    new PdfLiteral("/FitB")),

                // [page /FitBH top]
                PdfPageDestinationType.FitBH => new PdfArray(Owner, DestinationPage!.RequiredReference,
                    new PdfLiteral(Invariant($"/FitBH {Fd2(Top)}"))),

                // [page /FitBV left]
                PdfPageDestinationType.FitBV => new PdfArray(Owner, DestinationPage!.RequiredReference,
                    new PdfLiteral(Invariant($"/FitBV {Fd2(Left)}"))),

                _ => throw new ArgumentOutOfRangeException()
            };
            return dest;

            // /// <summary>
            // /// Format double.
            // /// </summary>
            static string Fd1(double value)
            {
                if (Double.IsNaN(value))
                    throw new InvalidOperationException("Value is not a valid Double.");
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            // /// <summary>
            // /// Format nullable double.
            // /// </summary>
            static string Fd2(double? value)
            {
                return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "null";
            }
        }

        internal override void WriteObject(PdfWriter writer)
        {
            if (writer.IsVerboseLayout)
            {
                var title = Title;
                if (String.IsNullOrEmpty(title))
                    writer.WriteRaw("% Title: \u008bempty\u009b\n");
                else
                    writer.WriteRaw("% Title: \u0093" + FilterUnicode(Title) + "\u0094\n");
            }

            // Everything is done in PrepareForSave. An empty root is written too: it is in the cross-reference
            // table and the catalog refers to it, so skipping it left the reference pointing at the next token.
            base.WriteObject(writer);

            static string FilterUnicode(string text)
            {
                var result = new StringBuilder();
                foreach (char ch in text)
                    result.Append((uint)ch < 256 ? (ch != '\r' && ch != '\n' ? ch : ' ') : '?');
                return result.ToString();
            }
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        public sealed class Keys : KeysBase
        {
            // ReSharper disable InconsistentNaming

            /// <summary>
            /// (Optional) The type of PDF object that this dictionary describes; if present,
            /// must be Outlines for an outline dictionary.
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Optional, FixedValue = "Outlines")]
            public const string Type = "/Type";

            // Outline and outline item are combined
            ///// <summary>
            ///// (Required if there are any open or closed outline entries; must be an indirect reference)
            ///// An outline item dictionary representing the first top-level item in the outline.
            ///// </summary>
            //[KeyInfo(KeyType.Dictionary)]
            //public const string First = "/First";
            //
            ///// <summary>
            ///// (Required if there are any open or closed outline entries; must be an indirect reference)
            ///// An outline item dictionary representing the last top-level item in the outline.
            ///// </summary>
            //[KeyInfo(KeyType.Dictionary)]
            //public const string Last = "/Last";
            //
            ///// <summary>
            ///// (Required if the document has any open outline entries) The total number of open items at all
            ///// levels of the outline. This entry should be omitted if there are no open outline items.
            ///// </summary>
            //[KeyInfo(KeyType.Integer)]
            //public const string Count = "/Count";

            /// <summary>
            /// (Required) The text to be displayed on the screen for this item.
            /// </summary>
            [KeyInfo(KeyType.String | KeyType.Required)]
            public const string Title = "/Title";

            /// <summary>
            /// (Required; must be an indirect reference) The parent of this item in the outline hierarchy.
            /// The parent of a top-level item is the outline dictionary itself.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string Parent = "/Parent";

            /// <summary>
            /// (Required for all but the first item at each level; must be an indirect reference)
            /// The previous item at this outline level.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string Prev = "/Prev";

            /// <summary>
            /// (Required for all but the last item at each level; must be an indirect reference)
            /// The next item at this outline level.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string Next = "/Next";

            /// <summary>
            /// (Required if the item has any descendants; must be an indirect reference)
            ///  The first of this item’s immediate children in the outline hierarchy.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string First = "/First";

            /// <summary>
            /// (Required if the item has any descendants; must be an indirect reference)
            /// The last of this item’s immediate children in the outline hierarchy.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public const string Last = "/Last";

            /// <summary>
            /// (Required if the item has any descendants) If the item is open, the total number of its 
            /// open descendants at all lower levels of the outline hierarchy. If the item is closed, a 
            /// negative integer whose absolute value specifies how many descendants would appear if the 
            /// item were reopened.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string Count = "/Count";

            /// <summary>
            /// (Optional; not permitted if an A entry is present) The destination to be displayed when this 
            /// item is activated.
            /// </summary>
            [KeyInfo(KeyType.ArrayOrNameOrString | KeyType.Optional)]
            public const string Dest = "/Dest";

            /// <summary>
            /// (Optional; not permitted if a Dest entry is present) The action to be performed when
            /// this item is activated.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string A = "/A";

            /// <summary>
            /// (Optional; PDF 1.3; must be an indirect reference) The structure element to which the item 
            /// refers.
            /// Note the ability to associate an outline item with a structure element (such as the beginning 
            /// of a chapter) is a PDF 1.3 feature. For backward compatibility with earlier PDF versions, such
            /// an item should also specify a destination (Dest) corresponding to an area of a page where the
            /// contents of the designated structure element are displayed.
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Optional)]
            public const string SE = "/SE";

            /// <summary>
            /// (Optional; PDF 1.4) An array of three numbers in the range 0.0 to 1.0, representing the 
            /// components in the DeviceRGB color space of the color to be used for the outline entry’s text.
            /// Default value: [0.0 0.0 0.0].
            /// </summary>
            [KeyInfo(KeyType.Array | KeyType.Optional)]
            public const string C = "/C";

            /// <summary>
            /// (Optional; PDF 1.4) A set of flags specifying style characteristics for displaying the outline
            /// item’s text. Default value: 0.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Optional)]
            public const string F = "/F";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            internal static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta? _meta;

            // ReSharper restore InconsistentNaming
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta => Keys.Meta;
    }
}
