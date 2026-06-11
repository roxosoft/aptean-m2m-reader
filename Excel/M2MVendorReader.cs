using ClosedXML.Excel;

namespace JetSolutions.BillVendorSync.Excel;

/// <summary>
/// Reads the M2M vendor list from an Excel (.xlsx) file. Columns are located by
/// their header text so the column order in the export does not matter.
/// </summary>
public static class M2MVendorReader
{
    // Header text (case-insensitive) -> logical field. Multiple aliases supported.
    private static readonly string[] VendorIdHeaders = { "Vendor" };
    private static readonly string[] CompanyHeaders = { "Company" };
    private static readonly string[] CityHeaders = { "City" };
    private static readonly string[] ZipHeaders = { "Zip Code", "Zip", "Zip/Postal Code", "ZipCode" };

    public static IReadOnlyList<M2MVendor> Read(string path, string? sheetName = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Excel file not found: {path}", path);
        }

        using var workbook = new XLWorkbook(path);
        var worksheet = ResolveWorksheet(workbook, sheetName);

        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException("The worksheet appears to be empty (no header row found).");

        var headers = BuildHeaderMap(headerRow);

        int vendorIdCol = RequireColumn(headers, VendorIdHeaders, "Vendor (M2MVendorID)");
        int companyCol = RequireColumn(headers, CompanyHeaders, "Company (VendorName)");
        int cityCol = FindColumn(headers, CityHeaders);
        int zipCol = FindColumn(headers, ZipHeaders);

        var vendors = new List<M2MVendor>();
        int firstDataRow = headerRow.RowNumber() + 1;
        int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();

        for (int rowNum = firstDataRow; rowNum <= lastRow; rowNum++)
        {
            var row = worksheet.Row(rowNum);

            string vendorId = GetCellText(row, vendorIdCol);
            string company = GetCellText(row, companyCol);

            // Skip completely blank rows.
            if (vendorId.Length == 0 && company.Length == 0)
            {
                continue;
            }

            vendors.Add(new M2MVendor
            {
                M2MVendorId = vendorId,
                VendorName = company,
                City = cityCol > 0 ? GetCellText(row, cityCol) : null,
                ZipCode = zipCol > 0 ? GetCellText(row, zipCol) : null,
                RowNumber = rowNum,
            });
        }

        return vendors;
    }

    private static IXLWorksheet ResolveWorksheet(XLWorkbook workbook, string? sheetName)
    {
        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            if (workbook.TryGetWorksheet(sheetName, out var named))
            {
                return named;
            }

            throw new InvalidOperationException($"Worksheet '{sheetName}' was not found in the workbook.");
        }

        return workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("The workbook contains no worksheets.");
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var text = cell.GetString().Trim();
            if (text.Length > 0 && !map.ContainsKey(text))
            {
                map[text] = cell.Address.ColumnNumber;
            }
        }

        return map;
    }

    private static int RequireColumn(Dictionary<string, int> headers, string[] candidates, string description)
    {
        int col = FindColumn(headers, candidates);
        if (col <= 0)
        {
            throw new InvalidOperationException(
                $"Required column '{description}' was not found. Looked for headers: {string.Join(", ", candidates)}.");
        }

        return col;
    }

    private static int FindColumn(Dictionary<string, int> headers, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (headers.TryGetValue(candidate, out int col))
            {
                return col;
            }
        }

        return -1;
    }

    private static string GetCellText(IXLRow row, int columnNumber)
    {
        if (columnNumber <= 0)
        {
            return string.Empty;
        }

        var cell = row.Cell(columnNumber);
        return cell.IsEmpty() ? string.Empty : cell.GetString().Trim();
    }
}
