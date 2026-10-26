using Books.Application.Documents;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Books.Api.Http;

/// <summary>
/// Draws a sales invoice or credit note as a bilingual Khmer/English A4 PDF (M9). Khmer text uses the embedded
/// Noto Sans Khmer font (SIL Open Font License, see Fonts/OFL-NotoSansKhmer.txt) as a fallback after Lato.
/// </summary>
public static class InvoicePdf
{
    public const string ContentType = "application/pdf";
    private const string KhmerFont = "Noto Sans Khmer";

    /// <summary>Call once at startup: the Community licence and the embedded Khmer font.</summary>
    public static void Configure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        using var font = typeof(InvoicePdf).Assembly.GetManifestResourceStream("Books.Api.Fonts.NotoSansKhmer.ttf")
            ?? throw new InvalidOperationException("The Khmer font is not embedded.");
        FontManager.RegisterFontFromStream(font);
    }

    public static byte[] Render(InvoicePrint p) => Document.Create(document => document.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(t => t.FontSize(9).FontFamily("Lato", KhmerFont));
        page.Header().Element(c => Header(c, p));
        page.Content().PaddingVertical(12).Element(c => Body(c, p));
        page.Footer().AlignCenter().Text(t =>
        {
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });
    })).GeneratePdf();

    private static (string Khmer, string English) Title(InvoicePrint p) =>
        p.CreditNote ? ("លិខិតឥណទាន", "CREDIT NOTE")
        : p.Style == "commercial" ? ("វិក្កយបត្រ", "COMMERCIAL INVOICE")
        : ("វិក្កយបត្រអាករ", "TAX INVOICE");

    private static void Header(IContainer c, InvoicePrint p) => c.Column(col =>
    {
        var s = p.SellerSettings;
        if (s.NameKhmer is not null)
        {
            col.Item().AlignCenter().Text(s.NameKhmer).FontSize(14).Bold();
        }
        col.Item().AlignCenter().Text(p.SellerName).FontSize(13).Bold();
        if (s.TaxId is not null)
        {
            col.Item().AlignCenter().Text($"លេខអត្តសញ្ញាណកម្ម អតប (VATTIN): {s.TaxId}");
        }
        foreach (var line in new[] { s.AddressKhmer is null ? null : $"អាសយដ្ឋាន: {s.AddressKhmer}", s.AddressEnglish is null ? null : $"Address: {s.AddressEnglish}" })
        {
            if (line is not null)
            {
                col.Item().AlignCenter().Text(line);
            }
        }
        var contact = string.Join("   ", new[] { s.Phone is null ? null : $"ទូរស័ព្ទ/Tel: {s.Phone}", s.Email is null ? null : $"Email: {s.Email}" }.OfType<string>());
        if (contact != "")
        {
            col.Item().AlignCenter().Text(contact);
        }
        var (khmer, english) = Title(p);
        col.Item().PaddingTop(10).AlignCenter().Text(khmer).FontSize(14).Bold();
        col.Item().AlignCenter().Text(p.Draft ? $"{english} (DRAFT)" : english).FontSize(12).Bold();
    });

    private static void Body(IContainer c, InvoicePrint p) => c.Column(col =>
    {
        col.Spacing(10);
        col.Item().Row(row =>
        {
            row.RelativeItem().Column(buyer =>
            {
                buyer.Item().Text("អតិថិជន / Customer").Bold();
                buyer.Item().Text(p.Buyer.Name);
                if (p.Buyer.TaxId is not null)
                {
                    buyer.Item().Text($"លេខអត្តសញ្ញាណកម្ម អតប (VATTIN): {p.Buyer.TaxId}");
                }
                if (p.Buyer.Address is not null)
                {
                    buyer.Item().Text($"អាសយដ្ឋាន/Address: {p.Buyer.Address}");
                }
                if (p.Buyer.Phone is not null)
                {
                    buyer.Item().Text($"ទូរស័ព្ទ/Tel: {p.Buyer.Phone}");
                }
            });
            row.ConstantItem(200).Column(meta =>
            {
                meta.Item().Text($"{(p.CreditNote ? "លេខលិខិតឥណទាន / Credit note no." : "លេខវិក្កយបត្រ / Invoice no.")}: {p.Number}").Bold();
                meta.Item().Text($"កាលបរិច្ឆេទ / Date: {p.Date:dd/MM/yyyy}");
                if (!p.CreditNote)
                {
                    meta.Item().Text($"ថ្ងៃផុតកំណត់ / Due date: {p.DueDate:dd/MM/yyyy}");
                }
                if (p.OriginalNumber is not null)
                {
                    meta.Item().Text($"វិក្កយបត្រដើម / Original invoice: {p.OriginalNumber}");
                }
                if (p.Reference is not null)
                {
                    meta.Item().Text($"យោង / Reference: {p.Reference}");
                }
            });
        });
        if (p.Description is not null)
        {
            col.Item().Text(p.Description);
        }
        col.Item().Element(e => Lines(e, p));
        col.Item().AlignRight().Width(280).Element(e => Totals(e, p));
        col.Item().PaddingTop(40).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "ហត្ថលេខា និងឈ្មោះអ្នកទិញ", "Customer's signature & name"));
            row.ConstantItem(60);
            row.RelativeItem().Element(e => Signature(e, "ហត្ថលេខា និងឈ្មោះអ្នកលក់", "Seller's signature & name"));
        });
    });

    private static void Lines(IContainer c, InvoicePrint p) => c.Table(table =>
    {
        var showTax = p.Style != "commercial";
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(28);
            cols.RelativeColumn(5);
            cols.RelativeColumn(1.4f);
            cols.RelativeColumn(2);
            if (showTax)
            {
                cols.RelativeColumn(2);
            }
            cols.RelativeColumn(2.2f);
        });
        table.Header(h =>
        {
            Head(h.Cell(), "ល.រ", "No.");
            Head(h.Cell(), "បរិយាយ", "Description");
            Head(h.Cell(), "បរិមាណ", "Qty");
            Head(h.Cell(), "ថ្លៃឯកតា", $"Unit price ({p.Currency})");
            if (showTax)
            {
                Head(h.Cell(), "អាករ", "Tax");
            }
            Head(h.Cell(), "ថ្លៃទំនិញ", $"Amount ({p.Currency})");
        });
        foreach (var line in p.Lines)
        {
            Cell(table.Cell()).Text(line.No.ToString());
            Cell(table.Cell()).Text(line.Description ?? "");
            Cell(table.Cell()).AlignRight().Text(line.Quantity ?? "");
            Cell(table.Cell()).AlignRight().Text(line.UnitPrice ?? "");
            if (showTax)
            {
                Cell(table.Cell()).Text(line.Tax ?? "");
            }
            Cell(table.Cell()).AlignRight().Text(line.Amount);
        }
    });

    private static void Totals(IContainer c, InvoicePrint p) => c.Column(col =>
    {
        void Row(string khmer, string english, string amount, bool bold = false) => col.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(3).Row(r =>
            {
                var label = r.RelativeItem().Text($"{khmer} / {english}");
                var value = r.ConstantItem(100).AlignRight().Text($"{amount} {p.Currency}");
                if (bold)
                {
                    label.Bold();
                    value.Bold();
                }
            });

        Row("សរុប (មិនរួមអាករ)", "Sub-total (excluding tax)", p.NetTotal);
        foreach (var other in p.OtherTaxes)
        {
            Row("អាករផ្សេងៗ", other.Label, other.Amount);
        }
        foreach (var charge in p.StateCharges)
        {
            Row("បន្ទុករដ្ឋ", charge.Label, charge.Amount);
        }
        if (p.Style != "commercial")
        {
            Row(p.StateCharge ? "បន្ទុករដ្ឋ" : "អាករលើតម្លៃបន្ថែម", p.StateCharge ? "State charge" : "VAT", p.VatTotal);
        }
        Row("សរុបរួម", "Grand total", p.GrossTotal, bold: true);
        if (p.TaxCurrency is { } k)
        {
            col.Item().PaddingTop(6).Text($"សរុបរួមជា {k.Currency} / Grand total in {k.Currency}: {k.Gross}").Bold();
            if (p.Style != "commercial")
            {
                col.Item().Text($"{(p.StateCharge ? "State charge" : "VAT")} in {k.Currency}: {k.Tax}");
            }
            col.Item().Text($"អត្រាប្តូរប្រាក់ / Exchange rate: {k.Rate} ({k.RateDate:dd/MM/yyyy})").FontSize(8);
        }
    });

    private static void Signature(IContainer c, string khmer, string english) => c.Column(col =>
    {
        col.Item().PaddingTop(30).BorderTop(0.75f).AlignCenter().Text(khmer);
        col.Item().AlignCenter().Text(english);
    });

    private static void Head(IContainer c, string khmer, string english) =>
        c.Background(Colors.Grey.Lighten3).Border(0.5f).Padding(3).Column(col =>
        {
            col.Item().Text(khmer).Bold();
            col.Item().Text(english).FontSize(8);
        });

    private static IContainer Cell(IContainer c) => c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);
}
