using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Netcad.Interop;
using Netcad.Interop.NMath;
using Newtonsoft.Json.Linq;

namespace KulAI.Netcad.Tools
{
    /// <summary>Excel (xlsx), CSV ve TXT koordinat listelerini satır/sütun olarak okur.</summary>
    internal static class TableFile
    {
        /// <summary>
        /// Tam yol değilse kullanıcının Masaüstü, Belgeler ve İndirilenler klasörlerinde arar
        /// ("masaüstündeki olcum.xlsx" gibi istekler için).
        /// </summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Dosya yolu gerekli.");
            path = path.Trim().Trim('"');
            if (Path.IsPathRooted(path))
                return File.Exists(path) ? path : throw new FileNotFoundException($"Dosya bulunamadı: {path}");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(home, "Downloads"),
            };
            foreach (var folder in folders)
            {
                var candidate = Path.Combine(folder, path);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException($"'{path}' Masaüstü, Belgeler ve İndirilenler klasörlerinde bulunamadı. Tam yolu verin.");
        }

        public static List<string[]> Read(string path, string sheet)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".xlsx" || ext == ".xlsm") return ReadExcel(path, sheet);
            if (ext == ".xls") throw new NotSupportedException("Eski .xls biçimi desteklenmiyor; dosyayı Excel'de .xlsx olarak kaydedin.");
            return ReadText(path);
        }

        private static List<string[]> ReadExcel(string path, string sheet)
        {
            // Excel açıkken de okunabilsin diye paylaşımlı açılır.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
            {
                var ws = string.IsNullOrWhiteSpace(sheet) ? workbook.Worksheet(1) : workbook.Worksheet(sheet);
                var used = ws.RangeUsed();
                if (used == null) return new List<string[]>();
                return used.Rows().Select(row => row.Cells().Select(c =>
                    c.DataType == ClosedXML.Excel.XLDataType.Number
                        ? c.GetDouble().ToString("R", CultureInfo.InvariantCulture)
                        : c.GetFormattedString()).ToArray()).ToList();
            }
        }

        private static List<string[]> ReadText(string path)
        {
            var bytes = File.ReadAllBytes(path);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { text = Encoding.GetEncoding(1254).GetString(bytes); } // eski Türkçe dosyalar
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Where(l => l.Trim().Length > 0).ToList();

            // Ayırıcı: ilk satırlarda en tutarlı geçen karakter; yoksa boşluk.
            var sample = lines.Take(20).ToList();
            char? delimiter = new[] { '\t', ';', ',' }
                .Where(d => sample.Count > 0 && sample.All(l => l.IndexOf(d) >= 0))
                .Cast<char?>().FirstOrDefault();
            return lines.Select(l => delimiter.HasValue
                    ? l.Split(delimiter.Value).Select(s => s.Trim()).ToArray()
                    : l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
        }

        /// <summary>"4512800.123", "4512800,123" ve "4.512.800,123" biçimlerini okur.</summary>
        public static bool TryNumber(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Replace(" ", "");
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !(s.Contains(",") && !s.Contains(".")))
                return true;
            if (s.Contains(",") && s.Contains(".")) s = s.Replace(".", "");   // binlik ayırıcı nokta, ondalık virgül
            return double.TryParse(s.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    public sealed class PreviewTableFileTool : NetcadTool
    {
        public override string Name => "preview_table_file";
        public override string Description =>
            "Bir koordinat listesi dosyasının (xlsx, csv, txt, NETCAD .ncn) ilk satırlarını gösterir. Noktaları aktarmadan ÖNCE çağır: " +
            "sütunlardan hangisinin harita Y'si (sağa, genelde 6 haneli), harita X'i (yukarı, genelde 7 haneli), kot, nokta adı " +
            "ve kod olduğunu buna bakarak belirle. Tam yol verilmezse Masaüstü, Belgeler ve İndirilenler'de aranır.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"":  { ""type"": ""string"", ""description"": ""Dosya yolu ya da adı, ör. olcum.xlsx"" },
    ""sheet"": { ""type"": ""string"", ""description"": ""Excel sayfa adı (varsayılan ilk sayfa)"" }
  },
  ""required"": [""path""]
}";
        public override bool ModifiesDrawing => false;

        public override string Execute(JObject args)
        {
            var path = TableFile.Resolve(RequiredStr(args, "path"));
            var rows = TableFile.Read(path, Str(args, "sheet"));
            int columns = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
            // Her sütunun sayısal olup olmadığı ve değer aralığı: Y/X'i ayırt etmeye yardım eder.
            var stats = Enumerable.Range(0, columns).Select(c =>
            {
                var nums = rows.Skip(1).Select(r => c < r.Length && TableFile.TryNumber(r[c], out var v) ? (double?)v : null)
                               .Where(v => v.HasValue).Select(v => v.Value).ToList();
                return new
                {
                    column = c,
                    numeric_ratio = rows.Count > 1 ? Math.Round((double)nums.Count / (rows.Count - 1), 2) : 0,
                    min = nums.Count > 0 ? nums.Min() : (double?)null,
                    max = nums.Count > 0 ? nums.Max() : (double?)null,
                };
            }).ToList();
            return Ok(new
            {
                file = path,
                row_count = rows.Count,
                column_count = columns,
                first_rows = rows.Take(12),
                columns = stats,
            });
        }
    }

    public sealed class ImportPointsFromFileTool : NetcadTool
    {
        public override string Name => "import_points_from_file";
        public override string Description =>
            "Koordinat listesi dosyasındaki noktaları NETCAD'e aktarır. Önce preview_table_file ile sütunları belirle. " +
            "Sütun numaraları 0'dan başlar. east_column = harita Y (sağa), north_column = harita X (yukarı). " +
            "NETCAD nokta dosyası (.ncn: 'Ad Y X Z [Kod]') için sütunları vermene gerek yok; standart sıra kullanılır.";
        protected override string SchemaJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"":          { ""type"": ""string"" },
    ""sheet"":         { ""type"": ""string"" },
    ""skip_rows"":     { ""type"": ""integer"", ""description"": ""Baştan atlanacak satır sayısı (başlık satırı varsa 1)"" },
    ""east_column"":   { ""type"": ""integer"", ""description"": ""Harita Y (sağa) sütunu"" },
    ""north_column"":  { ""type"": ""integer"", ""description"": ""Harita X (yukarı) sütunu"" },
    ""z_column"":      { ""type"": ""integer"", ""description"": ""Kot sütunu (yoksa verme)"" },
    ""name_column"":   { ""type"": ""integer"", ""description"": ""Nokta adı/no sütunu (yoksa verme)"" },
    ""code_column"":   { ""type"": ""integer"", ""description"": ""Nokta kodu sütunu (yoksa verme)"" },
    ""layer"":         { ""type"": ""string"", ""description"": ""Varsayılan 'NOKTALAR'"" }
  },
  ""required"": [""path""]
}";

        private sealed class Row { public double E, N, Z; public string Name, Code; }

        /// <summary>NETCAD .ncn nokta dosyası: her satır "Ad Y X Z [Kod]" (Y = sağa, X = yukarı), boşlukla ayrılmış.</summary>
        private static bool IsNcn(string path) => string.Equals(Path.GetExtension(path), ".ncn", StringComparison.OrdinalIgnoreCase);

        private static (List<Row> rows, List<int> skipped, string path) Parse(JObject args)
        {
            var path = TableFile.Resolve(RequiredStr(args, "path"));
            var raw = TableFile.Read(path, Str(args, "sheet")).Skip((int)Num(args, "skip_rows", 0)).ToList();
            bool ncn = IsNcn(path);
            int? Col(string key, int? ncnDefault) => args[key] != null ? (int?)Num(args, key) : (ncn ? ncnDefault : null);
            int? ec0 = Col("east_column", 1), nc0 = Col("north_column", 2);
            if (ec0 == null || nc0 == null)
                throw new ArgumentException("east_column ve north_column gerekli (önce preview_table_file ile sütunlara bak).");
            int ec = ec0.Value, nc = nc0.Value;
            int? zc = Col("z_column", 3);
            int? namec = Col("name_column", 0);
            int? codec = Col("code_column", 4);

            string Cell(string[] r, int? c) => c.HasValue && c.Value < r.Length ? r[c.Value] : null;
            var rows = new List<Row>();
            var skipped = new List<int>();
            for (int i = 0; i < raw.Count; i++)
            {
                var r = raw[i];
                if (!TableFile.TryNumber(Cell(r, ec), out var e) || !TableFile.TryNumber(Cell(r, nc), out var n)) { skipped.Add(i + 1); continue; }
                double z = 0;
                if (zc.HasValue) TableFile.TryNumber(Cell(r, zc), out z);
                rows.Add(new Row { E = e, N = n, Z = z, Name = Cell(r, namec)?.Trim(), Code = Cell(r, codec)?.Trim() });
            }
            return (rows, skipped, path);
        }

        public override string ConfirmationFor(JObject args, int threshold)
        {
            var (rows, _, path) = Parse(args);
            return rows.Count > threshold ? $"{Path.GetFileName(path)} dosyasından {rows.Count} nokta aktarılacak." : null;
        }

        public override string Execute(JObject args)
        {
            var (rows, skipped, path) = Parse(args);
            if (rows.Count == 0) throw new ArgumentException("Dosyada geçerli koordinat satırı bulunamadı; sütun numaralarını ve skip_rows'u kontrol edin.");

            var nc = NetcadContext.Nc;
            int layer = EnsureLayer(Str(args, "layer", "NOKTALAR"));
            foreach (var row in rows)
            {
                var c = new Coor(row.E, row.N);
                var point = nc.NC_MakePoint(ref c, row.Z, new DelphiString(row.Name ?? ""), new DelphiString(row.Code ?? ""), layer);
                nc.NC_AddObjectX(ref point, null);
            }

            string warning = null;
            // Y/X karışmış mı? Türkiye'de projeksiyon koordinatlarında X (north) genelde Y'den (east) çok büyüktür.
            double avgE = rows.Average(r => r.E), avgN = rows.Average(r => r.N);
            if (avgE > 1_000_000 && avgN < 1_000_000)
                warning = "Sağa (Y) değerleri 7 haneli, yukarı (X) değerleri 6 haneli görünüyor: sütunlar ters seçilmiş olabilir.";

            return Ok(new
            {
                file = Path.GetFileName(path),
                imported = rows.Count,
                skipped_rows = skipped.Count > 0 ? skipped.Take(20) : null,
                skipped_count = skipped.Count,
                bounds = new { min_y = rows.Min(r => r.E), max_y = rows.Max(r => r.E), min_x = rows.Min(r => r.N), max_x = rows.Max(r => r.N) },
                warning,
            });
        }
    }
}
