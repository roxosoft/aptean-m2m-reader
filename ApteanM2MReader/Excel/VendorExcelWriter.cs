using ClosedXML.Excel;
using JetSolutions.ApteanM2MReader.Aptean;

namespace JetSolutions.ApteanM2MReader.Excel;

/// <summary>
/// Writes vendors to an Excel file with headers compatible with
/// JetSolutions.BillVendorSync Excel/M2MVendorReader (plus State / Phone Number).
/// </summary>
public static class VendorExcelWriter
{
    public static void Write(string path, IReadOnlyList<ApteanVendor> vendors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Vendors");

        sheet.Cell(1, 1).Value = "Vendor";
        sheet.Cell(1, 2).Value = "Company";
        sheet.Cell(1, 3).Value = "City";
        sheet.Cell(1, 4).Value = "State";
        sheet.Cell(1, 5).Value = "Zip Code";
        sheet.Cell(1, 6).Value = "Phone Number";
        sheet.Row(1).Style.Font.Bold = true;

        for (int i = 0; i < vendors.Count; i++)
        {
            var v = vendors[i];
            int row = i + 2;
            sheet.Cell(row, 1).Value = v.VendorId;
            sheet.Cell(row, 2).Value = v.Company;
            sheet.Cell(row, 3).Value = v.City ?? string.Empty;
            sheet.Cell(row, 4).Value = v.State ?? string.Empty;
            sheet.Cell(row, 5).Value = v.ZipCode ?? string.Empty;
            sheet.Cell(row, 6).Value = v.Phone ?? string.Empty;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(fullPath);
    }
}
