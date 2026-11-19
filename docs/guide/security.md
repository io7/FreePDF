# Metadata and passwords

## Document properties

<!-- snippet: metadata -->
```csharp
using var document = new PdfDocument();
document.Info.Title = "Invoice 2026-118";
document.Info.Author = "Example Ltd";
document.Info.Subject = "Invoice";
document.Info.Keywords = "invoice, 2026";
document.AddPage();
document.Save("metadata.pdf");
```

Readers show these under the document's properties. FreePDF writes its own name and version as the
Producer.

## Passwords and permissions

<!-- snippet: passwords -->
```csharp
using var document = new PdfDocument();
document.AddPage();
document.SecurityHandler.SetEncryptionToV5();            // AES-256
document.SecuritySettings.UserPassword = "open-me";      // needed to open the file
document.SecuritySettings.OwnerPassword = "change-me";   // needed to change it
document.SecuritySettings.PermitPrint = false;
document.SecuritySettings.PermitModifyDocument = false;
document.Save("protected.pdf");
```

- The user password is needed to open the file. The owner password is needed to change it or its
  permissions; `PdfReader` refuses `Modify` and `Append` with only the user password, unless you set
  `PdfReaderOptions.AllowModifyWithoutOwnerPassword`.
- `SetEncryptionToV5` selects AES-256, the strongest encryption PDF has.
- Permissions such as `PermitPrint` are kept by well-behaved readers. They are not access control: anyone
  who can open the file can read what it contains.

To open a file with a password, see [Existing PDF files](existing-pdfs.md#files-with-a-password).
