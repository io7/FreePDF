// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PdfSharp.Internal;
using PdfSharp.Logging;
using PdfSharp.Pdf.Advanced;

namespace PdfSharp.Pdf.IO
{
    /*
       Direct and indirect objects:

       * If a primitive object (boolean, integer, number, date, string, rectangle etc.) is referenced indirectly,
         the parser reads this object immediately and consumes the indirection.

       * If a compound object (dictionary, array etc.) is referenced indirectly, a PdfReference object
         is returned.

       * If a compound object is a direct object, no PdfReference is created and the object is
         parsed immediately.

       * A reference to a non-existing object is specified as legal, therefore null is returned.
    */

    /// <summary>
    /// Provides the functionality to parse PDF documents.
    /// </summary>
    sealed class Parser
    {
        /// <summary>
        /// Constructs a parser for a document.
        /// </summary>
        public Parser(PdfDocument document, PdfReaderOptions options, ILogger? logger)
        {
            _document = document;
            _options = options;
            _lexer = document._lexer ?? throw new ArgumentNullException(nameof(document), "Lexer not defined.");
            _documentParser = this;
            _logger = logger ?? PdfSharpLogHost.PdfReadingLogger;
        }

        /// <summary>
        /// Constructs a parser for an ObjectStream.
        /// </summary>
        public Parser(PdfDocument document, Stream objectStream, Parser documentParser)
        {
            _document = document!; // NRT HACK_OLD
            _options = documentParser._options;
            _lexer = new Lexer(objectStream, documentParser._logger);
            _documentParser = documentParser;
            _logger = documentParser._logger ?? PdfSharpLogHost.PdfReadingLogger;
        }

        /// <summary>
        /// Sets PDF input stream position to the specified object.
        /// </summary>
        /// <param name="objectID">The ID of the object to move.</param>
        /// <param name="suppressObjectOrderExceptions">Suppresses exceptions that may be caused by not yet available objects.</param>
        public SizeType MoveToObject(PdfObjectID objectID, SuppressExceptions? suppressObjectOrderExceptions)
        {
            SizeType? position = _document.IrefTable[objectID]?.Position;
            if (!position.HasValue)
            {
                SuppressExceptions.HandleError(suppressObjectOrderExceptions, () => throw new PdfReaderException($"Invalid object ID {objectID}."));
                return -1;
            }
            if (position < 0)
            {
                SuppressExceptions.HandleError(suppressObjectOrderExceptions, () => throw new PdfReaderException($"Invalid position {position} for object ID {objectID}."));
                return -1;
            }

            return _lexer.Position = position.Value;
        }

#if true_
        // Experimental code
        // * no stack trace, maybe location ID, ??
        // * cleaner code
        /*public*/
        (SizeType Size, ParserError? ParserError) MoveToObject(PdfObjectID objectID/*, SuppressExceptions? suppressObjectOrderExceptions*/)
        {
            SizeType? position = _document.IrefTable[objectID]?.Position;
            if (!position.HasValue)
            {
                //SuppressExceptions.HandleError(suppressObjectOrderExceptions, () => throw new AggregateException($"Invalid object ID {objectID}."));
                //return -1;
                return (-1, new ParserError(123, "some text"));
            }
            if (position < 0)
            {
                //SuppressExceptions.HandleError(suppressObjectOrderExceptions, () => throw new AggregateException($"Invalid position {position} for object ID {objectID}."));
                //return -1;
                return (-1, new ParserError(123, "some other text"));
            }

            //return _lexer.Position = position.Value;
            return (position.Value, null);
        }
#endif

        /// <summary>
        /// Gets the current symbol from the underlying lexer.
        /// </summary>
        public Symbol Symbol => _lexer.Symbol;

        public PdfObjectID ReadObjectNumber(SizeType position)
        {
            _lexer.Position = position;
            int objectNumber = ReadInteger();
            int generationNumber = ReadInteger();
            return new(objectNumber, generationNumber);
        }

        /// <summary>
        /// Internal function to read PDF object from input stream.
        /// </summary>
        /// <param name="pdfObject">Either the instance of a derived type or null. If it is null
        /// an appropriate object is created.</param>
        /// <param name="objectID">The address of the object.</param>
        /// <param name="includeReferences">If true, specifies that all indirect objects
        /// are included recursively.</param>
        /// <param name="fromObjectStream">If true, the object is parsed from an object stream.</param>
        /// <param name="suppressObjectOrderExceptions">Suppresses exceptions that may be caused by not yet available objects.</param>
        PdfObject ReadObjectInternal(PdfObject? pdfObject, PdfObjectID objectID, bool includeReferences, bool fromObjectStream, SuppressExceptions? suppressObjectOrderExceptions)
        {
#if DEBUG_
            //Debug.WriteLine("ReadObject: " + objectID);
            if (objectID.ObjectNumber == 671)
                _ = typeof(int);
#endif
            int objectNumber = objectID.ObjectNumber;
            int generationNumber = objectID.GenerationNumber;
            if (!fromObjectStream)
            {
                // Reading from 'classical' object.
                MoveToObject(objectID, suppressObjectOrderExceptions);
                if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                    return null!;

                objectNumber = ReadInteger();
                generationNumber = ReadInteger();
            }
            else
            {
                // Reading from ObjectStream.
                var iref = _document.IrefTable[objectID];
                if (iref != null)
                {
                    // Attempt to read an object that was already registered. Keep the former object.
                    // This only happens with corrupt PDF files that have duplicate IDs.
                    if (iref.Value != null!)
                    {
                        PdfSharpLogHost.Logger.LogWarning("Another instance of object {iref} was found. Using previously encountered object instead.", iref);
                        // Attempt to read an object that was already read. Keep the former object.
                        return iref.Value;
                    }

                    if (iref.Position >= 0)
                    {
                        PdfSharpLogHost.Logger.LogWarning("Another instance of object {iref} was found. Keeping reference to previously encountered object.", iref);
                        // The object ID was already found, but the object was not read yet.
                        // We ignore the object in the object stream and return a dummy object.
                        // Better: Do not call this method in the first place.
                        var dummy = new PdfArray
                        {
                            Reference = iref
                        };
                        return dummy; // Return a dummy object. The object is not used, but Reference must not be empty.
                    }
                }
            }

#if DEBUG
            // The following assertion sometime failed (see below)
            //Debug.Assert(objectID == new PdfObjectID(objectNumber, generationNumber));
            if (fromObjectStream is false && objectID != new PdfObjectID(objectNumber, generationNumber))
            {
                // Investigate if this happens. Please send us the PDF file for analysing it (issues (at) pdfsharp.net).
                PdfSharpLogHost.Logger.LogError("Something happened in an object stream that is not expected but can be ignored.");
                // A special kind of bug? Or is this an undocumented PDF feature?
                // PDF4NET 2.6 provides a sample called 'Unicode', which produces a file 'unicode.pdf'
                // The iref table of this file contains the following entries:
                //    iref
                //    0 148
                //    0000000000 65535 f 
                //    0000000015 00000 n 
                //    0000000346 00000 n 
                //    ....
                //    0000083236 00000 n 
                //    0000083045 00000 n 
                //    0000083045 00000 n 
                //    0000083045 00000 n 
                //    0000083045 00000 n 
                //    0000080334 00000 n 
                //    ....
                // Object 84, 85, 86, and 87 maps to the same dictionary, but all PDF readers I tested
                // ignores this mismatch! The following assertion failed about 50 times with this file.
#if true_
                string message = String.Format("xref entry {0} {1} maps to object {2} {3}.",
                    objectID.ObjectNumber, objectID.GenerationNumber, objectNumber, generationNumber);
                Debug.Assert(false, message);
#endif
            }
#endif
            // Always use object ID from iref table (see above).
            objectNumber = objectID.ObjectNumber;
            generationNumber = objectID.GenerationNumber;
#if true_
            Debug.WriteLine(String.Format("obj: {0} {1}", objectNumber, generationNumber));
#endif
            if (fromObjectStream is false)
                ReadSymbol(Symbol.Obj);

            bool checkForStream = false; // Set true if parsing dictionary.
            var symbol = ScanNextToken();
            switch (symbol)
            {
                case Symbol.BeginArray:
                    {
                        PdfArray array;
                        if (pdfObject == null)
                            array = new PdfArray(_document);
                        else
                            array = (PdfArray)pdfObject;

                        pdfObject = ReadArray(array, includeReferences);
                        pdfObject.SetObjectID(objectNumber, generationNumber);
                    }
                    break;

                case Symbol.BeginDictionary:
                    {
                        PdfDictionary dict;
                        if (pdfObject == null)
                            dict = new PdfDictionary(_document);
                        else
                            dict = (PdfDictionary)pdfObject;
                        checkForStream = true;
                        pdfObject = ReadDictionary(dict, includeReferences);
                        pdfObject.SetObjectID(objectNumber, generationNumber);
                    }
                    break;

                case Symbol.Null:
                    pdfObject = new PdfNullObject(_document, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                // Empty object. Invalid PDF, but we need to handle it. Treat as null object.
                case Symbol.EndObj:  // #INVALID_PDF
                    pdfObject = new PdfNullObject(_document, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    return pdfObject;

                case Symbol.Boolean:
                    pdfObject = new PdfBooleanObject(_document, String.Compare(_lexer.Token, Boolean.TrueString, StringComparison.OrdinalIgnoreCase) == 0, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                case Symbol.Integer:
                    pdfObject = new PdfIntegerObject(_document, _lexer.TokenToInteger, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                case Symbol.LongInteger:
                    pdfObject = new PdfLongIntegerObject(_document, _lexer.TokenToLongInteger, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                case Symbol.Real:
                    pdfObject = new PdfRealObject(_document, _lexer.TokenToReal, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                case Symbol.String:
                    pdfObject = new PdfStringObject(_document, _lexer.Token, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                case Symbol.HexString:  // #HEX_STRING_FIX DELETE
                    pdfObject = new PdfStringObject(_document, _lexer.Token, false) { HexLiteral = true };
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                case Symbol.Name:
                    pdfObject = new PdfNameObject(_document, _lexer.Token, false);
                    pdfObject.SetObjectID(objectNumber, generationNumber);
                    if (!fromObjectStream)
                        ReadSymbol(Symbol.EndObj);
                    return pdfObject;

                case Symbol.Keyword:
                    // Should not come here anymore.
                    ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
                    break;

                default:
                    // Should not come here anymore.
                    ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
                    break;
            }
            symbol = ScanNextToken();
            if (symbol == Symbol.BeginStream)
            {
#if DEBUG
                if (objectID.ObjectNumber == 60)
                    _ = typeof(int);
#endif
                // Only dictionaries can have a stream.
                if (pdfObject is not PdfDictionary dict)
                    throw new InvalidOperationException(); // #INVALID_PDF TODO_OLD
                Debug.Assert(checkForStream, "Unexpected stream...");

                ReadDictionaryStream(dict, suppressObjectOrderExceptions);
                if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                    return null!;

                // Should scan 'endobj'.
                symbol = ScanNextToken();
            }
            if (fromObjectStream is false && symbol != Symbol.EndObj)
            {
                // An object whose 'endobj' is missing, followed by the next object or the file structure, is complete;
                // viewers read it, and FreePDF refused the whole file. Anything else still means damage.
                if (symbol is Symbol.Integer or Symbol.XRef or Symbol.Trailer or Symbol.StartXRef or Symbol.Eof)
                    PdfSharpLogHost.PdfReadingLogger.LogWarning("Object '{ObjectID}' has no 'endobj'.", objectID);
                else
                    ParserDiagnostics.ThrowParserException(PsMsgs.UnexpectedToken(_lexer.Token));
            }
            return pdfObject ?? NRT.ThrowOnNull<PdfObject>();
        }

        /// <summary>
        /// Reads the content of a stream between 'stream' and 'endstream'.
        /// Because Acrobat is very tolerant with the crap some producer apps crank out,
        /// it is more work than expected in the first place.<br/>
        /// Reference:     3.2.7  Stream Objects / Page 60
        /// Reference 2.0: 7.3.8  Stream objects / Page 31
        /// </summary>
        /// <param name="dict"></param>
        /// <param name="suppressObjectOrderExceptions">Suppresses exceptions that may be caused by not yet available objects.</param>
        void ReadDictionaryStream(PdfDictionary dict, SuppressExceptions? suppressObjectOrderExceptions)
        {
#if TESTCODE
            if (dict.ObjectID.ObjectNumber == 30)
                _ = typeof(int);
#endif
            // Step 1: We have parsed 'stream' and find position where
            // the content really starts.
            var startPosition = _lexer.FindStreamStartPosition(dict.ObjectID);
            Debug.Assert(startPosition == _lexer.Position);

            // Step 2: We try to get the length of the stream.        //read the content based /Length entry.
            int streamLength = GetStreamLength(dict, suppressObjectOrderExceptions);
            if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                return;
            // An untrusted /Length must neither allocate beyond the available
            // object bytes nor consume a later object's endstream marker.
            var available = GetStreamSearchLength(dict, startPosition);
            if (streamLength < 0 || streamLength > available)
            {
                streamLength = _lexer.DetermineStreamLength(startPosition, available, suppressObjectOrderExceptions);
                if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                    return;
                dict.Elements["/Length"] = new PdfInteger(streamLength);
            }
            int retryCount = 0;
        RetryReadStream:
            // Step 3: We try to read the stream content.
            // Maybe we have to re-read it in case 'endstream' was not at the
            // right place after reading with the length value coming from /Length.
            var bytes = _lexer.ScanStream(startPosition, streamLength, out var readBytes);
            var stream = new PdfDictionary.PdfStream(bytes, dict);
            dict.Stream = stream;
#if DEBUG_  // Check it with Notepad++ directly in PDF file.
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (bytes is not null && bytes.Length > 0)
            {
                var info = Invariant(
                    $"Stream of '{dict.ObjectID}': start={startPosition}, end={startPosition + streamLength}, length={streamLength}, first char='{(char)bytes[0]}'-0x{bytes[0]:X2}, last char='{(char)bytes[^1]}'-0x{bytes[^1]:X2}");
                _logger.LogDebug(info);
            }
            else
            {
                var info = Invariant(
                    $"Stream of '{dict.ObjectID}': start={startPosition}, end={startPosition + streamLength}, length={streamLength}");
                _logger.LogDebug(info);
            }
#endif
            // Step 4: We try to read the 'endstream' keyword.
            // Maybe we have to re-read the content.
            if (streamLength > readBytes)
                streamLength = readBytes;
            if (!TryReadEndStream(dict, startPosition, ref streamLength, suppressObjectOrderExceptions))
            {
                if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                    return;

                if (retryCount != 0)
                {
                    throw new InvalidOperationException(
                        "Should not happen. Correcting stream length failed twice. There may be a bug in DetermineStreamLength. " +
                        $"If you think this is a bug in PDFsharp, please visit {UrlLiterals.LinkToCannotOpenPdfFile} for further information.");
                }
                retryCount++;
                _lexer.Position = startPosition;
                goto RetryReadStream;
            }
            // When we come here 'endstream' was successfully parsed and the logical position is behind it.
        }

        /// <summary>
        /// Read stream length from /Length entry of the dictionary.
        /// But beware, /Length may be an indirect object. Furthermore, it can be an
        /// indirect object located in an object stream that was not yet parsed.
        /// And though /Length is a required entry in a stream dictionary, some
        /// PDF file miss it anyway in some dictionaries.
        /// In this case, we look for '\nendstream' backwards form the beginning
        /// of the object immediately behind this object or, in case this object itself
        /// is the last one in the PDF file, we start searching from the end of the whole file.
        /// </summary>
        int GetStreamLength(PdfDictionary dict, SuppressExceptions? suppressObjectOrderExceptions)
        {
            // A stream with /F keeps its data in an external file (ISO 32000-1 7.3.8.2); /Length still counts the bytes
            // between 'stream' and 'endstream', so it is parsed like any other stream. The external file is never
            // loaded: an untrusted PDF must not make the reader open local files.
#if TEST_CODE_
            // By uncommenting this and the label below,
            // we simulate stream dictionaries without \Length entry.

            // If the PDF file uses xref streams there is no directory of the objects before at least
            // the first xref stream object was read. That means we cannot determine the object behind
            // this object. We can assume that the stream is zipped and search forward for 'endstream'.
            // But because this is only self-test code, we use regular processing if we want to get
            // the length of a xref stream.
            // Creating object streams requires a sophisticated producer apps. For such apps it is very
            // unlikely that they produce ill formatted stream objects.
            // When the stream length is determined by the position of 'endstream' all trailing
            // CR and LF characters are considered to be part of the stream. In case the stream is 
            // encrypted decryption will fail.
            if (dict is not PdfCrossReferenceStream && dict.Owner.SecuritySettings.IsEncrypted is false)
                goto TestStreamWithoutLengthEntry;
            Debug.Assert(dict.Elements["/Type"]?.ToString() == "/XRef");
#endif
            // Most common case first: Length is a direct integer.
            var lengthItem = dict.Elements["/Length"]; // #US373 References handled below. Should we try GetValue here?
            if (lengthItem is PdfInteger pdfInteger)
            {
                Debug.Assert(Convert.ToInt32(lengthItem) == pdfInteger.Value);
                return pdfInteger.Value;
            }

            // Is /Length an indirect object?
            if (lengthItem is PdfReference reference)
            {
                PdfObject lengthObject;
                if (reference.Position == -1 && reference.Value != null!)
                {
                    // If somebody came here, please send us your PDF file so that we can fix it (issues (at) pdfsharp.net).
                    if (reference.Value is not PdfIntegerObject pdfIntegerObject)
                    {
                        SuppressExceptions.HandleError(suppressObjectOrderExceptions, () => throw TH.ObjectNotAvailableException_CannotRetrieveStreamLength(_lexer.Position));
                        return -1;
                    }

                    lengthObject = pdfIntegerObject;
                }
                else if (_documentParser._readingStreamLength)
                {
                    // The length object read for another stream's /Length is a stream whose /Length is indirect again: a
                    // chain of such objects would recurse once per object until the stack overflows. A length is a number;
                    // recover this one from 'endstream' below.
                    lengthObject = null!;
                }
                else
                {
                    // OMG: The length of a stream is an indirect object in another dictionary that is not yet read.
                    // And this other dictionary can be wrapped in an object stream.
                    // Makes no sense at all, but some producer apps do it this way.
                    var state = SaveState();
                    _documentParser._readingStreamLength = true;
                    try
                    {
                        lengthObject = ReadIndirectObject(reference, suppressObjectOrderExceptions);
                        if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                            return -1;
                    }
                    catch (Exception ex) when (!IsLimitExceeded(ex))
                    {
                        // If somebody came here, please send us your PDF file so that we can fix it (issues (at) pdfsharp.net).
                        // (A limit refusal passes through as it is, so that its marker is seen.)
                        throw TH.ObjectNotAvailableException_CannotRetrieveStreamLength(_lexer.Position, ex);
                    }
                    finally
                    {
                        _documentParser._readingStreamLength = false;
                    }
                    RestoreState(state);
                }

                if (lengthObject is PdfIntegerObject { Value: >= 0 } lengthInteger)
                {
                    // Reset Length to a direct object.
                    dict.Elements["/Length"] = new PdfInteger(lengthInteger.Value);
                    return lengthInteger.Value;
                }
                // Not a byte count: recover the length from 'endstream' as for a missing /Length.
            }

            // The dictionary obviously has not even the required /Length entry 🤯.
            PdfSharpLogHost.Logger.LogError("Object '{Object}' has no valid /Length entry. Try to determine stream length by looking for 'endstream'.",
                dict.ObjectID.ToString());
#if TEST_CODE_
            TestStreamWithoutLengthEntry:
#endif
            // Try to determine an upper limit of the stream length.
            var behindPosition = _document.IrefTable.GetPositionOfObjectBehind(dict, _lexer.Position);

            // The current logical stream position must be the start of the stream content.
            var streamStart = _lexer.Position;
            int searchLength;

            if (behindPosition != -1)
            {
                // Read up to next object.
                searchLength = (int)(behindPosition - streamStart);
            }
            else
            {
                // Object is obviously last object, so read up to end of file stream.
                searchLength = (int)(_lexer.PdfLength - streamStart);
            }

            var lenStream = _lexer.DetermineStreamLength(streamStart, searchLength, suppressObjectOrderExceptions);
            if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                return -1;

            PdfSharpLogHost.Logger.LogInformation("Determined stream length of object '{Object}' is {StreamLength}.",
                dict.ObjectID, lenStream);

            // Give stream a direct Length entry.
            dict.Elements["/Length"] = new PdfInteger(lenStream);

            return lenStream;
        }

        /// <summary>
        /// Try to read 'endstream' after reading the stream content. Sometimes the Length is not exact
        /// and ReadSymbol fails. In this case we search the token 'endstream' in the
        /// neighborhood where Length points.
        /// </summary>
        bool TryReadEndStream(PdfDictionary dict, SizeType streamStart, ref int streamLength, SuppressExceptions? suppressObjectOrderExceptions)
        {
#if DEBUG
            if (dict.ObjectNumber == 30)
                _ = typeof(int);
#endif
            // If PDF is well-formed, TryScanEndStreamSymbol will succeed.
            if (_lexer.TryScanEndStreamSymbol())
                return true;

            // #INVALID_PDF
            _endStreamNotFoundCounter++;
            PdfSharpLogHost.Logger.LogError(
                "Failed to read 'endstream' in object '{ObjectID}' immediately behind the end of the stream. (counter: {Counter})",
                dict.ObjectID, _endStreamNotFoundCounter);

            // Find 'endstream' up to where the next object begins. It looked only 20 bytes past /Length, so a stream
            // followed by more padding, or with a /Length much too short, made the whole file fail to open.
            var oldLength = streamLength;
            var endStream = _lexer.FindEndStream(streamStart, GetStreamSearchLength(dict, streamStart), oldLength, out streamLength);
            if (endStream < 0)
            {
                SuppressExceptions.HandleError(suppressObjectOrderExceptions,
                    () => throw TH.ObjectNotAvailableException_CannotRetrieveStreamLength(streamStart));
                return false;
            }
            if (streamLength == oldLength)
            {
                // Only white space between the data and 'endstream': /Length is right. Continue behind 'endstream'.
                _lexer.Position = streamStart + endStream + "endstream".Length;
                PdfSharpLogHost.Logger.LogInformation(
                    "Stream of object '{ObjectID}' is followed by white space before 'endstream'; its length {Length} is kept.",
                    dict.ObjectID, oldLength);
                return true;
            }
            PdfSharpLogHost.Logger.LogInformation(
                "Stream length of object '{ObjectID}' corrected from {OldLength} to {CorrectLength}.",
                dict.ObjectID, oldLength, streamLength);

            // Give stream the correct Length entry based on position of 'endstream'.
            dict.Elements["/Length"] = new PdfInteger(streamLength);
            return false;
        }

        int GetStreamSearchLength(PdfDictionary dictionary, SizeType start)
        {
            SizeType end;
            if (_streamBoundaries == null)
                end = _document.IrefTable.GetPositionOfObjectBehind(dictionary, start);
            else
            {
                // After xref loading, file offsets are stable. Avoid scanning the
                // entire object table for every valid stream in a large document.
                var index = Array.BinarySearch(_streamBoundaries, start);
                if (index < 0) index = ~index;
                end = index < _streamBoundaries.Length ? _streamBoundaries[index] : -1;
            }
            if (end < 0 || end > _lexer.PdfLength)
                end = _lexer.PdfLength;
            if (end < start)
                throw new PdfReaderException("Invalid stream boundary.");
            return (int)Math.Min((long)end - start, int.MaxValue);
        }

        SizeType[]? _streamBoundaries;

        public PdfArray ReadArray(PdfArray array, bool includeReferences)
        {
            Debug.Assert(Symbol == Symbol.BeginArray);

            if (array == null!)
                array = new(_document);

            var items = ParseObject(Symbol.EndArray);
            var count = items.Count;
            for (int idx = 0; idx < count; idx++)
            {
                var val = items[idx];
                if (includeReferences && val is PdfReference reference)
                {
                    // Use ReadReference from _documentParser, to include all references from the whole document
                    // not only from the current object stream.
                    val = _documentParser.ReadReference(reference, true);
                }
                array.Elements.Add(val);
            }
            return array;
        }
#if DEBUG_
        static int ReadDictionaryCounter;
#endif

        internal PdfDictionary ReadDictionary(PdfDictionary? dict, bool includeReferences)
        {
            Debug.Assert(Symbol == Symbol.BeginDictionary);
#if DEBUG_
            var xxx = _lexer.GetNeighborhoodOfCurrentPosition();
            var yyy = _lexer.GetNeighborhoodOfCurrentPosition(true);
#endif

#if DEBUG_
            ReadDictionaryCounter++;
            Debug.WriteLine(ReadDictionaryCounter.ToString());
            if (ReadDictionaryCounter == 101)
                _ = typeof(int);
#endif
#if DEBUG
            if (dict == null)
                _ = typeof(int);
#endif
            dict ??= new PdfDictionary(_document);
            DictionaryMeta meta = dict.Meta;

            var items = ParseObject(Symbol.EndDictionary);
            int count = items.Count;
            if (count % 2 != 0)
                ParserDiagnostics.ThrowParserException(
                    "A PDF dictionary contains an odd number of PDF objects. " +
                    "PDF expects pairs of names and values.");

            for (int idx = 0; idx < count; idx += 2)
            {
                var val = items[idx];
                // Recover only the string/integer key forms documented in real-world PDFs.
                // Other objects are ambiguous keys and must still be rejected.
                string key;
                if (val is PdfName name)
                    key = name.Value;
                else if (val is PdfString text)
                    key = "/" + text.Value;
                else if (val is PdfInteger number)
                    key = "/" + number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (val is PdfLongInteger longNumber)
                    key = "/" + longNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else
                {
                    ParserDiagnostics.ThrowParserException("Name expected.");
                    throw new InvalidOperationException("Unreachable dictionary key type.");
                }

                val = items[idx + 1];
                if (includeReferences && val is PdfReference reference)
                {
                    // ReadReference() has to be always called for the document, even if called from ObjectStream parser.
                    val = _documentParser.ReadReference(reference, true);
                }

                dict.Elements[key] = val;
            }
            return dict;
        }

#if DEBUG_
        static int ParseObjectCounter;
#endif

        /// <summary>
        /// Parses whatever comes until the specified stop symbol is reached.
        /// </summary>
        List<PdfItem> ParseObject(Symbol stopSymbol)
        {
#if DEBUG_
            ParseObjectCounter++;
            Debug.WriteLine(ParseObjectCounter.ToString());
            if (ParseObjectCounter == 178)
                _ = typeof(int);
#endif
            var items = new List<PdfItem>();
            Symbol symbol;
            while ((symbol = ScanNextToken(true)) != Symbol.Eof)
            {
                if (symbol == stopSymbol)
                    return items;

                switch (symbol)
                {
                    case Symbol.Comment:
                        // Ignore comments.
                        break;

                    case Symbol.Null:
                        items.Add(PdfNull.Value);
                        break;

                    case Symbol.Boolean:
                        items.Add(new PdfBoolean(_lexer.TokenToBoolean));
                        break;

                    case Symbol.Integer:
                        items.Add(new PdfInteger(_lexer.TokenToInteger));
                        break;

                    case Symbol.LongInteger:
                        items.Add(new PdfLongInteger(_lexer.TokenToLongInteger));
                        break;

                    case Symbol.Real:
                        items.Add(new PdfReal(_lexer.TokenToReal));
                        break;

                    case Symbol.String:
                        items.Add(new PdfString(_lexer.Token, PdfStringFlags.RawEncoding));
                        break;

                    case Symbol.HexString:
                        items.Add(new PdfString(_lexer.Token, true));  // #HEX_STRING_FIX DELETE
                        break;

                    case Symbol.Name:
                        items.Add(new PdfName(_lexer.Token));
                        break;

#if DEBUG
                    case Symbol.R:
                        Debug.Assert(false, "Must not come here anymore");
                        break;
#endif

                    case Symbol.ObjRef:
                        {
                            (int objectNumber, int generationNumber) = _lexer.TokenToObjectID;
                            var objectID = new PdfObjectID(objectNumber, generationNumber);

                            var iref = _document.IrefTable[objectID];
                            if (iref == null)
                            {
                                // If a document has more than one PdfXRefTable it is possible that the first trailer has
                                // indirect references to objects whose iref entry is not yet read in.
                                if (_document.IrefTable.IsUnderConstruction)
                                {
                                    // XRefTable not complete when trailer is read. Create temporary irefs that are
                                    // removed later in PdfTrailer.Finish().
                                    iref = PdfReference.CreateForObjectID(objectID, 0);
                                    items.Add(iref);
                                }
                                else
                                {
                                    // PDF Reference 2.0 section 7.3.10:
                                    // An indirect reference to an undefined object shall not be considered an error by a PDF processor;
                                    // it shall be treated as a reference to the null object.
                                    items.Add(PdfNull.Value);
                                }
                            }
                            else
                                items.Add(iref);
                            break;
                        }

                    case Symbol.BeginArray:
                        var array = new PdfArray(_document);
                        Nest();
                        try
                        {
                            ReadArray(array, false);
                        }
                        finally
                        {
                            _nesting--;
                        }
                        items.Add(array);
                        break;

                    case Symbol.BeginDictionary:
                        var dict = new PdfDictionary(_document);
                        Nest();
                        try
                        {
                            ReadDictionary(dict, false);
                        }
                        finally
                        {
                            _nesting--;
                        }
                        items.Add(dict);
                        break;

                    // Not expected here (a stream follows only a top-level dictionary):
                    //case Symbol.BeginStream:
                    //case Symbol.None:
                    //case Symbol.Keyword:
                    //case Symbol.EndStream:
                    //case Symbol.EndArray:
                    //case Symbol.EndDictionary:
                    //case Symbol.Obj:
                    //case Symbol.EndObj:
                    //case Symbol.XRef:
                    //case Symbol.Trailer:
                    //case Symbol.StartXRef:
                    //case Symbol.Eof:
                    default:
                        ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
                        SkipCharsUntil(stopSymbol);
                        return items;
                }
            }
            ParserDiagnostics.ThrowParserException("Unexpected end of file."); // TODO_OLD L10N using PsMsgs
            return items;  // Dummy code.
        }

        Symbol ScanNextToken(bool testForObjectReference = false)
            => _lexer.ScanNextToken(testForObjectReference);

        /// <summary>
        /// The deepest nesting of direct arrays and dictionaries read. Reading, and much of what later walks a direct
        /// object (writing, decrypting, copying, hashing), recurses once per level, and a hostile file nested tens of
        /// thousands of levels deep would overflow the stack, which no exception handler stops. Real files nest a few
        /// levels.
        /// </summary>
        internal const int MaxNesting = 256;
        int _nesting;

        // Set on the document's parser while it reads an indirect /Length.
        bool _readingStreamLength;

        // Cross-reference and object streams are decoded while a file is opened, before anything else can be checked: each
        // to at most MaxStructuralStream bytes, and all of a file's together to at most StructuralBudget, so that a few
        // kilobytes of Flate (or several Flate filters in a row) cannot inflate to gigabytes.
        internal const long MaxStructuralStream = 64L * 1024 * 1024;
        long StructuralBudget => Math.Max(256L * 1024 * 1024, 64L * Math.Max(0L, _document.FileSize));
        long _structuralDecoded;   // on the document's parser

        /// <summary>
        /// The decoded data of a cross-reference or object stream, within <see cref="MaxStructuralStream"/> and what is left
        /// of the file's budget; beyond them <see cref="LimitExceeded"/> is thrown.
        /// </summary>
        internal byte[] StructuralData(PdfDictionary stream)
        {
            var raw = stream.Stream?.Value ?? [];
            var filter = stream.Elements.GetValue("/Filter");
            if (filter == null)
                return raw;
            var owner = _documentParser;
            long left = owner.StructuralBudget - owner._structuralDecoded;
            if (left <= 0)
                throw LimitExceeded($"The file's cross-reference and object streams decode to more than {owner.StructuralBudget} bytes.");
            var data = Filters.Filtering.Decode(raw, filter, stream.Elements.GetValue("/DecodeParms"), Math.Min(MaxStructuralStream, left));
            owner._structuralDecoded += data.Length;
            return data;
        }

        void Nest()
        {
            if (++_nesting > MaxNesting)
            {
                _nesting--;
                throw LimitExceeded($"Arrays and dictionaries are nested more than {MaxNesting} levels deep.");
            }
        }

        /// <summary>
        /// Marks a <see cref="PdfReaderException"/> as a refusal of a file that exceeds one of FreePDF's limits (nesting,
        /// field depth), not as damage: a signature check that reads earlier revisions must not take such a revision for a
        /// %%EOF that is no revision and skip the signatures in it.
        /// </summary>
        internal const string LimitExceededKey = "FreePDF.LimitExceeded";

        internal static PdfReaderException LimitExceeded(string message)
        {
            var exception = new PdfReaderException(message);
            exception.Data[LimitExceededKey] = true;
            return exception;
        }

        /// <summary>The exception, or one it wraps, is a refusal from <see cref="LimitExceeded"/>.</summary>
        internal static bool IsLimitExceeded(Exception? exception)
        {
            for (int depth = 0; exception != null && depth < 32; depth++, exception = exception.InnerException)
            {
                if (exception.Data.Contains(LimitExceededKey))
                    return true;
            }
            return false;
        }

        Symbol SkipCharsUntil(Symbol stop)
        {
            switch (stop)
            {
                case Symbol.EndDictionary:
                    return SkipCharsUntil(">>", stop);

                default:
                    Symbol symbol;
                    do
                    {
                        symbol = ScanNextToken();
                    } while (symbol != stop && symbol != Symbol.Eof);
                    return symbol;
            }
        }

        Symbol SkipCharsUntil(string text, Symbol stop)
        {
            int length = text.Length;
            int idx = 0;
            char ch;
            while ((ch = _lexer.ScanNextChar(true)) != Chars.EOF)
            {
                if (ch == text[idx])
                {
                    if (idx + 1 == length)
                    {
                        _lexer.ScanNextChar(true);
                        return stop;
                    }
                    idx++;
                }
                else
                    idx = 0;
            }
            return Symbol.Eof;
        }

        /// <summary>
        /// Reads the object ID and the generation and sets it into the specified object.
        /// </summary>
        void ReadObjectID(PdfObject? obj)
        {
            int objectNumber = ReadInteger();
            int generationNumber = ReadInteger();
            ReadSymbol(Symbol.Obj);
            obj?.SetObjectID(objectNumber, generationNumber);
        }

        PdfItem ReadReference(PdfReference iref, bool includeReferences)
        {
            // Should not come here anymore.
            throw new NotImplementedException("ReadReference");
        }

        /// <summary>
        /// Reads the next symbol that must be the specified one.
        /// </summary>
        Symbol ReadSymbol(Symbol symbol)
        {
            Symbol current = ScanNextToken(symbol == Symbol.ObjRef);
            if (symbol != current)
                ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
            return current;
        }

        /// <summary>
        /// Reads a name from the PDF data stream. The preceding slash is part of the result string.
        /// </summary>
        string ReadName()
        {
            var symbol = ScanNextToken(false);
            if (symbol != Symbol.Name)
                ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
            return _lexer.Token;
        }

        /// <summary>
        /// Reads an integer value directly from the PDF data stream.
        /// </summary>
        int ReadInteger()
        {
            Symbol symbol = _lexer.ScanNextToken(false);
            if (symbol == Symbol.Integer)
                return _lexer.TokenToInteger;

            if (symbol == Symbol.LongInteger)
            {
                // Should not happen or is a bug in the parser.
                Debug.Assert(false, "ReadInteger found a long integer.");
            }

            // An 'R' here is an unexpected token like any other. An old fallback read an object ID after it, which read an
            // integer again: a run of 'R' tokens (in a damaged cross-reference section, say) recursed once per token until
            // the stack overflowed.
            ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
            return 0;
        }

        /// <summary>
        /// Reads an offset value (int or long) directly from the PDF data stream.
        /// </summary>
        SizeType ReadSize()
        {
            Symbol symbol = _lexer.ScanNextToken(false);

#if USE_LONG_SIZE
            if (symbol is Symbol.Integer or Symbol.LongInteger)
                return _lexer.TokenToLongInteger;
#else
            if (symbol is Symbol.Integer)
                return _lexer.TokenToInteger;

            if (symbol is Symbol.LongInteger)
                throw new PdfReaderException("File too large"); // Should not come here.
#endif
            ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
            return 0;
        }

        /// <summary>
        /// Reads the PdfObject of the reference, no matter if it’s saved at document level or inside an ObjectStream.
        /// </summary>
        internal PdfObject ReadIndirectObject(PdfReference pdfReference, SuppressExceptions? suppressObjectOrderExceptions, bool withoutDecrypting = false)
        {
            try
            {
                var objectID = pdfReference.ObjectID;
                var pdfObject = pdfReference.Value;

                // Return already loaded PdfObject.
                if (pdfObject != null!)
                {
                    Debug.Assert(_document.IrefTable.Contains(objectID));
                    return pdfObject;
                }

                var isInObjectStream = pdfReference.Position == -1;

                // Option 1: Load object from ObjectStream.
                if (isInObjectStream)
                {
                    pdfObject = ReadIndirectObjectFromObjectStreamInternal(objectID, suppressObjectOrderExceptions);
                    if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                        return null!;
                }
                // Option 2: Load object on file level.
                else
                {
                    pdfObject = ReadObjectInternal(null, objectID, false, false, suppressObjectOrderExceptions);
                    if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                        return null!;

                    // Decrypt file level object.
                    if (!withoutDecrypting)
                        _document.EffectiveSecurityHandler?.DecryptObject(pdfObject);
                }

                // Set maximum object number.
                _document.IrefTable.MaxObjectNumber = Math.Max(_document.IrefTable.MaxObjectNumber, objectID.ObjectNumber);

                Debug.Assert(_document.IrefTable.Contains(pdfReference.ObjectID));

                return pdfObject;
            }
            catch (Exception ex)
            {
                //Debug.WriteLine(ex.Message);
                PdfSharpLogHost.PdfReadingLogger.LogError(ex.Message);
                // Rethrow exception to notify caller.
                throw;
            }
        }

        PdfObject ReadIndirectObjectFromObjectStreamInternal(PdfObjectID objectID, SuppressExceptions? suppressObjectOrderExceptions)
        {
#if true
            // New code: Get the ObjectStream’s parser and the offset for the ObjectID from _objectStreamObjectSources and load the object.

            // Try to get the entry with the parser and the offset for the ObjectID.
            if (!_objectStreamObjectSources.TryGetValue(objectID, out var objectStreamObjectSource))
            {
                SuppressExceptions.HandleError(suppressObjectOrderExceptions, () => throw TH.PdfReaderException_ObjectCouldNotBeFoundInObjectStreams());
                return null!;
            }

            var objectStreamParser = objectStreamObjectSource.Parser;
            var offset = objectStreamObjectSource.Offset;

            // Set position in parser for the object stream and read object.
            objectStreamParser._lexer.Position = offset;
            var pdfObject = objectStreamParser.ReadObjectInternal(null, objectID, false, true, suppressObjectOrderExceptions);
            if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                return null!;
            return pdfObject;
#else
            // Old code: Scan each ObjectStream for the offset of the ObjectID and load it, if found.
            // Get the ObjectStream and its Parser from _objectStreamsWithParsers.
            // Scanning each ObjectStream for each objectID lead to performance issues.
            // The retry code lead to huge performance problems because of extensive Contains() checks.
            // It could be omitted in the new code as the code after the foreach loop was not executed in any test.
            // It seems to be obsolete and written before PdfReader executed ReadAllObjectStreamsAndTheirReferences() before ReadAllIndirectObjects().

            var doNextRound = true;
            var checkedObjectStreams = new List<PdfObjectStream>();

            // If the object can’t be loaded immediately, retry loading after updating ObjectStreams.
            while (doNextRound)
            {
                foreach (var objectStreamWithParser in _objectStreamsWithParsers.Values)
                {
                    var objectStream = objectStreamWithParser.ObjectStream;

                    if (checkedObjectStreams.Contains(objectStream))
                        continue;

                    checkedObjectStreams.Add(objectStream);

                    if (objectStream.TryGetObjectOffset(objectID, out var offset, suppressObjectOrderExceptions))
                    {
                        if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                            return null!;

                        var objectStreamParser = objectStreamWithParser.Parser;
                        objectStreamParser._lexer.Position = offset;
                        var pdfObject = objectStreamParser.ReadObjectInternal(null, objectID, false, true, suppressObjectOrderExceptions);
                        if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                            return null!;
                        return pdfObject;
                    }
                }

                //Debug.WriteLine($"Reading indirect object with ID {objectID} from ObjectStream will be retried.");
                PdfSharpLogHost.PdfReadingLogger.LogWarning($"Reading indirect object with ID {objectID} from ObjectStream will be retried.");

                // Load skippedObjectStreamNumbers in the next round and reset skippedObjectStreamNumbers variable.
                var oldObjectStreamCount = _objectStreamsWithParsers.Count;
                // Maybe there are more references known now.
                ReadAllObjectStreamsAndTheirReferences();

                // Retry, if the number of ObjectStreams has changed.
                doNextRound = _objectStreamsWithParsers.Count > oldObjectStreamCount;
            }

            //Debug.WriteLine($"Reading indirect object with ID {objectID} from ObjectStream finally failed.");
            PdfSharpLogHost.PdfReadingLogger.LogError($"Reading indirect object with ID {objectID} from ObjectStream finally failed.");

            throw TH.PdfReaderException_ObjectCouldNotBeFoundInObjectStreams();
#endif
        }

        /// <summary>
        /// Reads the PdfObjects of all known references, no matter if they are saved at document level or inside an ObjectStream.
        /// </summary>
        internal void ReadAllIndirectObjects()
        {
            var pdfReferences = _document.IrefTable.AllReferences;
            foreach (var pdfReference in pdfReferences)
            {
                if (pdfReference.Value == null!)
                {
                    var pdfObject = ReadIndirectObject(pdfReference, null);

                    Debug.Assert(pdfObject.Reference == pdfReference);
                    pdfObject.Reference = pdfReference;
                    Debug.Assert(pdfObject.Reference.Value != null, "Something went wrong.");
                }
                else
                {
                    Debug.Assert(_document.IrefTable.Contains(pdfReference.ObjectID));
                }
            }
        }

        List<PdfObjectID> LoadObjectStreamIDs()
        {
            // Only streams selected by active xref entries are needed. Physical
            // stream order cannot determine which version of each member is current.
            return _compressedObjectLocations.Keys.ToList();
        }

        readonly Dictionary<PdfObjectID, Dictionary<PdfObjectID, int>> _compressedObjectLocations = new();

        /// <summary>
        /// Reads all ObjectStreams and the references to the PdfObjects they hold.
        /// </summary>
        internal void ReadAllObjectStreamsAndTheirReferences()
        {
#if true
            // New code: Add an entry for each ObjectID saved in an ObjectStream to _objectStreamObjectSources.
            // Each entry holds the ObjectStream’s parser and the offset, to be able to directly load the object later.

            var nextObjectStreamIDsToLoad = LoadObjectStreamIDs();
            int nextObjectStreamIDsCount;

            int thisObjectStreamIDsCount;

            // If any ObjectStream can’t be loaded immediately because its stream length is saved in another ObjectStream, retry it in the next round.
            // Repeat while there are nextObjectStreamIDsToLoad and while their count has changed
            // (if it is unchanged, no more ObjectStream could be loaded, so we suppose cyclic references).
            do
            {
                // Get the next ObjectStreams to load.
                var thisObjectStreamIDsToLoad = nextObjectStreamIDsToLoad;
                thisObjectStreamIDsCount = thisObjectStreamIDsToLoad.Count;

                // Reset nextObjectStreamIDsToLoad to restart gathering.
                nextObjectStreamIDsToLoad = [];

                foreach (var objectStreamID in thisObjectStreamIDsToLoad)
                {
                    var objectStreamReference = _document.IrefTable[objectStreamID];

                    // Suppress exceptions that may be caused by not yet available objects.
                    var suppressObjectOrderExceptions = new SuppressExceptions();

                    // Read the PdfObjectStream object.
                    var objectStream = ReadObjectStream(objectStreamReference, suppressObjectOrderExceptions);
                    if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                    {
                        // Exceptional case: Reset objectStreamReference. Value back to null, because it could not be read
                        // properly and that may cause further errors.
                        objectStreamReference?.ResetObject();

                        // ObjectStream could not be loaded. Maybe its stream length is saved in an ObjectStream not yet loaded. Try it again in the next round.
                        nextObjectStreamIDsToLoad.Add(objectStreamID);

                        PdfSharpLogHost.PdfReadingLogger.LogWarning("Loading ObjectStream with ID {objectStreamID} will be retried.", objectStreamID);

                        // Read next ObjectStream.
                        continue;
                    }

                    // Create the parser for the object stream.
                    var objectStreamParser = new Parser(_document, new MemoryStream(StructuralData(objectStream)), _documentParser);

                    // Select individual members using the current cross-reference
                    // revision, rather than collecting stale copies in the stream.
                    var selectedObjects = _compressedObjectLocations[objectStreamID];

                    // Save all ObjectIDs with the parser of its ObjectStream and its offset.
                    foreach (var selectedObject in selectedObjects)
                    {
                        var objectID = selectedObject.Key;
                        var offset = objectStream.GetObjectOffset(objectID, selectedObject.Value);

                        // The selected xref entry already registered this reference.
                        Debug.Assert(_document.IrefTable.Contains(objectID));
                        _objectStreamObjectSources.Add(objectID, (objectStreamParser, offset));
                    }
                }

                nextObjectStreamIDsCount = nextObjectStreamIDsToLoad.Count;
            } while (nextObjectStreamIDsCount > 0 && nextObjectStreamIDsCount != thisObjectStreamIDsCount);

            // Handle ObjectStreams that could not be loaded.
            if (nextObjectStreamIDsCount > 0)
            {
                PdfSharpLogHost.PdfReadingLogger.LogError($"Loading ObjectStreams with IDs {String.Join(", ", nextObjectStreamIDsToLoad)} finally failed.");

                throw new ObjectNotAvailableException($"Could not load the ObjectStreams with the following ObjectNumbers: {String.Join(", ", nextObjectStreamIDsToLoad)}. " +
                                                      $"Perhaps there is a cyclic reference between ObjectStreams, whose stream lengths are saved in an object inside the other ObjectStream.");
            }
#else
            // Old code: Add an entry for each ObjectStreamID to _objectStreamsWithParsers.
            // Each entry holds the ObjectStream and its Parser, to be able to scan the ObjectStreams later.
            // _objectStreamsWithParsers.ContainsKey(objectStreamID) caused performance issues and could be omitted, as the loop does not save the parser for an ObjectStream a second time.
            
            var pdfReferences = _document.IrefTable.AllReferences;

            var objectStreamIDsToLoad = LoadObjectStreamIDs();

            var skippedObjectStreamIDs = new List<PdfObjectID>();
            var doNextRound = true;

            // If any ObjectStream can’t be loaded immediately because its stream length is saved in an ObjectStream, retry it later.
            // Repeat while not all object streams are load and object streams were still added in the last round, to retry loading
            // for objects streams that could not be loaded by now.
            while (objectStreamIDsToLoad.Count > 0 && doNextRound)
            {
                doNextRound = false;
                foreach (var objectStreamID in objectStreamIDsToLoad)
                {
                    if (!_objectStreamsWithParsers.ContainsKey(objectStreamID))
                    {
                        PdfReference? objectStreamReference = null;
                        objectStreamReference = _document.IrefTable[objectStreamID];
                        
                        // Suppress exceptions that may be caused by not yet available objects.
                        var suppressObjectOrderExceptions = new SuppressExceptions();

                        var objectStream = ReadObjectStream(objectStreamReference, suppressObjectOrderExceptions);
                        if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                        {
                            // Exceptional case: Reset objectStreamReference. Value back to null, because it could not be read
                            // properly and that may cause further errors.
                            objectStreamReference?.ResetObject();

                            // Try it again in the next round.
                            skippedObjectStreamIDs.Add(objectStreamID);

                            //Debug.WriteLine($"Loading ObjectStream with ID {objectStreamID} will be retried.");
                            PdfSharpLogHost.PdfReadingLogger.LogWarning($"Loading ObjectStream with ID {objectStreamID} will be retried.");

                            // Read next object.
                            continue;
                        }

                        var objectStreamParser = new Parser(_document, new MemoryStream(StructuralData(objectStream)), _documentParser);
                        _objectStreamsWithParsers.Add(objectStreamID, (objectStream, objectStreamParser));

                        // Read and add all References to objects residing in the object stream.
                        objectStream.ReadReferences(_document.IrefTable);

                        // Try a next round as long as at least one ObjectStream could be loaded.
                        doNextRound = true;
                    }
                }

                // Load skippedObjectStreamNumbers in the next round and reset skippedObjectStreamNumbers variable.
                objectStreamIDsToLoad = skippedObjectStreamIDs;
                skippedObjectStreamIDs = [];
            }

            if (objectStreamIDsToLoad.Any())
            {
                //Debug.WriteLine($"Loading ObjectStreams with IDs {String.Join(", ", objectStreamIDsToLoad)} finally failed.");
                PdfSharpLogHost.PdfReadingLogger.LogError($"Loading ObjectStreams with IDs {String.Join(", ", objectStreamIDsToLoad)} finally failed.");

                throw new ObjectNotAvailableException($"Could not load the ObjectStreams with the following ObjectNumbers: {String.Join(", ", objectStreamIDsToLoad)}. " +
                                                      $"Perhaps there is a cyclic reference between ObjectStreams, whose stream lengths are saved in an object inside the other ObjectStream.");
            }
#endif
        }

        PdfObjectStream ReadObjectStream(PdfReference? reference, SuppressExceptions? suppressObjectOrderExceptions)
        {
            if (reference == null)
                throw new PdfReaderException("An object stream named by the cross-reference data is missing from the file.");

            if (reference.Value == default!)
            {
                ReadIndirectObject(reference, suppressObjectOrderExceptions);
                if (SuppressExceptions.HasError(suppressObjectOrderExceptions))
                    return null!;
            }

            if (reference.Value is not PdfObjectStream pdfObjectStream)
            {
                // Only an untyped stream can become an object stream. Anything else (missing, not a stream, or an
                // object that already has a type, such as the catalog) means the cross-reference data is damaged;
                // converting it would destroy that object.
                if (reference.Value is not PdfDictionary { Stream: not null } dict || dict.GetType() != typeof(PdfDictionary))
                    throw new PdfReaderException($"Object {reference.ObjectNumber} is named as an object stream but is not one.");

                pdfObjectStream = new PdfObjectStream(dict, _documentParser);
                Debug.Assert(pdfObjectStream.Reference == reference);
            }

            return pdfObjectStream;
        }

        /// <summary>
        /// Reads the object stream header as pairs of integers from the beginning of the 
        /// stream of an object stream. Parameter first is the value of the First entry of
        /// the object stream object.
        /// </summary>
        internal int[][] ReadObjectStreamHeader(int n, int first)
        {
            // TODO_OLD: Concept for general error  handling.
            // If the stream is corrupted a lot of things can go wrong here.
            // Does it make sense to do a more detailed error checking?

            // Create n pairs of integers with object number and offset.
            int[][] header = new int[n][];
            for (int idx = 0; idx < n; idx++)
            {
                int number = ReadInteger();
                int offset = ReadInteger();
                if (number < 0 || offset < 0 || (long)offset + first > _lexer.PdfLength)
                    throw new PdfReaderException($"Object stream header entry '{number} {offset}' is invalid.");
                header[idx] = [number, offset + first];  // Absolute offset.
            }
            return header;
        }

        /// <summary>
        /// Rebuilds the cross-reference information from the objects in the file, for a file whose cross-reference
        /// table or stream cannot be read: a wrong 'startxref', offsets that point elsewhere, a missing table. Each
        /// 'N G obj' outside stream data gives the object's position, a later one replacing an earlier one as an
        /// incremental update does. Object streams supply the objects they hold (not in encrypted files, whose object
        /// streams cannot be read yet). /Root, /Info, /ID and /Encrypt each come from the newest trailer or cross-reference
        /// stream dictionary that has them; without a /Root that leads to an object, the last catalog found is used.
        /// Viewers repair files the same way.
        /// </summary>
        internal PdfTrailer RebuildCrossReferences()
        {
            var length = _lexer.PdfLength;
            if (length > int.MaxValue)
                throw new PdfReaderException("Files over 2 GiB are not rebuilt.");
            RepairedCrossReferences = true;
            foreach (var reference in _document.IrefTable.AllReferences.ToList())
                _document.IrefTable.Remove(reference);
            _compressedObjectLocations.Clear();
            _document.Trailer = null!;

            var text = _lexer.ScanRawString(0, (int)length);
            var found = FindObjects(text, out _, out var objectSpans, out var weakPositions);
            if (found.Count == 0)
                throw new PdfReaderException("The file holds no objects.");

            // Direct objects first, so that the trailer and the object streams can refer to them. The last version of
            // each object number in the file is the one an incremental update made current.
            // A weak copy (a header behind an 'endstream' found by searching, which may lie inside stream data) is used
            // only when the file has no other copy of that object number.
            var newest = new Dictionary<int, SizeType>();
            foreach (var item in found.Where(item => !weakPositions.Contains(item.Position)))
                newest[item.Number] = item.Position;
            foreach (var item in found.Where(item => weakPositions.Contains(item.Position) && !newest.ContainsKey(item.Number)).Reverse())
                newest[item.Number] = item.Position;
            var objects = found.Where(item => newest[item.Number] == item.Position).ToList();
            var rebuilt = new RebuiltObjects();
            foreach (var item in objects)
            {
                var objectID = new PdfObjectID(item.Number, item.Generation);
                _document.IrefTable.Add(PdfReference.CreateForObjectID(objectID, item.Position));
                rebuilt.Versions[item.Number] = item.Position;
                rebuilt.Current[item.Number] = objectID;
            }

            var trailer = RebuildTrailer(text, objects, objectSpans);
            if (trailer.Elements[PdfTrailer.Keys.Encrypt] is null)
            {
                foreach (var item in objects.Where(item => item.Kind == "/ObjStm"))
                    AddObjectStreamMembers(item, rebuilt);
            }
            else
            {
                PdfSharpLogHost.PdfReadingLogger.LogWarning("The file is encrypted; objects in its object streams are not rebuilt.");
            }

            // A reference the trailer names must lead to an object found in the file; a /Root that does not falls back
            // to the last catalog.
            if (trailer.Elements[PdfTrailer.Keys.Info] is { } info and not (PdfReference or PdfDictionary))
                trailer.Elements.Remove(PdfTrailer.Keys.Info);
            foreach (var key in new[] { PdfTrailer.Keys.Root, PdfTrailer.Keys.Info, PdfTrailer.Keys.Encrypt })
            {
                if (trailer.Elements[key] is PdfReference reference)
                {
                    if (_document.IrefTable[reference.ObjectID] is { } current)
                        trailer.Elements[key] = current;
                    else
                        trailer.Elements.Remove(key);
                }
            }
            if (trailer.Elements[PdfTrailer.Keys.Root] is not PdfReference)
            {
                var catalog = objects.LastOrDefault(item => item.Kind == "/Catalog");
                if (catalog.Number == 0)
                    throw new PdfReaderException("The file has no catalog.");
                trailer.Elements[PdfTrailer.Keys.Root] = _document.IrefTable[new PdfObjectID(catalog.Number, catalog.Generation)]!;
            }

            trailer.Elements.SetInteger(PdfTrailer.Keys.Size, rebuilt.Current.Keys.Max() + 1);
            _document.Trailer = trailer;
            _streamBoundaries = _document.IrefTable.AllReferences.Where(r => r.Position >= 0)
                .Select(r => r.Position).OrderBy(position => position).ToArray();
            PdfSharpLogHost.PdfReadingLogger.LogWarning("Rebuilt the cross-reference information from {Count} objects.", found.Count);
            return trailer;
        }

        // Where each object number was last registered while rebuilding: its file position, or the position of the
        // object stream that holds it, its current ID, and the object stream it was taken from.
        sealed class RebuiltObjects
        {
            public readonly Dictionary<int, SizeType> Versions = new();
            public readonly Dictionary<int, PdfObjectID> Current = new();
            public readonly Dictionary<int, PdfObjectID> MemberOf = new();
        }

        // The largest object number and generation PDF allows (ISO 32000-1 C.2); a larger 'N G obj' is not an object.
        const int MaxRebuiltObjectNumber = 8_388_607;
        const int MaxRebuiltGeneration = 65_535;

        // An object header that starts a line or follows 'endobj', as writers put them; the same text inside a string,
        // such as "see 2 0 obj", does not count. Inside an object, a header that follows its end on the same line is
        // found by the scan of the object body instead. A header at the start of a line inside a string spanning lines
        // still counts; the scan cannot tell it from a string that never closes.
        static readonly Regex ObjectHeaderPattern = new(
            @"(?<=(?:\A|[\r\n]|endobj)[\0\t\f ]*)([0-9]{1,10})[\0\t\n\f\r ]+([0-9]{1,5})[\0\t\n\f\r ]+obj(?![A-Za-z0-9])",
            RegexOptions.CultureInvariant);
        static readonly Regex AnchoredHeaderPattern = new(
            @"\G([0-9]{1,10})[\0\t\n\f\r ]+([0-9]{1,5})[\0\t\n\f\r ]+obj(?![A-Za-z0-9])", RegexOptions.CultureInvariant);
        static readonly Regex ObjectTypePattern = new(@"/Type\s*/(ObjStm|XRef|Catalog)\b", RegexOptions.CultureInvariant);
        static readonly Regex LengthPattern = new(
            @"/Length[\0\t\n\f\r ]+([0-9]{1,10})(?:[\0\t\n\f\r ]+([0-9]{1,5})[\0\t\n\f\r ]+R)?(?![0-9])",
            RegexOptions.CultureInvariant);
        static readonly Regex IntegerObjectPattern = new(
            @"(?<=(?:\A|[\r\n]|endobj)[\0\t\f ]*)([0-9]{1,10})[\0\t\n\f\r ]+[0-9]{1,5}[\0\t\n\f\r ]+obj[\0\t\n\f\r ]*([0-9]{1,10})[\0\t\n\f\r ]*endobj",
            RegexOptions.CultureInvariant);

        // The objects in the file, in file order: number, generation, position and the /Type that matters here.
        // Stream data is skipped, so that bytes inside it are not taken for objects, and so are strings and comments.
        // Every search is bounded by the next object header, so the work grows with the file size only. The spans of
        // objects that end with 'endobj' or a stream are returned too.
        static List<(int Number, int Generation, SizeType Position, string Kind)> FindObjects(string text,
            out List<(int Start, int End)> streamRanges, out List<(int Start, int End)> objectSpans, out HashSet<SizeType> weakPositions)
        {
            var result = new List<(int, int, SizeType, string)>();
            weakPositions = [];
            streamRanges = [];
            objectSpans = [];

            // Integer objects, so that an indirect /Length can say where stream data ends. Every value is kept, as an
            // update or an embedded file may have an integer object with the same number.
            var integers = new Dictionary<int, List<int>>();
            for (var integer = IntegerObjectPattern.Match(text); integer.Success; integer = integer.NextMatch())
            {
                if (int.TryParse(integer.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && int.TryParse(integer.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    if (!integers.TryGetValue(number, out var values))
                        integers.Add(number, values = []);
                    values.Add(value);
                }
            }

            // The next header at the start of a line is searched for again only once the scan has passed it: an earlier
            // result is still the first one at or after a later position. Searching on every object took quadratic time
            // when objects follow each other on one line.
            var lineStart = ObjectHeaderPattern.Match(text, 0);
            var match = lineStart;
            int noEndStreamFrom = Int32.MaxValue;
            while (match.Success)
            {
                int body = match.Index + match.Length;
                if (lineStart.Success && lineStart.Index < body)
                    lineStart = ObjectHeaderPattern.Match(text, body);
                var next = lineStart;
                int limit = next.Success ? next.Index : text.Length;
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    || number is < 1 or > MaxRebuiltObjectNumber
                    || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var generation)
                    || generation > MaxRebuiltGeneration)
                {
                    match = next;
                    continue;
                }

                ScanObjectBody(text, body, limit, out int endObj, out int stream, out int sameLineHeader);
                int dictionaryEnd;
                int resume;
                if (stream >= 0)
                {
                    dictionaryEnd = stream;
                    int endStream = FindStreamEnd(text, body, stream, integers, ref noEndStreamFrom, out bool byLength);
                    if (endStream >= 0)
                    {
                        streamRanges.Add((stream, endStream));
                        resume = endStream + 9;
                        objectSpans.Add((match.Index, resume));
                        // 'endobj' may be missing, with the next header right behind 'endstream'. When /Length did not
                        // locate that 'endstream', it may lie inside the stream data; such a header is checked below.
                        int after = SkipWhiteSpace(text, resume);
                        if (String.CompareOrdinal(text, after, "endobj", 0, 6) == 0)
                            after = SkipWhiteSpace(text, after + 6);
                        if (AnchoredHeaderPattern.Match(text, after) is { Success: true } following)
                        {
                            sameLineHeader = after;
                            next = following;
                            if (!byLength)
                                weakPositions.Add(after);
                        }
                    }
                    else
                    {
                        resume = limit;
                    }
                }
                else
                {
                    int end = limit;
                    if (endObj >= 0)
                        end = endObj;
                    else if (sameLineHeader >= 0)
                        end = sameLineHeader;
                    dictionaryEnd = end;
                    resume = endObj >= 0 ? endObj + 6 : end;
                    if (endObj >= 0)
                        objectSpans.Add((match.Index, resume));
                    else if (sameLineHeader >= 0)
                        next = AnchoredHeaderPattern.Match(text, sameLineHeader);
                }
                // A header behind an 'endstream' found by searching is weak when its body holds 'endstream' too: then it
                // lay inside stream data, followed by the rest of the stream. Otherwise it is an ordinary object.
                if (weakPositions.Contains(match.Index)
                    && text.IndexOf("endstream", body, dictionaryEnd - body, StringComparison.Ordinal) < 0)
                {
                    weakPositions.Remove(match.Index);
                }
                var dictionary = text.Substring(body, Math.Min(dictionaryEnd - body, 64 * 1024));
                var kind = ObjectTypePattern.Match(dictionary) is { Success: true } typeMatch ? "/" + typeMatch.Groups[1].Value : "";
                result.Add((number, generation, match.Index, kind));

                // The next header was found from the dictionary on; when it lies inside this object's stream data, look
                // again behind the data.
                if (next.Success && next.Index < resume)
                {
                    if (lineStart.Success && lineStart.Index < resume)
                        lineStart = ObjectHeaderPattern.Match(text, resume);
                    next = lineStart;
                }
                match = next;
            }
            return result;
        }

        // Finds 'endobj', the 'stream' keyword, or the header of a next object that follows a missing 'endobj' on the
        // same line ('>> 3 0 obj'), in the object whose body starts at 'from', skipping literal strings and comments,
        // up to 'limit' (the next header at the start of a line). -1 for what is not found.
        static void ScanObjectBody(string text, int from, int limit, out int endObj, out int stream, out int nextHeader)
        {
            endObj = -1;
            stream = -1;
            nextHeader = -1;
            int index = from;
            while (index < limit)
            {
                char ch = text[index];
                if (ch is >= '0' and <= '9' && index > from && text[index - 1] is '\0' or '\t' or '\n' or '\f' or '\r' or ' ' or '>' or ']' or ')'
                    && AnchoredHeaderPattern.Match(text, index).Success)
                {
                    nextHeader = index;
                    return;
                }
                switch (ch)
                {
                    case '(':
                        index = SkipLiteralString(text, index, limit);
                        continue;

                    case '%':
                        while (index < limit && text[index] is not ('\n' or '\r'))
                            index++;
                        continue;

                    case 'e' when String.CompareOrdinal(text, index, "endobj", 0, 6) == 0:
                        endObj = index;
                        return;

                    case 's' when String.CompareOrdinal(text, index, "stream", 0, 6) == 0 && IsStreamKeyword(text, from, index):
                        stream = index;
                        return;
                }
                index++;
            }
        }

        // The position behind a literal string that starts at 'start', honouring nested parentheses and escapes; a
        // string that does not close ends at 'limit'.
        static int SkipLiteralString(string text, int start, int limit) => SkipLiteralString(text, start, limit, out _);

        static int SkipLiteralString(string text, int start, int limit, out bool closed)
        {
            closed = false;
            int depth = 0;
            for (int index = start; index < limit; index++)
            {
                char ch = text[index];
                if (ch == '\\')
                {
                    index++;
                }
                else if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closed = true;
                        return index + 1;
                    }
                }
            }
            return limit;
        }

        static int SkipWhiteSpace(string text, int index)
        {
            while (index < text.Length && text[index] is '\0' or '\t' or '\n' or '\f' or '\r' or ' ')
                index++;
            return index;
        }

        // The 'stream' keyword: after the dictionary's '>>' and white space, and followed by an end-of-line marker,
        // which some writers precede with spaces; names such as /BitstreamVera and the end of 'endstream' do not count.
        static bool IsStreamKeyword(string text, int from, int at)
        {
            int before = at - 1;
            while (before >= from && text[before] is '\0' or '\t' or '\n' or '\f' or '\r' or ' ')
                before--;
            if (before < from || text[before] != '>')
                return false;
            int after = at + 6;
            while (after < text.Length && text[after] is ' ' or '\t')
                after++;
            return after < text.Length && text[after] is '\n' or '\r';
        }

        // The 'endstream' that ends the data behind a 'stream' keyword: where /Length says (directly, or through an
        // integer object), when 'endstream' follows there, else the first one, as stream data may itself contain PDF
        // text such as an embedded file. Once no 'endstream' follows a position, none follows any later one either.
        static int FindStreamEnd(string text, int dictionaryStart, int stream, Dictionary<int, List<int>> integers, ref int noEndStreamFrom,
            out bool byLength)
        {
            byLength = false;
            int data = stream + 6;
            while (data < text.Length && text[data] is ' ' or '\t')
                data++;
            if (data < text.Length && text[data] == '\r')
                data++;
            if (data < text.Length && text[data] == '\n')
                data++;
            var length = LengthPattern.Match(text, dictionaryStart, stream - dictionaryStart);
            if (length.Success && int.TryParse(length.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                // A direct length, or every value an integer object of that number has, newest first.
                IEnumerable<int> counts = [value];
                if (length.Groups[2].Success)
                    counts = integers.TryGetValue(value, out var values) ? Enumerable.Reverse(values) : [];
                foreach (var bytes in counts)
                {
                    if (bytes < 0 || bytes > text.Length - data)
                        continue;
                    int end = SkipWhiteSpace(text, data + bytes);
                    if (String.CompareOrdinal(text, end, "endstream", 0, 9) == 0)
                    {
                        byLength = true;
                        return end;
                    }
                }
            }
            if (data >= noEndStreamFrom)
                return -1;
            int found = text.IndexOf("endstream", data, StringComparison.Ordinal);
            if (found < 0)
                noEndStreamFrom = data;
            return found;
        }

        PdfTrailer RebuildTrailer(string text, List<(int Number, int Generation, SizeType Position, string Kind)> objects,
            List<(int Start, int End)> objectSpans)
        {
            // The entries come from the newest trailer or cross-reference stream dictionary that has a /Root, so that
            // they belong to one revision of one file; a linearized file's last trailer holds only /Size and is passed
            // over. Only /ID may come from an older dictionary, when the chosen one encrypts but lacks it.
            var sources = new List<(SizeType Position, bool IsXRefStream, int Number, int Generation)>();
            var keywords = new List<int>();
            for (int at = text.IndexOf("trailer", StringComparison.Ordinal); at >= 0; at = text.IndexOf("trailer", at + 7, StringComparison.Ordinal))
            {
                if (IsTrailerKeyword(text, at) && !IsInsideRange(objectSpans, at))
                    keywords.Add(at);
            }
            // A trailer dictionary must close before the next 'trailer' keyword, except within a comment or a string of
            // its own (some slack is allowed there); checking that first keeps junk such as a run of 'trailer << ('
            // from being parsed to the end of the file each time. A keyword inside an accepted dictionary is no trailer.
            int coveredUntil = -1;
            for (int index = 0; index < keywords.Count; index++)
            {
                if (keywords[index] < coveredUntil)
                    continue;
                int start = SkipWhiteSpace(text, keywords[index] + 7);
                int close = DictionaryEnd(text, start, keywords);
                if (close >= 0)
                {
                    sources.Add((keywords[index], false, 0, 0));
                    coveredUntil = close;
                }
            }
            foreach (var item in objects.Where(item => item.Kind == "/XRef"))
                sources.Add((item.Position, true, item.Number, item.Generation));

            var trailer = new PdfTrailer(_document);
            bool chosen = false;
            var readable = new List<PdfDictionary>();
            foreach (var source in sources.OrderByDescending(source => source.Position))
            {
                PdfDictionary? dictionary = null;
                try
                {
                    if (source.IsXRefStream)
                    {
                        var reference = _document.IrefTable[new PdfObjectID(source.Number, source.Generation)];
                        if (reference != null)
                            dictionary = ReadIndirectObject(reference, null, true) as PdfDictionary;
                    }
                    else
                    {
                        _lexer.Position = source.Position;
                        if (ScanNextToken() == Symbol.Trailer && ScanNextToken() == Symbol.BeginDictionary)
                        {
                            dictionary = new PdfDictionary(_document);
                            ReadDictionary(dictionary, false);
                        }
                    }
                }
                catch (PdfReaderException ex) when (!IsLimitExceeded(ex))
                {
                    // An unreadable one is skipped; an older one may do. One FreePDF refuses for its limits is not skipped:
                    // the trailer chosen instead, or the last catalog, could belong to another revision or to none.
                    dictionary = null;
                }
                if (dictionary == null)
                    continue;
                readable.Add(dictionary);
                if (!chosen)
                {
                    if (dictionary.Elements[PdfTrailer.Keys.Root] == null)
                        continue;
                    foreach (var key in new[] { PdfTrailer.Keys.Root, PdfTrailer.Keys.Info, PdfTrailer.Keys.ID, PdfTrailer.Keys.Encrypt })
                        CopyEntry(dictionary, key);
                    chosen = true;
                    if (trailer.Elements[PdfTrailer.Keys.Encrypt] == null || trailer.Elements[PdfTrailer.Keys.ID] != null)
                        break;
                }
                else if (dictionary.Elements[PdfTrailer.Keys.ID] != null)
                {
                    CopyEntry(dictionary, PdfTrailer.Keys.ID);
                    break;
                }
            }
            // No dictionary names a /Root, so the last catalog will. /Info comes from the newest dictionary that has one,
            // /Encrypt with its /ID from the newest that encrypts (an update trailer may hold only /Size), and /ID
            // otherwise from the newest that has one.
            if (!chosen)
            {
                // Only dictionaries with /Size, as trailers and cross-reference streams have; others may be text in a
                // comment or a stray dictionary.
                readable.RemoveAll(dictionary => dictionary.Elements[PdfTrailer.Keys.Size] == null);
                if (readable.FirstOrDefault(dictionary => dictionary.Elements[PdfTrailer.Keys.Info] != null) is { } withInfo)
                    CopyEntry(withInfo, PdfTrailer.Keys.Info);
                if (readable.FirstOrDefault(dictionary => dictionary.Elements[PdfTrailer.Keys.Encrypt] != null) is { } withEncrypt)
                {
                    CopyEntry(withEncrypt, PdfTrailer.Keys.Encrypt);
                    CopyEntry(withEncrypt, PdfTrailer.Keys.ID);
                }
                if (trailer.Elements[PdfTrailer.Keys.ID] == null
                    && readable.FirstOrDefault(dictionary => dictionary.Elements[PdfTrailer.Keys.ID] != null) is { } withID)
                {
                    CopyEntry(withID, PdfTrailer.Keys.ID);
                }
            }
            return trailer;

            void CopyEntry(PdfDictionary dictionary, string key)
            {
                if (dictionary.Elements[key] is { } value)
                    trailer.Elements[key] = value is PdfObject { IsIndirect: false } direct ? direct.Clone() : value;
            }
        }

        // The position behind the '>>' that closes the dictionary starting at 'start', or -1 if it does not close
        // before the next 'trailer' keyword. Strings and comments are skipped. One that crosses that keyword (it may
        // hold the text 'trailer <<') may go on within a fixed 4 KiB budget, and the bound becomes the next keyword
        // behind it; one that runs over 'startxref' or '%%EOF' belongs to junk, not to a trailer. The fixed budget
        // keeps the work per keyword bounded, also on hostile runs of strings and comments.
        static int DictionaryEnd(string text, int start, List<int> keywords)
        {
            const int slack = 4096;
            int end = NextKeyword(start);
            int limit = (int)Math.Min((long)end + slack, text.Length);
            int depth = 0;
            for (int index = start; index < end; index++)
            {
                char ch = text[index];
                if (ch == '(')
                {
                    int behind = SkipLiteralString(text, index, limit, out bool closed);
                    if (!closed && limit < text.Length)
                        return -1;
                    if (behind > end)
                    {
                        if (CrossesFileEnd(end, behind))
                            return -1;
                        end = Math.Min(NextKeyword(behind), limit);
                    }
                    index = behind - 1;
                }
                else if (ch == '%')
                {
                    int lineEnd = index;
                    while (lineEnd < limit && text[lineEnd] is not ('\n' or '\r'))
                        lineEnd++;
                    if (lineEnd >= limit && limit < text.Length)
                        return -1;
                    if (lineEnd > end)
                    {
                        if (CrossesFileEnd(end, lineEnd))
                            return -1;
                        end = Math.Min(NextKeyword(lineEnd), limit);
                    }
                    index = lineEnd;
                }
                else if (ch == '<' && index + 1 < text.Length && text[index + 1] == '<')
                {
                    depth++;
                    index++;
                }
                else if (ch == '>' && index + 1 < text.Length && text[index + 1] == '>')
                {
                    depth--;
                    index++;
                    if (depth <= 0)
                        return index + 1;
                }
            }
            return -1;

            // The first keyword at or after a position, or the end of the text.
            int NextKeyword(int position)
            {
                int found = keywords.BinarySearch(position);
                if (found < 0)
                    found = ~found;
                return found < keywords.Count ? keywords[found] : text.Length;
            }

            bool CrossesFileEnd(int from, int to)
                => text.IndexOf("startxref", from, to - from, StringComparison.Ordinal) >= 0
                   || text.IndexOf("%%EOF", from, to - from, StringComparison.Ordinal) >= 0;
        }

        // 'trailer' as a keyword: after a delimiter or white space and before '<<'.
        static bool IsTrailerKeyword(string text, int at)
        {
            if (at > 0 && text[at - 1] is not ('\0' or '\t' or '\n' or '\f' or '\r' or ' ' or '>' or ']' or ')'))
                return false;
            int after = at + 7;
            while (after < text.Length && text[after] is '\0' or '\t' or '\n' or '\f' or '\r' or ' ')
                after++;
            return String.CompareOrdinal(text, after, "<<", 0, 2) == 0;
        }

        // Whether a position lies strictly inside one of the ranges, which are disjoint and in file order.
        static bool IsInsideRange(List<(int Start, int End)> ranges, int position)
        {
            int low = 0, high = ranges.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                var range = ranges[middle];
                if (position <= range.Start)
                    high = middle - 1;
                else if (position >= range.End)
                    low = middle + 1;
                else
                    return true;
            }
            return false;
        }

        // Registers the objects an object stream holds, unless a later version of the object comes after the stream.
        void AddObjectStreamMembers((int Number, int Generation, SizeType Position, string Kind) item, RebuiltObjects rebuilt)
        {
            var streamID = new PdfObjectID(item.Number, item.Generation);
            var reference = _document.IrefTable[streamID];
            if (reference == null)
                return;
            try
            {
                if (ReadIndirectObject(reference, null, true) is not PdfDictionary objectStream || objectStream.Stream == null)
                    return;
                int count = objectStream.Elements.GetInteger("/N");
                var lexer = new Lexer(new MemoryStream(StructuralData(objectStream)), _logger);
                for (int index = 0; index < count; index++)
                {
                    if (lexer.ScanNextToken(false) != Symbol.Integer)
                        break;
                    int number = lexer.TokenToInteger;
                    if (lexer.ScanNextToken(false) != Symbol.Integer)
                        break;
                    if (number is < 1 or > MaxRebuiltObjectNumber || number == item.Number)
                        continue;
                    if (rebuilt.Versions.TryGetValue(number, out var newer) && newer > item.Position)
                        continue;
                    if (rebuilt.Current.TryGetValue(number, out var olderID) && _document.IrefTable[olderID] is { } older)
                        _document.IrefTable.Remove(older);
                    var objectID = new PdfObjectID(number);
                    _document.IrefTable.Add(PdfReference.CreateForObjectID(objectID, -1));
                    if (rebuilt.MemberOf.TryGetValue(number, out var previousStream)
                        && _compressedObjectLocations.TryGetValue(previousStream, out var previousLocations))
                    {
                        previousLocations.Remove(objectID);
                    }
                    if (!_compressedObjectLocations.TryGetValue(streamID, out var locations))
                        _compressedObjectLocations.Add(streamID, locations = new());
                    locations[objectID] = index;
                    rebuilt.Versions[number] = item.Position;
                    rebuilt.Current[number] = objectID;
                    rebuilt.MemberOf[number] = streamID;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && !IsLimitExceeded(ex))
            {
                PdfSharpLogHost.PdfReadingLogger.LogWarning("Object stream {ObjectID} could not be read while rebuilding: {Message}", streamID, ex.Message);
            }
            finally
            {
                // It is read again with the other objects.
                reference.ResetObject();
            }
        }

        /// <summary>
        /// Finds the last 'startxref' before the end, searching backwards in blocks; -1 if there is none. The file was
        /// read into one string, which took about five times its size in memory and refused files over 2 GiB.
        /// </summary>
        SizeType FindLastStartXRef(SizeType end)
        {
            const string keyword = "startxref";
            const int blockSize = 64 * 1024;
            while (end > 0)
            {
                SizeType start = Math.Max(0, end - blockSize);
                var text = _lexer.ScanRawString(start, (int)(end - start));
                int index = text.LastIndexOf(keyword, StringComparison.Ordinal);
                if (index >= 0)
                    return start + index;
                if (start == 0)
                    break;
                // The next block overlaps this one, so a keyword across the border is found.
                end = start + keyword.Length - 1;
            }
            return -1;
        }

        /// <summary>
        /// Reads the cross-reference table(s) and their trailer dictionary or
        /// cross-reference streams.
        /// </summary>
        internal PdfTrailer ReadTrailer()
        {
            SizeType length = _lexer.PdfLength;

            // Implementation note 18 Appendix  H:
            // Acrobat viewers require only that the %%EOF marker appear somewhere within the last 1024 bytes of the file.
            // A damaged input can be shorter than the usual trailer window.
            // Never seek before the file or to the -1 returned for a missing marker.
            SizeType tailStart = Math.Max(0, length - 1030);
            string tail = _lexer.ScanRawString(tailStart, (int)(length - tailStart));
            int idx = tail.LastIndexOf("startxref", StringComparison.Ordinal);
            if (idx >= 0)
                _lexer.Position = tailStart + idx;

            // SAP sometimes creates files with a size of several MByte and places 'startxref' somewhere in the middle...
            if (idx == -1)
            {
                PdfSharpLogHost.Logger.LogError("Cannot find 'startxref' within the last 1024 bytes of the PDF file.");
                var position = FindLastStartXRef(length);
                if (position < 0)
                    throw new PdfReaderException("The StartXRef table could not be found, the file cannot be opened.");
                _lexer.Position = position;
            }

            ReadSymbol(Symbol.StartXRef);
            // Read position behind 'startxref'.
            long xrefPosition = ReadSize();
            StartXRef = xrefPosition;

            // Read all trailers.
            PdfTrailer? newerTrailer = null;
            var visitedXrefs = new HashSet<long>();
            var hybridStreams = new HashSet<long>();
            while (true)
            {
                if (xrefPosition < 0 || xrefPosition >= length)
                    throw new PdfReaderException("Invalid cross-reference offset outside the PDF file.");
                if (!visitedXrefs.Add(xrefPosition))
                    throw new PdfReaderException("Cyclic cross-reference chain in the PDF trailer.");
                _lexer.Position = (SizeType)xrefPosition;
                var trailer = ReadXRefTableAndTrailer(_document.IrefTable)
                    ?? throw new PdfReaderException("Invalid cross-reference section.");

                // A hybrid revision supplements its table with an XRefStm before
                // consulting Prev. Its stream's Prev entry is not a second chain.
                if (trailer is not PdfCrossReferenceStream && trailer.Elements.ContainsKey("/XRefStm"))
                {
                    var hybridPosition = trailer.Elements.GetLongInteger("/XRefStm");
                    if (hybridPosition < 0 || hybridPosition >= length)
                        throw new PdfReaderException("Invalid hybrid cross-reference offset.");
                    if (hybridStreams.Add(hybridPosition))
                    {
                        _lexer.Position = (SizeType)hybridPosition;
                        if (ScanNextToken() != Symbol.Integer)
                            throw new PdfReaderException("Invalid hybrid cross-reference stream.");
                        ReadXRefStream(_document.IrefTable);
                    }
                }

                // Return the first found trailer, which is the one 'startxref' points to.
                // This is the current trailer, even for incrementally updated files.
                if (_document.Trailer == null!)
                    _document.Trailer = trailer;

                // Add previous trailer to the newerTrailer, if existing.
                if (newerTrailer != null)
                    newerTrailer.PreviousTrailer = trailer;

                // Break if there is no previous trailer.
                long prev = trailer.Elements.GetLongInteger(PdfTrailer.Keys.Prev);
                if (prev == 0)
                    break;

                // Continue loading previous trailer and cache this one as the newerTrailer to add its previous trailer.
                xrefPosition = prev;
                newerTrailer = trailer;
            }
            _streamBoundaries = _document.IrefTable.AllReferences.Where(r => r.Position >= 0)
                .Select(r => r.Position).OrderBy(position => position).ToArray();
            return _document.Trailer;
        }

        /// <summary>
        /// Reads cross-reference table(s) and trailer(s).
        /// </summary>
        PdfTrailer? ReadXRefTableAndTrailer(PdfCrossReferenceTable xrefTable)
        {
            Debug.Assert(xrefTable != null);

            var symbol = ScanNextToken();

            if (symbol == Symbol.XRef)
            {
                // Case: Entry is it a cross-reference table.
                // Reference: 3.4.3  Cross-Reference Table / Page 93
                while (true)
                {
                    symbol = ScanNextToken();
                    if (symbol == Symbol.Integer)
                    {
                        int start = _lexer.TokenToInteger;
                        int length = ReadInteger();
                        for (int id = start; id < start + length; id++)
                        {
                            SizeType position = ReadSize();
                            int generation = ReadInteger();
                            ReadSymbol(Symbol.Keyword);
                            string token = _lexer.Token;
                            // Skip start entry.
                            if (id == 0)
                                continue;
                            // Skip unused entries.
                            if (token != "n")
                                continue;

                            int idToUse = id;
#if true
                            // The following issue in PDF files is rare, but must be fixed here to prevent PdfReference with wrong IDs.
                            // We found PDF files where the ID of the referenced object was misaligned by one relative to
                            // its number from the xref table. 
                            // Check if the object at the address has the correct ID and generation.
                            if (!CheckXRefTableEntry(position, id, generation, out var idChecked, out var generationChecked))
                            {
                                RepairedCrossReferences = true;
                                // Found the keyword "obj", but ID or generation did not match.
                                // There is a producer app where ID is off by one.
                                // In this case we use the ID from the object, not the ID from the XRef table.
                                if (generation == generationChecked && id == idChecked + 1)
                                {
                                    idToUse = idChecked;
                                }
                                else
                                {
                                    // File is corrupt, but try to recover it by using the ID we found at the location.
                                    idToUse = idChecked;
                                    //ParserDiagnostics.ThrowParserException("Invalid entry in XRef table, ID=" + id + ", Generation=" + generation + ", Position=" + position + ", ID of referenced object=" + idChecked + ", Generation of referenced object=" + generationChecked);  // TODO_OLD L10N using PsMsgs
                                }
                                var message = id == idChecked ?
                                    Invariant($"Object ID/generation mismatch: Object at position {position} has ID '{id} {generation}' according to xref table and ID '{idChecked} {generationChecked}' at its position of file.") :
                                    Invariant($"Object ID mismatch: Object at position {position} has ID '{id}' according to xref table and ID '{idChecked}' at its position of file.");
                                PdfSharpLogHost.Logger.LogError(message);
                            }
#endif
                            // Even if it is restricted, an object can exist in more than one subsection.
                            // (PDF Reference Implementation Notes 15).
                            var objectID = new PdfObjectID(idToUse, generation);
                            // Ignore the latter one.
                            if (xrefTable.Contains(objectID))
                                continue;

                            var iref = PdfReference.CreateForObjectID(objectID, position);
                            xrefTable.Add(iref);
                        }
                    }
                    else if (symbol == Symbol.Trailer)
                    {
                        ReadSymbol(Symbol.BeginDictionary);
                        var trailer = new PdfTrailer(_document);
                        ReadDictionary(trailer, false);
                        return trailer;
                    }
                    else
                        ParserDiagnostics.HandleUnexpectedToken(_lexer.Token, _lexer.DumpNeighborhoodOfPosition());
                }
            }
            // ReSharper disable once RedundantIfElseBlock because of code readability.
            else if (symbol == Symbol.Integer)
            {
                // Case: Entry is a cross-reference stream.
                // Reference: 3.4.7  Cross-Reference Streams / Page 93
                // TODO_OLD: We have not yet tested PDF files larger than 2 GiB because we have none and cannot produce one.

                // The parsed integer is the object ID of the cross-reference stream object.
                return ReadXRefStream(xrefTable);
            }
            return null;
        }

        /// <summary>
        /// Checks the x reference table entry. Returns true if everything is correct.
        /// Returns false if the keyword "obj" was found, but ID or Generation are incorrect.
        /// Throws an exception otherwise.
        /// </summary>
        /// <param name="position">The position where the object is supposed to be.</param>
        /// <param name="id">The ID from the XRef table.</param>
        /// <param name="generation">The generation from the XRef table.</param>
        /// <param name="idChecked">The identifier found in the PDF file.</param>
        /// <param name="generationChecked">The generation found in the PDF file.</param>
        bool CheckXRefTableEntry(SizeType position, int id, int generation, out int idChecked, out int generationChecked)
        {
            // We found a lot of PDF files with wrong position in xref table:
            // …〈LF〉42 0 obj〈LF〉…
            //           ^
            // The offsets points behind the numbers.
            // Should we handle this wrong case?
            SizeType origin = _lexer.Position;
            idChecked = -1;
            generationChecked = -1;
            try
            {
                _lexer.Position = position;
                idChecked = ReadInteger();
                generationChecked = ReadInteger();
                //// TODO_OLD Should we use ScanKeyword here?
                //ReadKSymbol(Symbol.Keyword);
                //string token = _lexer.Token;
                Symbol symbol = _lexer.ScanNextToken(false);
                if (symbol != Symbol.Obj)
                    ParserDiagnostics.ThrowParserException(Invariant($"Invalid entry in XRef table, ID={id} {generation} at position={position}")); // TODO_OLD L10N using PsMsgs

                if (id != idChecked || generation != generationChecked)
                    return false;
            }
            catch (PdfReaderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ParserDiagnostics.ThrowParserException("Invalid entry in XRef table, ID=" + id + ", Generation=" + generation + ", Position=" + position, ex); // TODO_OLD L10N using PsMsgs
            }
            finally
            {
                _lexer.Position = origin;
            }
            return true;
        }

        /// <summary>
        /// Reads cross-reference stream(s).
        /// </summary>
        PdfTrailer ReadXRefStream(PdfCrossReferenceTable xrefTable)
        {
            // Read cross-reference stream.
            //Debug.Assert(_lexer.Symbol == Symbol.Integer);

            var xrefStart = _lexer.Position - _lexer.Token.Length;

            int number = _lexer.TokenToInteger;
            int generation = ReadInteger();
            // According to specs, generation number "shall not" be "other than zero".
            // Debug.Assert(generation == 0);
            if (generation != 0)
            {
                // Considered to be an error, but without consequences.
                PdfSharpLogHost.Logger.LogError(
                    $"Generation number of object '{number} {generation}' " +
                    "which is cross-reference stream shall not be other than zero.");
            }

            // Reference 2.0: 7.5.7  Object streams / Page 61
            // Quote
            // "The following objects shall not be stored in an object stream: [...]
            // Objects with a generation number other than zero"

            ReadSymbol(Symbol.Obj);
            ReadSymbol(Symbol.BeginDictionary);
            var objectID = new PdfObjectID(number, generation);

            var xrefStream = new PdfCrossReferenceStream(_document);

            ReadDictionary(xrefStream, false);
            ReadSymbol(Symbol.BeginStream);
#if true
            ReadDictionaryStream(xrefStream, null);
#else
            ReadStreamFromXRefTable(xrefStream);
#endif

            // Usually, the cross-reference stream and its reference have not been read yet.
            if (!xrefTable.Contains(objectID))
            {
                var iref = PdfReference.CreateFromObject(xrefStream, objectID, xrefStart);
                xrefTable.Add(iref);
            }
            // If a cross-reference stream B is referenced in the /Prev key of another cross-reference stream A dictionary,
            // it doesn’t have to be referenced in the cross-reference stream A itself.
            // But if it is, its reference has been already created, when reading all the cross-reference stream A
            // references. So we reuse this reference here, but set the value,
            // as the object itself has not been read, when reading the references.
            else
            {
                var oldIref = xrefTable.AllReferences.First(x => x.ObjectID == objectID);
                if (oldIref.Position == xrefStart)
                {
                    oldIref.Value = xrefStream;
                }
                else
                {
                    // A newer revision gave the number to another object, as incremental updates may (ISO 32000-1
                    // 7.5.6); that object stands. The older cross-reference stream replaced it, so a form saved that
                    // way was lost.
                    PdfSharpLogHost.PdfReadingLogger.LogInformation("Object '{ObjectID}' at position {Position} is a newer revision's object; " +
                                                                    "the older cross-reference stream at position {XRefStart} keeps no reference.",
                        oldIref.ObjectID, oldIref.Position, xrefStart);
                }
            }

            Debug.Assert(xrefStream.Stream != null);
            //string sValue = new RawEncoding().GetString(xrefStream.Stream.UnfilteredValue,);
            //_ = typeof(int);
            byte[] bytes;
            try
            {
                bytes = StructuralData(xrefStream);
            }
            catch (Exception ex) when (ex is not PdfReaderException)
            {
                // The file's structure is in this data, so a filter that is unknown, unsupported or fails on it
                // means the file is damaged.
                throw new PdfReaderException($"The cross-reference stream at offset {xrefStart} cannot be decoded: {ex.Message}", ex);
            }

            // The dictionary must describe the data (ISO 32000-1 7.5.8.2): /Size, /W with three widths and optional
            // /Index pairs, all direct non-negative integers, and enough data for every entry. Anything else is a
            // damaged file. May look like this: W[1 2 1] ¤ Index[7 12] ¤ Size 19
            static int Count(PdfItem? item, string key)
            {
                if (item is PdfInteger { Value: >= 0 } integer)
                    return integer.Value;
                throw new PdfReaderException($"The {key} entry of a cross-reference stream is invalid: '{item?.ToString() ?? "(no value)"}'.");
            }

            int size = Count(xrefStream.Elements.GetValue(PdfCrossReferenceStream.Keys.Size, VCF.NoTransform), "/Size");
            var index = xrefStream.Elements.GetValue(PdfCrossReferenceStream.Keys.Index, VCF.NoTransform);
            var w = xrefStream.Elements.GetValue(PdfCrossReferenceStream.Keys.W, VCF.NoTransform) as PdfArray;
            if (w is not { Elements.Count: 3 })
                throw new PdfReaderException($"The /W entry of a cross-reference stream is invalid: '{w?.ToString() ?? "(no value)"}'.");
            if (xrefStream.Elements.GetValue(PdfCrossReferenceStream.Keys.Prev, VCF.NoTransform) is { } prev and not PdfInteger { Value: >= 0 })
                throw new PdfReaderException($"The /Prev entry of a cross-reference stream is invalid: '{prev}'.");

            // Setup subsections.
            int subsectionCount;
            int[][] subsections;
            long subsectionEntryCount = 0;
            if (index == null)
            {
                // Default: one subsection for objects 0 to /Size - 1.
                subsectionCount = 1;
                subsections = new int[subsectionCount][];
                subsections[0] = [0, size];
                subsectionEntryCount = size;
            }
            else if (index is PdfArray { Elements.Count: > 0 } indexArray && indexArray.Elements.Count % 2 == 0)
            {
                subsectionCount = indexArray.Elements.Count / 2;
                subsections = new int[subsectionCount][];
                for (int idx = 0; idx < subsectionCount; idx++)
                {
                    subsections[idx] = [Count(indexArray.Elements[2 * idx], "/Index"), Count(indexArray.Elements[2 * idx + 1], "/Index")];
                    subsectionEntryCount += subsections[idx][1];
                }
            }
            else
                throw new PdfReaderException($"The /Index entry of a cross-reference stream is invalid: '{index}'.");

            int[] wsize = [Count(w.Elements[0], "/W"), Count(w.Elements[1], "/W"), Count(w.Elements[2], "/W")];
            long wsum = (long)wsize[0] + wsize[1] + wsize[2];
            if (wsum == 0 || wsum * subsectionEntryCount > bytes.Length)
                throw new PdfReaderException(
                    $"A cross-reference stream has {bytes.Length} bytes of data, but its /W and /Index describe {subsectionEntryCount} entries of {wsum} bytes.");
#if DEBUG_
            if (wsum * subsectionEntryCount != bytes.Length)
                _ = typeof(int);
#endif
            // BUG_OLD: This assertion fails with original PDF 2.0 documentation (ISO_32000-2_2020(en).pdf)
            //Debug.Assert(wsum * subsectionEntryCount == bytes.Length, "Check implementation here.");
#if DEBUG_ && CORE
            if (PdfDiagnostics.TraceXrefStreams)
            {
                int testcount = subsections[0][1];
                int[] currentSubsection = subsections[0];
                for (int idx = 0; idx < testcount; idx++)
                {
                    uint field1 = StreamHelper.ReadBytes(bytes, idx * wsum, wsize[0]);
                    uint field2 = StreamHelper.ReadBytes(bytes, idx * wsum + wsize[0], wsize[1]);
                    uint field3 = StreamHelper.ReadBytes(bytes, idx * wsum + wsize[0] + wsize[1], wsize[2]);
                    string res = String.Format("{0,2:00}: {1} {2,5} {3}  // ", idx, field1, field2, field3);
                    switch (field1)
                    {
                        case 0:
                            res += "Free list entry: object number, generation number";
                            break;

                        case 1:
                            res += "Not compressed: offset, generation number";
                            break;

                        case 2:
                            res += "Compressed: object stream object number, index in stream";
                            break;

                        default:
                            res += "??? Type undefined";
                            break;
                    }
                    Debug.WriteLine(res);
                }
            }
#endif
            int index2 = -1;
            for (int ssc = 0; ssc < subsectionCount; ssc++)
            {
                int abc = subsections[ssc][1];
                for (int idx = 0; idx < abc; idx++)
                {
                    index2++;

                    // Fits in int: wsum * entries is at most bytes.Length, checked above.
                    int start = index2 * (int)wsum;
                    PdfCrossReferenceStream.CrossReferenceStreamEntry item = new()
                    {
                        // ISO 32000-1 Table 17: without a type field (width 0), every entry is type 1.
                        Type = StreamHelper.EntryType(bytes, start, wsize[0]),
                        Field2 = StreamHelper.ReadBytes(bytes, start + wsize[0], wsize[1]),
                        Field3 = StreamHelper.ReadBytes(bytes, start + wsize[0] + wsize[1], wsize[2])
                    };

                    xrefStream.Entries.Add(item);

                    switch (item.Type)
                    {
                        case 0:
                            // Nothing to do, not needed.
                            break;

                        case 1: // offset / generation number
                            //// Even if it is restricted, an object can exist in more than one subsection.
                            //// (PDF Reference Implementation Notes 15).

                            SizeType position = (SizeType)item.Field2;
                            objectID = ReadObjectNumber(position);
#if DEBUG_
                            if (objectID.ObjectNumber == 1074)
                                _ = typeof(int);
#endif
                            // TODO Sometimes fails.
                            //Debug.Assert(objectID.GenerationNumber == item.Field3);

#if DEBUG
                            if (objectID.ObjectNumber == 96049)
                                _ = typeof(int);
#endif

                            // Ignore the latter one.
                            if (!xrefTable.Contains(objectID))
                            {
                                // Add iref for all uncompressed objects.
                                var iref = PdfReference.CreateForObjectID(objectID, position);
                                xrefTable.Add(iref);
                            }
#if DEBUG_
                            else
                            {
                                _ = typeof(int);
                            }
#endif
                            break;

                        case 2:
                            // Reserve the current compressed version now, before
                            // older xref sections can supply a file-level version.
                            var compressedId = new PdfObjectID(subsections[ssc][0] + idx);
                            if (!xrefTable.Contains(compressedId))
                            {
                                xrefTable.Add(PdfReference.CreateForObjectID(compressedId, -1));
                                if (item.Field2 > int.MaxValue || item.Field3 > int.MaxValue)
                                    throw new PdfReaderException("Invalid compressed object location.");
                                var streamId = new PdfObjectID((int)item.Field2);
                                if (!_compressedObjectLocations.TryGetValue(streamId, out var locations))
                                    _compressedObjectLocations.Add(streamId, locations = new());
                                locations.Add(compressedId, (int)item.Field3);
                            }
                            break;
                    }
                }
            }

            // If set, replace the temporary PdfReferences in xrefStream.Elements, which were set when reading the XrefStream’s dictionary.
            // They have no position set, so reading them would assign the wrong objects to their PdfReference duplicates.
            // The actual references with the correct positions were read later to the xrefTable when reading the XrefStream’s stream.
            // These are the ones to replace the temporary ones.
            ReplaceTemporaryReferences(xrefStream, xrefTable);

            return xrefStream;
        }

        void ReplaceTemporaryReferences(PdfArray array, PdfCrossReferenceTable xrefTable)
        {
            for (var i = 0; i < array.Elements.Count; i++)
            {
                switch (array.Elements[i])
                {
                    case PdfReference reference:
                        {
                            var refID = reference.ObjectID;
                            var actualReference = xrefTable[refID];

                            if (reference != actualReference && actualReference != null)
                                array.Elements[i] = actualReference;
                            break;
                        }

                    case PdfDictionary dic:
                        ReplaceTemporaryReferences(dic, xrefTable);
                        break;

                    case PdfArray arr:
                        ReplaceTemporaryReferences(arr, xrefTable);
                        break;
                }
            }
        }

        void ReplaceTemporaryReferences(PdfDictionary dictionary, PdfCrossReferenceTable xrefTable)
        {
            // Dictionary elements are modified inside the loop. Avoid "Collection was modified; enumeration operation may not execute" error occuring in net 4.6.2.
            // There is no way to access KeyValuePairs via index natively to use a for loop with.
            // Instead, enumerate Keys and get value via Elements[key], which should be O(1).
            foreach (var key in dictionary.Elements.Keys)
            {
                var value = dictionary.Elements[key]; // #US373 References handled below.

                switch (value)
                {
                    case PdfReference reference:
                        {
                            var refID = reference.ObjectID;
                            var actualReference = xrefTable[refID];

                            if (reference != actualReference && actualReference != null)
                                dictionary.Elements[key] = actualReference;
                            break;
                        }

                    case PdfDictionary dict:
                        ReplaceTemporaryReferences(dict, xrefTable);
                        break;

                    case PdfArray arr:
                        ReplaceTemporaryReferences(arr, xrefTable);
                        break;
                }
            }
        }

        /// <summary>
        /// Parses a PDF date string.
        /// </summary>
        internal static bool TryParseDate(string pdfDate, out DateTimeOffset? dateTime)
        {
            dateTime = null;
            bool success = false;
            try
            {
            TryAgain:
                if (pdfDate.StartsWith("D:", StringComparison.Ordinal))
                {
                    // Format is
                    // D:YYYYMMDDHHmmSSOHH'mm'
                    //   ^2      ^10   ^16 ^20
                    // with O is '+', '-', or 'Z'.
                    int length = pdfDate.Length;
                    int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0, hh = 0, mm = 0;
                    char o = 'Z';
                    if (length >= 10)  // Can read date?
                    {
                        year = Int32.Parse(pdfDate.Substring(2, 4));
                        month = Int32.Parse(pdfDate.Substring(6, 2));
                        day = Int32.Parse(pdfDate.Substring(8, 2));
                        if (length >= 16)  // Can read time?
                        {
                            hour = Int32.Parse(pdfDate.Substring(10, 2));
                            minute = Int32.Parse(pdfDate.Substring(12, 2));
                            second = Int32.Parse(pdfDate.Substring(14, 2));
                            if (length >= 22)  // Can read offset? Do not care about trailing ‘'’.
                            {
                                if ((o = pdfDate[16]) != 'Z')
                                {
                                    hh = Int32.Parse(pdfDate.Substring(17, 2));
                                    mm = Int32.Parse(pdfDate.Substring(20, 2));
                                }
                            }
                        }
                    }

                    var offset = TimeSpan.Zero;
                    if (o != 'Z')
                    {
                        offset = new TimeSpan(hh, mm, 0);
                        if (o == '-')
                            offset = offset.Negate();
                    }

                    // There are miserable PDF tools around the world.
                    month = Math.Min(Math.Max(month, 1), 12);

                    dateTime = new(year, month, day, hour, minute, second, offset);
                    success = true;
                }
                else
                {
                    // Some dumb PDF creator tools use plain English date format.
                    if (DateTimeOffset.TryParse(pdfDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                    {
                        dateTime = result;
                        success = true;
                    }
                    else
                    {
                        // Try again with starting “D:”. The specs say it is optional.
                        pdfDate = "D:" + pdfDate;
                        goto TryAgain;
                    }
                }
            }
            catch (Exception)
            {
                // If we cannot parse datetime, just eat it, but give a hint in DEBUG build.
                //Debug.Assert(false, ex.Message);
                PdfSharpLogHost.PdfReadingLogger.LogError("Date string '{Date}' could not be parsed.", pdfDate);
                success = false;
            }
            return success;
        }

        /// <summary>
        /// Saves the current parser state, which is the lexer Position and the Symbol,
        /// in a ParserState struct.
        /// </summary>
        ParserState SaveState()
        {
            return new ParserState
            {
                Position = _lexer.Position,
                Symbol = _lexer.Symbol
            };
        }

        /// <summary>
        /// Restores the current parser state from a ParserState struct.
        /// </summary>
        void RestoreState(ParserState state)
        {
            _lexer.Position = state.Position;
            _lexer.Symbol = state.Symbol;
        }

        struct ParserState
        {
            public SizeType Position;
            public Symbol Symbol;
        }

        /// <summary>
        /// Gets the offset of the newest cross-reference section, as given after 'startxref'.
        /// </summary>
        internal long StartXRef { get; private set; }

        /// <summary>
        /// Gets a value indicating whether a cross-reference table entry named another object than the one at its
        /// offset and the parser used the object it found there.
        /// </summary>
        internal bool RepairedCrossReferences { get; private set; }

        readonly PdfDocument _document;
        readonly PdfReaderOptions _options;
        readonly Lexer _lexer;
#if true
        // Holds the parser and the offset for each ObjectID residing in an ObjectStream.
        readonly Dictionary<PdfObjectID, (Parser Parser, SizeType Offset)> _objectStreamObjectSources = new();
#else
        // Holds the ObjectStream and the parser for each ObjectStream’s ObjectID.
        readonly Dictionary<PdfObjectID, (PdfObjectStream ObjectStream, Parser Parser)> _objectStreamsWithParsers = new();
#endif
        readonly Parser _documentParser;
        int _endStreamNotFoundCounter = 0;
        readonly ILogger _logger;
    }

    static class StreamHelper
    {
        /// <summary>
        /// Reads the type field of a cross-reference stream entry; an entry without one (width 0) is type 1.
        /// </summary>
        public static uint EntryType(byte[] bytes, int index, int width)
        {
            if (width == 0)
                return 1;
            return ReadBytes(bytes, index, width);
        }

        public static int WSize(int[] w)
        {
            Debug.Assert(w.Length == 3);
            return w[0] + w[1] + w[2];
        }

        public static uint ReadBytes(byte[] bytes, int index, int byteCount)
        {
            uint value = 0;
            for (int idx = 0; idx < byteCount; idx++)
            {
                value *= 256;
                value += bytes[index + idx];
            }
            return value;
        }
    }
}
