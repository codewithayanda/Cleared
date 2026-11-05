using UglyToad.PdfPig;

namespace Cleared.Integration.Tests;

// What a person would read on a generated PDF, as plain words in page order. It lets a test say what
// an invoice shows and what it leaves off.
public static class PdfText
{
    public static string Of(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);

        return string.Join(' ', document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
    }
}
