// Original synthetic content shared by the native and independent accessibility tests. MIT.
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace FreePDF.Lab.Fixtures;

static class AccessibilityFormFixture
{
    internal static PdfDictionary AddForm(PdfDocument pdf, PdfDictionary resources, string name, string content)
    {
        var form = new PdfDictionary(pdf);
        form.Elements.SetName("/Type", "/XObject");
        form.Elements.SetName("/Subtype", "/Form");
        form.Elements.SetRectangle("/BBox", new PdfRectangle(new XPoint(0, 0), new XPoint(20, 20)));
        form.Elements["/Resources"] = new PdfDictionary(pdf);
        form.CreateStream(Encoding.ASCII.GetBytes(content));
        pdf.Internals.AddObject(form);
        var objects = resources.Elements.GetDictionary("/XObject") ?? new PdfDictionary(pdf);
        objects.Elements[name] = form.Reference!;
        resources.Elements["/XObject"] = objects;
        return form;
    }

    // The original figure already owns MCID 0 on the page. Its second MCID 0 belongs to the inner form.
    internal static PdfDictionary AddTaggedForm(PdfDocument pdf)
    {
        var page = pdf.Pages[0];
        var outer = AddForm(pdf, page.Elements.GetDictionary("/Resources")!, "/Outer", "/Inner Do");
        var inner = AddForm(pdf, outer.Elements.GetDictionary("/Resources")!, "/Inner",
            "/Figure /Mark BDC 1 1 8 8 re f EMC");
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Outer Do"));

        var properties = new PdfDictionary(pdf);
        var mark = new PdfDictionary(pdf);
        mark.Elements.SetInteger("/MCID", 0);
        mark.Elements.SetString("/Lang", "en");
        pdf.Internals.AddObject(mark);
        properties.Elements["/Mark"] = mark.Reference!;
        inner.Elements.GetDictionary("/Resources")!.Elements["/Properties"] = properties;

        var root = pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!;
        var parents = root.Elements.GetDictionary("/ParentTree")!.Elements.GetArray("/Nums")!;
        var pageParents = parents.Elements.GetArray(1)!;
        var figure = pageParents.Elements.GetDictionary(0)!;
        var formKey = root.Elements.GetInteger("/ParentTreeNextKey");
        inner.Elements.SetInteger("/StructParents", formKey);
        root.Elements.SetInteger("/ParentTreeNextKey", formKey + 1);
        var formParents = new PdfArray(pdf);
        formParents.Elements.Add(figure.Reference!);
        parents.Elements.Add(new PdfInteger(formKey));
        parents.Elements.Add(formParents);

        var mcr = new PdfDictionary(pdf);
        mcr.Elements.SetName("/Type", "/MCR");
        mcr.Elements["/Pg"] = page.Reference!;
        mcr.Elements["/Stm"] = inner.Reference!;
        mcr.Elements.SetInteger("/MCID", 0);
        var kids = figure.Elements.GetArray("/K");
        if (kids == null)
        {
            kids = new PdfArray(pdf);
            kids.Elements.Add(figure.Elements["/K"]!);
            figure.Elements["/K"] = kids;
        }
        kids.Elements.Add(mcr);
        return inner;
    }
}
