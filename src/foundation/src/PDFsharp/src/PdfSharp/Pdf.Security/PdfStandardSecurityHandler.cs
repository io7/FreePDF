// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PdfSharp.Internal;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security.Encryption;
using PdfSharp.Pdf.Signatures;

namespace PdfSharp.Pdf.Security
{
    /// <summary>
    /// Represents the standard PDF security handler.
    /// </summary>
    public sealed class PdfStandardSecurityHandler : PdfSecurityHandler
    {
        internal PdfStandardSecurityHandler(PdfDocument document) : base(document)
        { }

        /// <summary>
        /// Initializes a new instance of this class using the elements of the specified dictionary.
        /// After this type transformation the specified dictionary is dead and cannot be used anymore.
        /// </summary>
        internal PdfStandardSecurityHandler(PdfDictionary dict) : base(dict)
        { }

        void EnsureEncryptionIsSet()
        {
            if (_encryption == null)
                SetDefaultEncryption();
        }

        void SetDefaultEncryption()
        {
            SetEncryptionToV4UsingAES(); // Initialize with the most recent encryption method not requiring PDF 2.0.
        }

        /// <summary>
        /// Do not encrypt the PDF file. Resets the user and owner password.
        /// </summary>
        public void SetEncryptionToNoneAndResetPasswords()
        {
            _userPassword = "";
            _ownerPassword = "";
            _encryption = null;
        }

        /// <summary>
        /// Set the encryption according to the given DefaultEncryption.
        /// Allows setting the encryption automized using one single parameter.
        /// </summary>
        [Obsolete("Use SetEncryption(PdfDefaultEncryption encryption) instead.")]
        public void SetEncryption(DefaultEncryption encryption) => SetEncryption((PdfDefaultEncryption)encryption);

        /// <summary>
        /// Set the encryption according to the given PdfDefaultEncryption.
        /// Allows setting the encryption automized using one single parameter.
        /// </summary>
        public void SetEncryption(PdfDefaultEncryption encryption)
        {
            switch (encryption)
            {
                case PdfDefaultEncryption.None:
                    SetEncryptionToNoneAndResetPasswords();
                    break;

                case PdfDefaultEncryption.Default:
                    SetDefaultEncryption();
                    break;

                case PdfDefaultEncryption.V1:
                    SetEncryptionToV1();
                    break;

                case PdfDefaultEncryption.V2With40Bits:
                    SetEncryptionToV2();
                    break;

                case PdfDefaultEncryption.V2With128Bits:
                    SetEncryptionToV2With128Bits();
                    break;

                case PdfDefaultEncryption.V4UsingRC4:
                    SetEncryptionToV4UsingRC4();
                    break;

                case PdfDefaultEncryption.V4UsingAES:
                    SetEncryptionToV4UsingAES();
                    break;

                case PdfDefaultEncryption.V5:
                    SetEncryptionToV5();
                    break;
            }
        }

        /// <summary>
        /// Encrypt with Version 1 (RC4 and a file encryption key length of 40 bits).
        /// </summary>

        public void SetEncryptionToV1()
        {
            SetEncryptionFieldToV1To4().SetEncryptionToV1();
        }

        /// <summary>
        /// Encrypt with Version 2 (RC4 and a file encryption key length of more than 40 bits, PDF 1.4).
        /// </summary>
        /// <param name="length">The file encryption key length - a multiple of 8 from 40 to 128 bit.</param>
        public void SetEncryptionToV2(int length = 40)
        {
            // There is a specific problem with Microsoft Edge and encryption version 2.
            // If the PDF is saved with an owner AND a user password with version 2 and 40 bit key length, the new PDF viewer of Edge 143.0.3650.96 is not
            // able to open the file with the owner password. For a key length of 48 bit it’s the same.
            // All other encryption versions work correctly in Edge, so use a newer encryption version or SetEncryptionToV2With128Bits() if version 2 is required.
            SetEncryptionFieldToV1To4().SetEncryptionToV2(length);
        }

        /// <summary>
        /// Encrypt with Version 2 (RC4 and a file encryption key length of more than 40 bits, PDF 1.4) with a file encryption key length of 128 bits.
        /// This was the default encryption in PDFsharp 1.5.
        /// </summary>
        public void SetEncryptionToV2With128Bits()
        {
            SetEncryptionFieldToV1To4().SetEncryptionToV2(128);
        }

        /// <summary>
        /// Encrypt with Version 4 (RC4 or AES and a file encryption key length of 128 bits using a crypt filter, PDF 1.5) using RC4.
        /// </summary>
        /// <param name="encryptMetadata">The document metadata stream shall be encrypted (default: true).</param>
        // ReSharper disable once InconsistentNaming
        public void SetEncryptionToV4UsingRC4(bool encryptMetadata = true)
        {
            SetEncryptionFieldToV1To4().SetEncryptionToV4UsingRC4(encryptMetadata);
        }

        /// <summary>
        /// Encrypt with Version 4 (RC4 or AES and a file encryption key length of 128 bits using a crypt filter, PDF 1.5) using AES (PDF 1.6).
        /// </summary>
        /// <param name="encryptMetadata">The document metadata stream shall be encrypted (default: true).</param>
        public void SetEncryptionToV4UsingAES(bool encryptMetadata = true)
        {
            SetEncryptionFieldToV1To4().SetEncryptionToV4UsingAES(encryptMetadata);
        }

        /// <summary>
        /// Encrypt with Version 5 (AES and a file encryption key length of 256 bits using a crypt filter, PDF 2.0).
        /// </summary>
        /// <param name="encryptMetadata">The document metadata stream shall be encrypted (default: true).</param>
        public void SetEncryptionToV5(bool encryptMetadata = true)
        {
            SetEncryptionFieldToV5().Initialize(encryptMetadata);
        }

        PdfEncryptionV1To4 SetEncryptionFieldToV1To4()
        {
            // If necessary, set _encryption to a new PdfEncryptionV1To4.
            if (_encryption is not PdfEncryptionV1To4 encryption1To4)
                _encryption = encryption1To4 = new PdfEncryptionV1To4(this);

            return encryption1To4;
        }
        PdfEncryptionV5 SetEncryptionFieldToV5()
        {
            // If necessary, set _encryption to a new PdfEncryptionV5.
            if (_encryption is not PdfEncryptionV5 encryption5)
                _encryption = encryption5 = new PdfEncryptionV5(this);

            return encryption5;
        }

        /// <summary>
        /// Returns this SecurityHandler, if it shall be written to PDF (if an encryption is chosen).
        /// </summary>
        internal PdfStandardSecurityHandler? GetIfEncryptionIsActive() => IsEncrypted ? this : null;

        bool IsEncrypted => _encryption != null; 
        
        /// <summary>
        /// Indicates that the encryption of an opened document shall not be changed.
        /// </summary>
        public bool DoNotResetEncryption { get; set; }

        /// <summary>
        /// Sets the user password of the document.
        /// </summary>
        public string UserPassword
        {
            internal get => _userPassword;
            set
            {
                EnsureEncryptionIsSet();
                _userPassword = value;
            }
        }
        string _userPassword = "";

        /// <summary>
        /// Sets the owner password of the document.
        /// </summary>
        public string OwnerPassword
        {
            internal get => _ownerPassword;
            set
            {
                EnsureEncryptionIsSet();
                _ownerPassword = value;
            }
        }

        string _ownerPassword = "";

        /// <summary>
        /// Gets a value indicating whether a user or owner password was set. Reading a file sets neither.
        /// </summary>
        internal bool PasswordsChanged => _userPassword.Length > 0 || _ownerPassword.Length > 0;

        /// <summary>
        /// Gets or sets the user access permission represented as an unsigned 32-bit integer in the P key.
        /// </summary>
        internal PdfUserAccessPermission Permissions
        {
            get
            {
                var permissions = (PdfUserAccessPermission)Elements.GetUnsignedInteger(Keys.P);
                if (permissions == 0)
                    permissions = PdfUserAccessPermission.PermitAll;
                return permissions;
            }
            set => Elements.SetInteger(Keys.P, (int)value);
        }

        /// <summary>
        /// Gets the PermissionsValue with some corrections that shall be done for saving.
        /// </summary>
        internal uint GetCorrectedPermissionsValue()
        {
            var permissionsValue = (uint)Permissions;

            // Correct permission bits.
            permissionsValue &= 0xfffffffc; // 1... 1111 1111 1100 - Bit 1 & 2 must be 0.
#if true  // TODO clean up
            //permissionsValue |= 0x000002c0; // 0... 0010 1100 0000 - Bit 7 & 8 must be 1. Also, Bit 10 is no longer used and shall be always set to 1.
            // Top-most bit not correct, but can also be read with PDFsharp up to 6.1.0.
            permissionsValue |= 0x7ffff2c0; // 01.. 1110 1100 0000 - Bit 7 & 8 & 13 through 32 must be 1. Also, Bit 10 is no longer used and shall be always set to 1.
#else
            // Include this later as files can not be read with PDFsharp up to 6.1.0.
            permissionsValue |= 0xfffff2c0; // 1... 1110 1100 0000 - Bit 7 & 8 & 13 through 32 must be 1. Also, Bit 10 is no longer used and shall be always set to 1.
#endif
            return permissionsValue;
        }

        /// <summary>
        /// Has to be called if an indirect PdfObject is entered for encryption/decryption.
        /// </summary>
        internal void EnterObject(PdfObjectID id)
        {
            GetEncryption().EnterObject(id);
        }

        /// <summary>
        /// Should be called if a PdfObject is leaved from encryption/decryption.
        /// </summary>
        internal void LeaveObject()
        {
            GetEncryption().LeaveObject();
        }

        /// <summary>
        /// Returns true, if pdfObject is a SecurityHandler used in any PdfTrailer.
        /// </summary>
        bool IsSecurityHandler(PdfObject pdfObject)
        {
            if (pdfObject is not PdfDictionary pdfDictionary)
                return false;

            // Incrementally updated PDFs contain multiple trailers. Check the /Encrypt entry of each one by object
            // number: reading it as a security handler converts the dictionary, and a second trailer naming the same
            // dictionary would then convert a dictionary whose content the first conversion already took. An update
            // can reuse the number of an older encryption dictionary for an ordinary object, so a number named only
            // by an older trailer counts if the object is an encryption dictionary.
            var currentTrailer = Document.Trailer;
            bool newest = true;
            while (currentTrailer != null)
            {
                if (NamesEncryptionDictionary(currentTrailer, pdfDictionary) && (newest || IsEncryptionDictionary(pdfDictionary)))
                    return true;

                newest = false;
                currentTrailer = currentTrailer.PreviousTrailer;
            }

            return false;
        }

        static bool NamesEncryptionDictionary(PdfTrailer trailer, PdfDictionary dictionary)
        {
            var encrypt = trailer.Elements[PdfTrailer.Keys.Encrypt];
            if (encrypt is PdfReference reference && dictionary.Reference != null && reference.ObjectID == dictionary.ObjectID)
                return true;
            return ReferenceEquals(encrypt, dictionary);
        }

        // ISO 32000-1 Tables 20 and 21: an encryption dictionary has a /Filter, and the standard one /O and /U.
        static bool IsEncryptionDictionary(PdfDictionary dictionary)
            => dictionary.Elements.ContainsKey(PdfSecurityHandler.Keys.Filter)
               && dictionary.Elements.ContainsKey(Keys.O) && dictionary.Elements.ContainsKey(Keys.U);

        /// <summary>
        /// Decrypts an indirect PdfObject.
        /// </summary>
        public void DecryptObject(PdfObject value)
        {
            // PdfStandardSecurityHandler itself is not encrypted.
            if (IsSecurityHandler(value))
                return;

            // Cross-reference streams are not encrypted.
            if (value is PdfCrossReferenceStream)
                return;

            Debug.Assert(value.Reference != null);

            EnterObject(value.ObjectID);

            switch (value)
            {
                case PdfDictionary vDict:
                    DecryptDictionary(vDict);
                    break;

                case PdfArray vArray:
                    DecryptArray(vArray);
                    break;

                case PdfStringObject vStr:
                    DecryptString(vStr);
                    break;
            }

            LeaveObject();
        }

        /// <summary>
        /// Decrypts a dictionary.
        /// </summary>
        void DecryptDictionary(PdfDictionary dict)
        {
            var elementsToDecrypt = GetElementsToDecrypt(dict);

            foreach (var item in elementsToDecrypt)
            {
                switch (item.Value)
                {
                    case PdfString vStr:
                        DecryptString(vStr);
                        break;

                    case PdfDictionary vDict:
                        DecryptDictionary(vDict);
                        break;

                    case PdfArray vArray:
                        DecryptArray(vArray);
                        break;
                }
            }
            if (dict.Stream != null!)
            {
                var bytes = dict.Stream.Value;
                if (bytes.Length != 0)
                {
                    DecryptStream(ref bytes, dict);
                    dict.Stream.Value = bytes;
                }
            }
        }

        IEnumerable<KeyValuePair<string, PdfItem>> GetElementsToDecrypt(PdfDictionary dict)
        {
            var elements = dict.Elements;

            // According to PDF Reference 2.0, hexadecimal strings in the /Contents key of a signature dictionary shall not be encrypted (and so shall not decrypted).

            // However, it’s hard to identify a signature dictionary, as the /Type key is optional. You could identify it by being the dictionary referenced via /V key
            // of a signature field, which itself could be identified by its /FT key set to /Sig. But as decryption is done while reading all indirect objects, the
            // relationship between the signature field and the signature dictionary isn’t modelled yet.
            // But due to the following specifications of PDF Reference 2.0, /Contents key must only be excluded from decryption, if /ByteRange key is present, which is
            // a unique key, we can use for identification:
            // - 7.6.2 Application of encryption: "Encryption applies to all strings and streams in the document's PDF file, with the following exceptions: [...]
            //   Any hexadecimal strings representing the value of the Contents key in a Signature dictionary"
            // - Table 255 — Entries in a signature dictionary:
            //   - "ByteRange - array - [...] When a byte range digest is present, all values in the signature dictionary shall be direct objects.
            //   - "Contents - byte string - (Required) The signature value. When ByteRange is present, the value shall be a hexadecimal string (see 7.3.4.3, "Hexadecimal strings")
            //     representing the value of the byte range digest."

            // So checking for /ByteRange and /Contents keys is sufficient to identify the constellation, where /Contents key must not be decrypted.
            var isSignatureDictWithHexByteRange = elements.ContainsKey(PdfSignature.Keys.ByteRange) && elements.ContainsKey(PdfSignature.Keys.Contents);
            if (isSignatureDictWithHexByteRange)
                return elements.Where(x => x.Key != PdfSignature.Keys.Contents);

            // For all other cases, all elements of the dictionary shall be processed.
            return elements;
        }

        /// <summary>
        /// Decrypts an array.
        /// </summary>
        void DecryptArray(PdfArray array)
        {
            var count = array.Elements.Count;
            for (var idx = 0; idx < count; idx++)
            {
                var item = array.Elements[idx];

                switch (item)
                {
                    case PdfString vStr:
                        DecryptString(vStr);
                        break;

                    case PdfDictionary vDict:
                        DecryptDictionary(vDict);
                        break;

                    case PdfArray vArray:
                        DecryptArray(vArray);
                        break;
                }
            }
        }

        /// <summary>
        /// Decrypts a string.
        /// </summary>
        void DecryptString(PdfStringObject value)
        {
            if (value.Length == 0)
                return;

            var bytes = value.GetRawBytes();
            DecryptString(ref bytes);
            value.SetRawBytes(bytes);

        }

        /// <summary>
        /// Decrypts a string.
        /// </summary>
        void DecryptString(PdfString value)
        {
            if (value.Length == 0)
                return;

            var bytes = value.GetRawBytes();
            DecryptString(ref bytes);
            value.SetRawBytes(bytes);
        }

        /// <summary>
        /// Encrypts a string.
        /// </summary>
        /// <param name="bytes">The byte representation of the string.</param>
        internal void EncryptString(ref byte[] bytes)
        {
            // Empty data is encrypted too: with AES it becomes an initialization vector and a padding block
            // (ISO 32000-1, 7.6.2), which readers such as pyHanko require; RC4 leaves it empty.
            if (VersionSupportsCryptFilter())
            {
                var cryptFilter = _defaultCryptFilterStrings;
                if (cryptFilter is not null)
                {
                    if (cryptFilter.EncryptForEnteredObject(ref bytes))
                        return;
                }
            }

            GetEncryption().EncryptForEnteredObject(ref bytes);
        }

        /// <summary>
        /// Decrypts a string.
        /// </summary>
        /// <param name="bytes">The byte representation of the string.</param>
        internal void DecryptString(ref byte[] bytes)
        {
            if (bytes.Length == 0)
                return;

            if (VersionSupportsCryptFilter())
            {
                var cryptFilter = _defaultCryptFilterStrings;
                if (cryptFilter is not null)
                {
                    if (cryptFilter.DecryptForEnteredObject(ref bytes))
                        return;
                }
            }

            GetEncryption().DecryptForEnteredObject(ref bytes);
        }

        /// <summary>
        /// Encrypts a stream.
        /// </summary>
        /// <param name="bytes">The byte representation of the stream.</param>
        /// <param name="dictionary">The PdfDictionary holding the stream.</param>
        internal void EncryptStream(ref byte[] bytes, PdfDictionary dictionary)
        {
            // Empty data is encrypted too, as in EncryptString.
            if (VersionSupportsCryptFilter())
            {
                var cryptFilter = GetCryptFilter(dictionary, writing: true);
                if (cryptFilter is not null)
                {
                    if (cryptFilter.EncryptForEnteredObject(ref bytes))
                        return;
                }
            }

            GetEncryption().EncryptForEnteredObject(ref bytes);
        }

        /// <summary>
        /// Decrypts a stream.
        /// </summary>
        /// <param name="bytes">The byte representation of the stream.</param>
        /// <param name="dictionary">The PdfDictionary holding the stream.</param>
        internal void DecryptStream(ref byte[] bytes, PdfDictionary dictionary)
        {
            if (bytes.Length == 0)
                return;

            if (VersionSupportsCryptFilter())
            {
                var cryptFilter = GetCryptFilter(dictionary);
                if (cryptFilter is not null)
                {
                    if (cryptFilter.DecryptForEnteredObject(ref bytes))
                        return;
                }
            }

            GetEncryption().DecryptForEnteredObject(ref bytes);
        }

        /// <summary>
        /// Does all necessary initialization for reading and decrypting the document with this security handler.
        /// </summary>
        internal void PrepareForReading()
        {
            var filterValue = Elements.GetName(PdfSecurityHandler.Keys.Filter);
            if (filterValue != "/Standard")
                throw TH.PdfReaderException_UnknownEncryption();

            // Encryption to use for reading the file depends on the version value.
            var versionValue = Elements.GetInteger(PdfSecurityHandler.Keys.V);
            if (PdfEncryptionV1To4.IsVersionSupported(versionValue))
                SetEncryptionFieldToV1To4();
            else if (PdfEncryptionV5.IsVersionSupported(versionValue))
                SetEncryptionFieldToV5();
            else
                throw TH.PdfReaderException_UnknownEncryption();

            // The value and crypt filter checks guard writing as well. For a file being read they mean an encryption
            // dictionary that is damaged or inconsistent, which is a reader error.
            try
            {
                GetEncryption().InitializeFromLoadedSecurityHandler();

                // Load, initialize and prepare crypt filters.
                LoadCryptFilters(true);
                if (_loadedCryptFilters is not null)
                {
                    foreach (var cryptFilter in _loadedCryptFilters.Values)
                        cryptFilter.PrepareForProcessing();
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new PdfReaderException("The document's encryption dictionary is invalid: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Does all necessary initialization for encrypting and writing the document with this security handler.
        /// </summary>
        internal void PrepareForWriting()
        {
            // Return if encryption shall be left unchanged.
            if (DoNotResetEncryption)
                return;

            Elements[PdfSecurityHandler.Keys.Filter] = new PdfName("/Standard");

            GetEncryption().PrepareEncryptionForSaving(UserPassword, OwnerPassword);

            // Load and prepare crypt filters.
            LoadCryptFilters(false);
            if (_loadedCryptFilters is not null)
            {
                foreach (var cryptFilter in _loadedCryptFilters.Values)
                    cryptFilter.PrepareForProcessing();
            }
        }

        /// <summary>
        /// Checks the password.
        /// </summary>
        /// <param name="inputPassword">Password or null if no password is provided.</param>
        internal PasswordValidity ValidatePassword(string? inputPassword)
        {
            inputPassword ??= "";

            PasswordValidity passwordValidity;
            try
            {
                passwordValidity = GetEncryption().ValidatePassword(inputPassword);
            }
            catch (InvalidOperationException ex)
            {
                // The value checks guard writing as well; for a file being read they mean a damaged dictionary.
                throw new PdfReaderException("The document's encryption dictionary is invalid: " + ex.Message, ex);
            }
            Document.SecuritySettings.HasFullPermission = passwordValidity == PasswordValidity.OwnerPassword;

            return passwordValidity;
        }

        internal override void WriteObject(PdfWriter writer)
        {
            // Don’t encrypt myself.
            var effectiveSecurityHandler = writer.EffectiveSecurityHandler;
            writer.EffectiveSecurityHandler = null;
            base.WriteObject(writer);
            writer.EffectiveSecurityHandler = effectiveSecurityHandler;
        }

        /// <summary>
        /// Gets the encryption (not nullable). Use this in cases where the encryption must be set.
        /// </summary>
        internal PdfEncryptionBase GetEncryption()
        {
            if (_encryption == null)
                throw TH.InvalidOperationException_NoEncryptionSet();

            return _encryption;
        }
        PdfEncryptionBase? _encryption;

        #region CryptFilters
        bool VersionSupportsCryptFilter()
        {
            return GetEncryption().VersionValue is 4 or 5;
        }

        void EnsureCryptFiltersAreSupported()
        {
            if (!VersionSupportsCryptFilter())
                throw TH.InvalidOperationException_CryptFiltersNotSupportedForChosenEncryptionVersion();
        }

        bool VersionNeedsCryptFilter()
        {
            return VersionSupportsCryptFilter();
        }

        void EnsureCryptFiltersAreNotNeeded()
        {
            if (VersionNeedsCryptFilter())
                throw TH.InvalidOperationException_CryptFiltersNeededForChosenEncryptionVersion();
        }

        /// <summary>
        /// Removes all crypt filters from the document.
        /// </summary>
        public void RemoveCryptFilters()
        {
            RemoveCryptFilters(false);
        }

        void RemoveCryptFilters(bool force)
        {
            if (!force)
                EnsureCryptFiltersAreNotNeeded();

            Elements.Remove(PdfSecurityHandler.Keys.CF);
            Elements.Remove(PdfSecurityHandler.Keys.StmF);
            Elements.Remove(PdfSecurityHandler.Keys.StrF);
            Elements.Remove(PdfSecurityHandler.Keys.EFF);

            ResetCryptFilterEntriesInAllElements();
        }

        /// <summary>
        /// Creates a crypt filter belonging to standard security handler.
        /// </summary>
        public PdfCryptFilter CreateCryptFilter()
        {
            return new PdfCryptFilter(this);
        }

        /// <summary>
        /// Returns the StdCF as it shall be used in encryption version 4 and 5.
        /// If not yet existing, it is created regarding the asDefaultIfNew parameter, which will set StdCF as default for streams, strings, and embedded file streams.
        /// </summary>
        /// <param name="asDefaultIfNew">If true and the crypt filter is newly created, the crypt filter is referred to as default for any strings, and streams in StmF, StrF and EFF keys.</param>
        public PdfCryptFilter GetOrAddStandardCryptFilter(bool asDefaultIfNew = true)
        {
            EnsureCryptFiltersAreSupported();

            var pdfCryptFilters = (PdfCryptFilters?)Elements.GetValue(PdfSecurityHandler.Keys.CF);
            var standardCryptFilter = pdfCryptFilters?.GetCryptFilter(CryptFilterConstants.StandardFilterName);

            if (standardCryptFilter is null)
            {
                standardCryptFilter = CreateCryptFilter();
                AddCryptFilter(CryptFilterConstants.StandardFilterName, standardCryptFilter, asDefaultIfNew);
            }

            return standardCryptFilter;
        }

        /// <summary>
        /// Adds a crypt filter to the document.
        /// </summary>
        /// <param name="name">The name to identify the crypt filter.</param>
        /// <param name="cryptFilter">The crypt filter.</param>
        /// <param name="asDefault">If true, the crypt filter is referred to as default for any strings and streams in StmF, StrF and EFF keys.</param>
        public void AddCryptFilter(string name, PdfCryptFilter cryptFilter, bool asDefault = false)
        {
            EnsureCryptFiltersAreSupported();

            // If necessary, create cryptFilters PdfDictionary.
            var pdfCryptFilters = (PdfCryptFilters?)Elements.GetValue(PdfSecurityHandler.Keys.CF);
            if (pdfCryptFilters is null)
            {
                pdfCryptFilters = new PdfCryptFilters();
                Elements.SetObject(PdfSecurityHandler.Keys.CF, pdfCryptFilters);
            }

            // Add CryptFilter.
            pdfCryptFilters.AddCryptFilter(name, cryptFilter);

            // Set CryptFilter as default for streams, strings, and embedded file streams, if desired.
            if (asDefault)
                SetCryptFilterAsDefault(name);
        }

        /// <summary>
        /// Encrypts embedded file streams only by setting a crypt filter only in the security handler’s EFF key and
        /// setting the crypt filter’s AuthEvent Key to /EFOpen, in order authenticate embedded file streams when accessing the embedded file.
        /// </summary>
        public void EncryptEmbeddedFileStreamsOnly()
        {
#if true
            throw TH.NotImplementedException_EncryptEmbeddedFileStreamsOnlyCurrentlyShutOff();
#else
            // TODO_OLD: Find and fix error in order to produce files readable by common PDF readers. When done enable SecurityTests.Test_OnlyEmbeddedFileStreamEncrypted().

            if (!VersionSupportsCryptFilter())
                throw TH.InvalidOperationException_InvalidVersionForEncryptEmbeddedFilesOnly();

            var pdfCryptFilters = (PdfCryptFilters?)Elements.GetValue(PdfSecurityHandler.Keys.CF);
            var defaultCryptFilter = pdfCryptFilters?.GetCryptFilter(CryptFilterConstants.StandardFilterName);

            if (defaultCryptFilter is null)
                throw TH.InvalidOperationException_MissingDefaultCryptFilter(CryptFilterConstants.StandardFilterName);

            defaultCryptFilter.SetAuthEventForEmbeddedFiles();

            SetCryptFilterAsDefaultForStreams(null);
            SetCryptFilterAsDefaultForStrings(null);
            SetCryptFilterAsDefaultForEmbeddedFileStreams(CryptFilterConstants.StandardFilterName);
#endif
        }

        /// <summary>
        /// Sets the given crypt filter as default for streams, strings, and embedded streams.
        /// The crypt filter must be manually added crypt filter, "Identity" or null to remove the StmF, StrF and EFF key.
        /// </summary>
        public void SetCryptFilterAsDefault(string? name)
        {
            SetCryptFilterAsDefaultForStreams(name);
            SetCryptFilterAsDefaultForStrings(name);
            SetCryptFilterAsDefaultForEmbeddedFileStreams(null); // If not present, default for streams is used.
        }

        /// <summary>
        /// Sets the given crypt filter as default for streams.
        /// The crypt filter must be manually added crypt filter, "Identity" or null to remove the StmF key.
        /// </summary>
        public void SetCryptFilterAsDefaultForStreams(string? name)
        {
            SetCryptFilterAsDefaultInternal(PdfSecurityHandler.Keys.StmF, name);
        }

        /// <summary>
        /// Sets the given crypt filter as default for strings.
        /// The crypt filter must be manually added crypt filter, "Identity" or null to remove the StrF key.
        /// </summary>
        public void SetCryptFilterAsDefaultForStrings(string? name)
        {
            SetCryptFilterAsDefaultInternal(PdfSecurityHandler.Keys.StrF, name);
        }

        /// <summary>
        /// Sets the given crypt filter as default for embedded file streams.
        /// The crypt filter must be manually added crypt filter, "Identity" or null to remove the EFF key.
        /// </summary>
        public void SetCryptFilterAsDefaultForEmbeddedFileStreams(string? name)
        {
            SetCryptFilterAsDefaultInternal(PdfSecurityHandler.Keys.EFF, name);
        }

        void SetCryptFilterAsDefaultInternal(string key, string? name)
        {
            EnsureCryptFiltersAreSupported();

            if (name is null)
            {
                Elements.Remove(key);
                return;
            }

            if (name != Name.RemoveSlash(CryptFilterConstants.IdentityFilterValue))
            {
                var pdfCryptFilters = (PdfCryptFilters?)Elements.GetValue(PdfSecurityHandler.Keys.CF);
                if (pdfCryptFilters?.GetCryptFilter(name) is null)
                    throw TH.ArgumentException_UnknownCryptFilterSetToDefault();
            }

            Elements.SetName(key, name);
        }

        void LoadCryptFilters(bool initializeCryptFilters)
        {
            var pdfCryptFilters = (PdfCryptFilters?)Elements.GetValue(PdfSecurityHandler.Keys.CF);

            if (!VersionSupportsCryptFilter() || pdfCryptFilters is null || pdfCryptFilters.IsEmpty())
            {
                _loadedCryptFilters = null;
                _defaultCryptFilterStreams = null;
                _defaultCryptFilterStrings = null;
                _defaultCryptFilterEmbeddedFileStreams = null;
                return;
            }

            _loadedCryptFilters = pdfCryptFilters.GetCryptFiltersAsDictionary();
            if (initializeCryptFilters)
            {
                foreach (var loadedCryptFilter in _loadedCryptFilters)
                    loadedCryptFilter.Value.Initialize(this);
            }

            _defaultCryptFilterStreams = GetDefaultCryptFilter(Name.RemoveSlash(Elements.GetName(PdfSecurityHandler.Keys.StmF)));
            _defaultCryptFilterStrings = GetDefaultCryptFilter(Name.RemoveSlash(Elements.GetName(PdfSecurityHandler.Keys.StrF)));
            _defaultCryptFilterEmbeddedFileStreams = GetDefaultCryptFilter(Name.RemoveSlash(Elements.GetName(PdfSecurityHandler.Keys.EFF)), _defaultCryptFilterStreams);
        }

        CryptFilterBase GetDefaultCryptFilter(string cryptFilterName)
        {
            return GetDefaultCryptFilter(cryptFilterName, IdentityCryptFilter.Instance);
        }

        CryptFilterBase GetDefaultCryptFilter(string cryptFilterName, CryptFilterBase @default)
        {
            if (cryptFilterName == Name.RemoveSlash(CryptFilterConstants.IdentityFilterValue))
                return IdentityCryptFilter.Instance;

            if (string.IsNullOrEmpty(cryptFilterName))
                return @default;

            var cryptFilter = _loadedCryptFilters?[cryptFilterName];

            if (cryptFilter is null)
                throw TH.ArgumentException_UnknownCryptFilter();

            return cryptFilter;
        }

        /// <summary>
        /// Resets the explicitly set crypt filter of a dictionary.
        /// </summary>
        public void ResetCryptFilter(PdfDictionary dictionary)
        {
            var filters = FilterItems(dictionary.Elements[PdfStream.Keys.Filter]);
            var parameters = FilterItems(dictionary.Elements[PdfStream.Keys.DecodeParms]);
            if (!filters.Any(IsCryptFilter) && !parameters.Any(IsCryptFilterParms))
                return;

            // /Crypt and its parameters go; the other filters keep theirs, also when an earlier version wrote the
            // crypt filter's parameters as a lone dictionary or at the end of the array.
            var keptFilters = new List<PdfItem>();
            var keptParameters = new List<PdfItem>();
            var aligned = parameters.Count == filters.Count;
            for (var index = 0; index < filters.Count; index++)
            {
                if (IsCryptFilter(filters[index]))
                    continue;
                keptFilters.Add(filters[index]);
                if (aligned)
                    keptParameters.Add(parameters[index]);
            }
            if (!aligned)
            {
                var others = parameters.Where(item => !IsCryptFilterParms(item)).ToList();
                keptParameters = others.Count == keptFilters.Count ? others : keptFilters.Select(_ => (PdfItem)PdfNull.Value).ToList();
            }
            WriteFilters(dictionary, keptFilters, keptParameters);
        }

        void ResetCryptFilterEntriesInAllElements()
        {
            foreach (var iref in Document.IrefTable.AllReferences)
            {
                var pdfObject = iref.Value;
                if (pdfObject is not PdfDictionary dictionary)
                    continue;

                ResetCryptFilter(dictionary);
            }
        }

        /// <summary>
        /// Sets the dictionary’s explicitly set crypt filter to the Identity crypt filter.
        /// </summary>
        public void SetIdentityCryptFilter(PdfDictionary dictionary)
        {
            SetCryptFilter(dictionary, CryptFilterConstants.IdentityFilterValue);
        }

        /// <summary>
        /// Sets the dictionary’s explicitly set crypt filter to the desired crypt filter.
        /// </summary>
        public void SetCryptFilter(PdfDictionary dictionary, string cryptFilterName)
        {
            ResetCryptFilter(dictionary);

            EnsureCryptFiltersAreSupported();

            if (Name.MakeName(cryptFilterName) != CryptFilterConstants.IdentityFilterValue)
            {
                var cryptFilters = (PdfCryptFilters?)Elements.GetValue(PdfSecurityHandler.Keys.CF);
                if (cryptFilters?.GetCryptFilter(cryptFilterName) is null)
                    throw TH.ArgumentException_UnknownCryptFilter();
            }

            var decodeParams = new PdfDictionary(dictionary.Document);
            decodeParams.Elements.SetName(CryptFilterConstants.DecodeParmsTypeKey, CryptFilterConstants.DecodeParmsTypeValue);
            decodeParams.Elements.SetName(CryptFilterConstants.DecodeParmsNameKey, cryptFilterName);

            // The crypt filter comes first (ISO 32000-1 7.4.10), and /DecodeParms holds one entry per filter
            // (Table 5): the other filters keep their parameters, with null where they have none. The parameters
            // were appended instead, so a compressed stream got a lone dictionary next to two filters.
            var filters = FilterItems(dictionary.Elements[PdfStream.Keys.Filter]);
            var parameters = FilterItems(dictionary.Elements[PdfStream.Keys.DecodeParms]);
            var allFilters = new List<PdfItem> { new PdfName(CryptFilterConstants.FilterValue) };
            var allParameters = new List<PdfItem> { decodeParams };
            for (var index = 0; index < filters.Count; index++)
            {
                allFilters.Add(filters[index]);
                allParameters.Add(parameters.Count == filters.Count ? parameters[index] : PdfNull.Value);
            }
            WriteFilters(dictionary, allFilters, allParameters);
        }

        // The entries of /Filter or /DecodeParms: none, one, or the items of an array, read through references.
        static List<PdfItem> FilterItems(PdfItem? item)
        {
            if (item is PdfReference reference)
                item = reference.Value;
            return item switch
            {
                null or PdfNull or PdfNullObject => [],
                PdfArray array => array.Elements.ToList(),
                _ => [item]
            };
        }

        static bool IsCryptFilter(PdfItem filter) => FilterName(filter) is PdfName { Value: CryptFilterConstants.FilterValue };

        static bool IsCryptFilterParms(PdfItem parameters)
        {
            if (parameters is PdfReference reference)
                parameters = reference.Value;
            return parameters is PdfDictionary dictionary && CryptFilterConstants.DecodeParmsPredicate(dictionary);
        }

        // A filter name held as an indirect object is written as a name, which every reader takes.
        static PdfItem FilterName(PdfItem item)
        {
            if (item is PdfReference reference)
                item = reference.Value;
            return item is PdfNameObject name ? new PdfName(name.Value) : item;
        }

        /// <summary>
        /// Writes /Filter and /DecodeParms: a single filter as a name with its parameters, several as arrays of the
        /// same length, and no /DecodeParms when no filter has parameters. Direct parameter dictionaries belong to the
        /// container they came from and are copied; indirect ones stay references.
        /// </summary>
        static void WriteFilters(PdfDictionary dictionary, List<PdfItem> filters, List<PdfItem> parameters)
        {
            var hasParameters = parameters.Any(item => item is not (PdfNull or PdfNullObject));
            dictionary.Elements.Remove(PdfStream.Keys.Filter);
            dictionary.Elements.Remove(PdfStream.Keys.DecodeParms);
            if (filters.Count == 0)
                return;
            if (filters.Count == 1)
            {
                dictionary.Elements[PdfStream.Keys.Filter] = FilterName(filters[0]);
                if (hasParameters)
                    dictionary.Elements[PdfStream.Keys.DecodeParms] = Detached(parameters[0]);
                return;
            }
            var filterArray = new PdfArray(dictionary.Document);
            dictionary.Elements[PdfStream.Keys.Filter] = filterArray;
            foreach (var filter in filters)
                filterArray.Elements.Add(FilterName(filter));
            if (!hasParameters)
                return;
            var parameterArray = new PdfArray(dictionary.Document);
            dictionary.Elements[PdfStream.Keys.DecodeParms] = parameterArray;
            foreach (var item in parameters)
                parameterArray.Elements.Add(Detached(item));

            PdfItem Detached(PdfItem item)
            {
                if (item is not PdfObject { IsIndirect: false } direct || direct.ParentInfo == null)
                    return item;
                var copy = direct.Clone();
                copy.Document = dictionary.Owner;
                return copy;
            }
        }

        /// <summary>
        /// Gets the crypt filter that shall be used to decrypt or encrypt the dictionary.
        /// </summary>
        public CryptFilterBase? GetCryptFilter(PdfDictionary dictionary) => GetCryptFilter(dictionary, false);

        /// <summary>
        /// Gets the crypt filter for the dictionary. With EncryptMetadata false, only the document's metadata stream
        /// is written unencrypted (ISO 32000-1 Table 20); when reading, any metadata stream that holds plain XML is
        /// read as it is, because some writers leave them all unencrypted.
        /// </summary>
        internal CryptFilterBase? GetCryptFilter(PdfDictionary dictionary, bool writing)
        {
            // The cross-reference stream shall not be encrypted. See Reference PDF 2.0: 7.5.8.2  Cross-reference stream dictionary / Page 80.
            var type = dictionary.Elements.GetName(PdfCrossReferenceStream.Keys.Type);
            if (type == "/XRef")
                return IdentityCryptFilter.Instance;

            // If a crypt filter is set for this PdfDictionary, try to return the desired crypt filter.
            var filters = dictionary.Elements.ArrayOrSingleItem.GetAll(PdfStream.Keys.Filter).ToList();
            // ReSharper disable once SuspiciousTypeConversion.Global
            var cryptFilterValue = filters.OfType<PdfName>().FirstOrDefault(x => x.Equals(CryptFilterConstants.FilterValue));
            if (cryptFilterValue is not null)
            {
                // Try to return crypt filter declared in corresponding DecodeParms.
                var filterIndex = filters.IndexOf(cryptFilterValue);
                var decodeParms = dictionary.Elements.ArrayOrSingleItem.GetAll(PdfStream.Keys.DecodeParms).ToList();
                var filterDecodeParms = filterIndex < decodeParms.Count ? decodeParms[filterIndex] : null;

                if (filterDecodeParms is PdfDictionary filterDecodeParmsDict)
                {
                    // /Type is optional, and a missing /Name means Identity (ISO 32000-1, Table 14).
                    var elements = filterDecodeParmsDict.Elements;
                    if (!elements.ContainsKey(CryptFilterConstants.DecodeParmsTypeKey) ||
                        elements.GetName(CryptFilterConstants.DecodeParmsTypeKey) == CryptFilterConstants.DecodeParmsTypeValue)
                    {
                        if (!elements.ContainsKey(CryptFilterConstants.DecodeParmsNameKey))
                            return IdentityCryptFilter.Instance;

                        var cryptFilterNameValue = elements.GetName(CryptFilterConstants.DecodeParmsNameKey);

                        // For Identity crypt filter return its instance.
                        if (cryptFilterNameValue == CryptFilterConstants.IdentityFilterValue)
                            return IdentityCryptFilter.Instance;

                        // For others try to load crypt filter form _loadedCryptFilters.
                        var cryptFilterName = Name.RemoveSlash(cryptFilterNameValue);
                        if (!string.IsNullOrEmpty(cryptFilterName))
                            return _loadedCryptFilters![cryptFilterName];
                    }

                }
                // Use IdentityCryptFilter (no encryption), if DecodeParms is not defined.
                else
                    return IdentityCryptFilter.Instance;

                throw TH.InvalidOperationException_CryptFilterDecodeParmsNotInitializedCorrectly();
            }

            // In general the identity crypt filter should be added to not encrypted dictionaries.
            // But some PDF tools don’t add it to the metadata dictionary if EncryptMetadata is set to false.
            // So if we didn’t find a crypt filter, we check if a metadata dictionary is not encrypted and
            // return the identity crypt filter in that case to avoid decrypting it.
            type = dictionary.Elements.GetName(PdfMetadata.Keys.Type);
            if (type == "/Metadata" && !GetEncryption().EncryptMetadata && (writing ? IsDocumentMetadata(dictionary) : IsPlainMetadata(dictionary)))
                return IdentityCryptFilter.Instance;

            if (PdfEmbeddedFileStream.IsEmbeddedFile(dictionary))
                return _defaultCryptFilterEmbeddedFileStreams;

            // Otherwise return the default crypt filter for streams.
            return _defaultCryptFilterStreams;
        }

        // The catalog's /Metadata: the document-level metadata stream that EncryptMetadata false exempts.
        static bool IsDocumentMetadata(PdfDictionary dictionary)
        {
            var item = dictionary.Owner?.Catalog.Elements[PdfCatalog.Keys.Metadata];
            if (item is PdfReference reference)
                return dictionary.Reference is { } own && own.ObjectID == reference.ObjectID;
            return ReferenceEquals(item, dictionary);
        }

        /// <summary>
        /// Tells whether a metadata stream holds unencrypted XML, or is empty or blank, which is left as it is too.
        /// </summary>
        static bool IsPlainMetadata(PdfDictionary dictionary)
        {
            if (dictionary.Stream is null)
                return false;

            // Blankness is judged on the stream's own bytes: encrypted data can decode to nothing.
            var bytes = dictionary.Stream.Value;
            if (bytes.All(value => Char.IsWhiteSpace((char)value)))
                return true;

            var filter = dictionary.Elements.GetValue(PdfStream.Keys.Filter);
            if (filter != null)
            {
                // Encrypted data rarely has the two-byte header of compressed data.
                var first = filter is PdfArray { Elements.Count: > 0 } filters ? filters.Elements[0] : filter;
                if (first is PdfName { Value: "/FlateDecode" } && !HasZlibHeader(bytes))
                    return false;

                try
                {
                    bytes = Filtering.Decode(bytes, filter, dictionary.Elements.GetValue(PdfStream.Keys.DecodeParms));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Encrypted data does not decode with the stream's filters.
                    return false;
                }
            }
            return LooksLikeXml(bytes);
        }

        static bool HasZlibHeader(byte[] bytes) =>
            bytes.Length >= 2 && (bytes[0] & 0x0F) == 8 && bytes[0] >> 4 <= 7 && ((bytes[0] << 8) | bytes[1]) % 31 == 0;

        /// <summary>
        /// XML starts with '&lt;'. Encrypted data does too, once in 256 streams, but XML goes on with text ("&lt;?xpacket
        /// begin=" in XMP), which encrypted data almost never does: the first 16 characters must be printable ASCII,
        /// tabs, line breaks or well-formed UTF-8 sequences.
        /// </summary>
        static bool LooksLikeXml(byte[] bytes)
        {
            var index = 0;
            while (index < bytes.Length && Char.IsWhiteSpace((char)bytes[index]))
                index++;
            if (index == bytes.Length || bytes[index] != '<')
                return false;

            index++;
            for (var characters = 1; characters < 16 && index < bytes.Length; characters++)
            {
                var value = bytes[index];
                if (value is (>= 0x20 and <= 0x7E) or (byte)'\t' or (byte)'\n' or (byte)'\r')
                {
                    index++;
                    continue;
                }

                var length = value switch
                {
                    >= 0xC2 and <= 0xDF => 2,
                    >= 0xE0 and <= 0xEF => 3,
                    >= 0xF0 and <= 0xF4 => 4,
                    _ => 0
                };
                if (length == 0 || index + length > bytes.Length)
                    return false;
                for (var next = index + 1; next < index + length; next++)
                {
                    if (bytes[next] is < 0x80 or > 0xBF)
                        return false;
                }
                index += length;
            }
            return true;
        }
        #endregion CryptFilters

        Dictionary<string, PdfCryptFilter>? _loadedCryptFilters;
        CryptFilterBase? _defaultCryptFilterStreams;
        CryptFilterBase? _defaultCryptFilterStrings;
        CryptFilterBase? _defaultCryptFilterEmbeddedFileStreams;

        /// <summary>
        /// Typical settings to initialize encryption with.
        /// With DefaultEncryption, the encryption can be set automized using PdfStandardSecurityHandler.SetPermission() with one single parameter.
        /// </summary>
        [Obsolete("Use PdfDefaultEncryption instead.")]
        public enum DefaultEncryption
        {
            /// <summary>
            /// Do not encrypt the PDF file.
            /// </summary>
            None = PdfDefaultEncryption.None,

            /// <summary>
            /// Use V4UsingAES, the most recent encryption method not requiring PDF 2.0.
            /// </summary>
            Default = PdfDefaultEncryption.Default,

            /// <summary>
            /// Encrypt with Version 1 (RC4 and a file encryption key length of 40 bits).
            /// </summary>
            V1 = PdfDefaultEncryption.V1,

            /// <summary>
            /// Encrypt with Version 2 (RC4 and a file encryption key length of more than 40 bits, PDF 1.4) with a file encryption key length of 40 bits.
            /// </summary>
            V2With40Bits = PdfDefaultEncryption.V2With40Bits,

            /// <summary>
            /// Encrypt with Version 2 (RC4 and a file encryption key length of more than 40 bits, PDF 1.4) with a file encryption key length of 128 bits.
            /// This was the default encryption in PDFsharp 1.5.
            /// </summary>
            V2With128Bits = PdfDefaultEncryption.V2With128Bits,

            /// <summary>
            /// Encrypt with Version 4 (RC4 or AES and a file encryption key length of 128 bits using a crypt filter, PDF 1.5) using RC4.
            /// </summary>
            // ReSharper disable once InconsistentNaming
            V4UsingRC4 = PdfDefaultEncryption.V4UsingRC4,

            /// <summary>
            /// Encrypt with Version 4 (RC4 or AES and a file encryption key length of 128 bits using a crypt filter, PDF 1.5) using AES (PDF 1.6).
            /// </summary>
            V4UsingAES = PdfDefaultEncryption.V4UsingAES,

            /// <summary>
            /// Encrypt with Version 5 (AES and a file encryption key length of 256 bits using a crypt filter, PDF 2.0).
            /// </summary>
            V5 = PdfDefaultEncryption.V5
        }

        static class CryptFilterConstants
        {
            public const string FilterValue = "/Crypt";

            public const string DecodeParmsTypeKey = "/Type";

            public const string DecodeParmsTypeValue = "/CryptFilterDecodeParms";

            public const string DecodeParmsNameKey = "/Name";

            public static readonly Func<PdfDictionary, bool> DecodeParmsPredicate = x => x.Elements.GetName(DecodeParmsTypeKey) == DecodeParmsTypeValue;

            public const string IdentityFilterValue = "/Identity";

            public const string StandardFilterName = "StdCF";
        }

        #region Keys
        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal new sealed class Keys : PdfSecurityHandler.Keys
        {
            /// <summary>
            /// (Required) A number specifying which revision of the standard security handler
            /// shall be used to interpret this dictionary:
            /// 2 (Deprecated in PDF 2.0)
            ///     if the document is encrypted with a V value less than 2 and does not have any of
            ///     the access permissions set to 0 (by means of the P entry, below) that are designated
            ///     "Security handlers of revision 3 or greater".
            /// 3 (Deprecated in PDF 2.0)
            ///     if the document is encrypted with a V value of 2 or 3, or has any "Security handlers of revision 3 or
            ///     greater" access permissions set to 0.
            /// 4 (Deprecated in PDF 2.0)
            ///     if the document is encrypted with a V value of 4.
            /// 5 (PDF 2.0; deprecated in PDF 2.0)
            ///     Shall not be used. This value was used by a deprecated proprietary Adobe extension.
            /// 6 (PDF 2.0)
            ///     if the document is encrypted with a V value of 5.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string R = "/R";

            /// <summary>
            /// (Required) A byte string,
            /// 32 bytes long if the value of R is 4 or less and 48 bytes long if the value of R is 6,
            /// based on both the owner and user passwords, that shall be
            /// used in computing the file encryption key and in determining whether a valid owner
            /// password was entered.
            /// </summary>
            [KeyInfo(KeyType.ByteString | KeyType.Required)]
            public const string O = "/O";

            /// <summary>
            /// (Required) A byte string,
            /// 32 bytes long if the value of R is 4 or less and 48 bytes long if the value of R is 6,
            /// based on the owner and user password, that shall be used in determining
            /// whether to prompt the user for a password and, if so, whether a valid user or owner
            /// password was entered.
            /// </summary>
            [KeyInfo(KeyType.ByteString | KeyType.Required)]
            public const string U = "/U";

            /// <summary>
            /// (Required if R is 6 (PDF 2.0))
            /// A 32-byte string, based on the owner and user password, that shall be used in computing the file encryption key. 
            /// </summary>
            [KeyInfo("2.0", KeyType.ByteString | KeyType.Optional)]
            public const string OE = "/OE";

            /// <summary>
            /// (Required if R is 6 (PDF 2.0))
            /// A 32-byte string, based on the user password, that shall be used in computing the file encryption key.
            /// </summary>
            [KeyInfo("2.0", KeyType.ByteString | KeyType.Optional)]
            public const string UE = "/UE";

            /// <summary>
            /// (Required) A set of flags specifying which operations shall be permitted when the document
            /// is opened with user access.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string P = "/P";

            /// <summary>
            /// (Required if R is 6 (PDF 2.0))
            /// A 16-byte string, encrypted with the file encryption key, that contains an encrypted copy of the permissions flags.
            /// </summary>
            [KeyInfo("2.0", KeyType.ByteString | KeyType.Optional)]
            public const string Perms = "/Perms";

            /// <summary>
            /// (Optional; meaningful only when the value of V is 4 (PDF 1.5) or 5 (PDF 2.0)) Indicates whether
            /// the document-level metadata stream shall be encrypted.
            /// Default value: true.
            /// </summary>
            [KeyInfo(KeyType.Boolean | KeyType.Optional)]
            public const string EncryptMetadata = "/EncryptMetadata";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            internal static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta? _meta;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta => Keys.Meta;

        #endregion
    }
}
