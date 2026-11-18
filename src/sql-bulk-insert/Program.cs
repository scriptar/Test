using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;

namespace BulkInsert
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine($"Usage: {AppDomain.CurrentDomain.FriendlyName} <datafile> <fmtfile> <tablename>");
                return;
            }

            string dataFile = args[0];
            string fmtFile = args[1];
            string tableName = args[2];

            if (!File.Exists(dataFile))
                throw new FileNotFoundException($"Invalid data file: {dataFile}");
            if (!File.Exists(fmtFile))
                throw new FileNotFoundException($"Invalid .fmt file: {fmtFile}");
            if (string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("Invalid destination table name");

            var fmtColumns = ParseFmtFile(fmtFile);
            var headers = fmtColumns.ConvertAll(c => c.ServerColumnName).ToArray();
            var delimiters = GetDelimitersFromFmt(fmtColumns);
            using (var reader = new DelimitedDataReader(dataFile, headers, delimiters))
            using (var conn = new SqlConnection("Server=.;Database=CODIS;Trusted_Connection=True;"))
            {
                conn.Open();
                using (var bulk = new SqlBulkCopy(conn))
                {
                    bulk.DestinationTableName = tableName;
                    foreach (var col in fmtColumns)
                    {
                        bulk.ColumnMappings.Add(col.FieldOrder - 1, col.ServerColumnName);
                    }

                    bulk.WriteToServer(reader);
                }
            }
        }

        public static List<FmtColumn> ParseFmtFile(string fmtPath)
        {
            var columns = new List<FmtColumn>();
            var lines = File.ReadAllLines(fmtPath);

            if (lines.Length < 3)
                throw new InvalidOperationException("Invalid .fmt file");

            var columnCount = int.Parse(lines[1].Trim());

            for (var i = 2; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 7) continue;
                var col = new FmtColumn
                {
                    FieldOrder = int.Parse(parts[0]),
                    SqlType = parts[1],
                    PrefixLength = int.Parse(parts[2]),
                    MaxLength = int.Parse(parts[3]),
                    Terminator = parts[4].Trim('"'),
                    ServerColumnOrder = int.Parse(parts[5]),
                    ServerColumnName = parts[6],
                    Collation = parts.Length > 7 ? parts[7] : ""
                };
                columns.Add(col);
            }

            if (columns.Count != columnCount)
                throw new InvalidOperationException("Mismatch between declared and parsed column counts.");

            return columns;
        }

        public static List<string> GetDelimitersFromFmt(List<FmtColumn> columns)
        {
            var delimiters = new List<string>();
            foreach (var col in columns)
            {
                delimiters.Add(col.Terminator);
            }

            return delimiters;
        }
    }

    public class FmtColumn
    {
        public int FieldOrder { get; set; }
        public string SqlType { get; set; } = "";
        public int PrefixLength { get; set; }
        public int MaxLength { get; set; }
        public string Terminator { get; set; } = "";
        public int ServerColumnOrder { get; set; }
        public string ServerColumnName { get; set; } = "";
        public string Collation { get; set; } = "";
    }

    public class DelimitedDataReader : IDataReader
    {
        private readonly StreamReader _reader;
        private readonly string[] _headers;
        private readonly List<string> _delimiters;
        private string[] _currentRow;

        public DelimitedDataReader(string filePath, string[] headers, List<string> delimiters)
        {
            _reader = new StreamReader(filePath);
            _headers = headers;
            _delimiters = delimiters;
        }

        public object GetValue(int i) => _currentRow[i];
        public int FieldCount => _headers.Length;
        public string GetName(int i) => _headers[i];


        public bool Read()
        {
            var line = _reader.ReadLine();
            if (line == null) return false;
            _currentRow = SplitLine(line, _delimiters, _headers.Length);
            return true;
        }

        private static string[] SplitLine(string line, List<string> delimiters, int expectedFields)
        {
            var fields = new List<string>();
            int start = 0;
            for (int i = 0; i < delimiters.Count && fields.Count < expectedFields - 1; i++)
            {
                int idx = line.IndexOf(delimiters[i], start, StringComparison.Ordinal);
                if (idx == -1) break;
                fields.Add(line.Substring(start, idx - start));
                start = idx + delimiters[i].Length;
            }

            fields.Add(line.Substring(start));
            return fields.ToArray();
        }

        public int Depth => 0;
        public bool IsClosed => _reader.EndOfStream;
        public int RecordsAffected => -1;
        public void Close() => _reader.Dispose();
        public void Dispose() => _reader.Dispose();
        public bool NextResult() => false;

        // methods used by SqlBulkCopy
        public bool GetBoolean(int i) => bool.Parse(GetValue(i).ToString());
        public int GetInt32(int i) => int.Parse(GetValue(i).ToString());
        public string GetString(int i) => GetValue(i).ToString();
        public decimal GetDecimal(int i) => decimal.Parse(GetValue(i).ToString());


        // indexer
        public object this[int i] => GetValue(i);
        public object this[string name] => GetValue(GetOrdinal(name));

        // schema handling
        public DataTable GetSchemaTable()
        {
            var schema = new DataTable();
            foreach (var header in _headers)
            {
                schema.Columns.Add(header, typeof(string));
            }

            return schema;
        }

        public int GetOrdinal(string name)
        {
            for (int i = 0; i < _headers.Length; i++)
            {
                if (string.Equals(_headers[i], name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            throw new IndexOutOfRangeException($"Column not found: {name}");
        }

        public bool IsDBNull(int i) => string.IsNullOrEmpty(GetValue(i).ToString());
        public Type GetFieldType(int i) => typeof(string);

        public int GetValues(object[] values)
        {
            _currentRow.CopyTo(values, 0);
            return _currentRow.Length;
        }

        public string GetDataTypeName(int i) => "string";

        public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferOffset, int length) =>
            throw new NotImplementedException();

        public long GetChars(int i, long fieldOffset, char[] buffer, int bufferOffset, int length) =>
            throw new NotImplementedException();

        public long GetInt64(int i) => long.Parse(GetValue(i).ToString());
        public float GetFloat(int i) => float.Parse(GetValue(i).ToString());
        public double GetDouble(int i) => double.Parse(GetValue(i).ToString());
        public byte GetByte(int i) => byte.Parse(GetValue(i).ToString());
        public short GetInt16(int i) => short.Parse(GetValue(i).ToString());
        public char GetChar(int i) => char.Parse(GetValue(i).ToString());
        public Guid GetGuid(int i) => Guid.Parse(GetValue(i).ToString());
        public DateTime GetDateTime(int i) => DateTime.Parse(GetValue(i).ToString());
        public IDataReader GetData(int i) => throw new NotImplementedException();
    }
}