using System.Globalization;
using Powel.Optimal.MultiAsset.Domain.General.Data;


namespace BatterySimulator
{
    public class SpotPriceReader
    {
        
        
        public SpotPriceReader() { }

        /// <summary>
        /// Reads day-ahead prices per price area, keyed by local clock time (the simulator's time axis). Accepts
        /// Nord Pool's hourly Elspot export (semicolon-separated, decimal commas) and the 15-minute CSV written by
        /// scripts/fetch_volue_prices.py.
        /// </summary>
        public static Dictionary<string, TimeSeries> ReadFile(string filename)
        {
            // read data from file with name 'filename' on csv format
            string str = "";
            try
            {
                str = File.ReadAllText(filename);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            return str.StartsWith("start,") ? ReadQuarterHourCsv(str) : ReadNordPoolHourly(str);
        }

        /// <summary>
        /// The fetch script's format: "start" as local time with UTC offset, then one column per series; columns
        /// without a space are day-ahead prices per area, the others (intraday, auctions) are not read here.
        /// At the autumn DST switch the repeated local hour keeps its second occurrence, as in the Nord Pool file.
        /// </summary>
        private static Dictionary<string, TimeSeries> ReadQuarterHourCsv(string text)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string[] header = lines[0].Split(',');
            var areaColumns = Enumerable.Range(1, header.Length - 1).Where(i => !header[i].Contains(' ')).ToList();

            var spotPrices = areaColumns.ToDictionary(i => header[i], _ => new TimeSeries());
            foreach (string line in lines.Skip(1))
            {
                string[] data = line.Split(',');
                DateTime local = DateTimeOffset.Parse(data[0], CultureInfo.InvariantCulture).DateTime;
                foreach (int i in areaColumns)
                {
                    if (i < data.Length && double.TryParse(data[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        spotPrices[header[i]].SetValueAt(local, value);
                }
            }
            return spotPrices;
        }

        /// <summary>
        /// One named column of a file in the fetch script's format (e.g. "SE3 forecast" from the forecast file),
        /// keyed by local clock time; empty cells are skipped.
        /// </summary>
        public static TimeSeries ReadColumn(string filename, string column)
        {
            var lines = File.ReadAllText(filename).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int index = Array.IndexOf(lines[0].Split(','), column);
            if (!lines[0].StartsWith("start,") || index < 1)
                throw new ArgumentException($"Column '{column}' not found in {filename}.");

            var series = new TimeSeries { IsBreakPointSeries = true };
            foreach (string line in lines.Skip(1))
            {
                string[] data = line.Split(',');
                if (index < data.Length && double.TryParse(data[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    series.SetValueAt(DateTimeOffset.Parse(data[0], CultureInfo.InvariantCulture).DateTime, value);
            }
            return series;
        }

        private static Dictionary<string, TimeSeries> ReadNordPoolHourly(string str)
        {
            Dictionary<string, TimeSeries> spotPrices = new Dictionary<string, TimeSeries>();


            var lines = str.Split("\n",StringSplitOptions.RemoveEmptyEntries);

            var lineData = lines[2].Split(";");

            for (int i = 2; i < lineData.Length; i++)
            {
                string priceArea = lineData[i].Trim();
                spotPrices.Add(priceArea, new TimeSeries());
            }

            
            for (int lineNo = 3; lineNo < lines.Length; lineNo++)
            {
                var data = lines[lineNo].Split(";", StringSplitOptions.RemoveEmptyEntries);
                var dateStrings= data[0].Split("-");
                
                char SpecNBSPChar = Convert.ToChar(65533);
                var temp2 = data[1].Replace(SpecNBSPChar, ' ');

                var hourStrings= temp2.Split("-",StringSplitOptions.TrimEntries);
                var date = new DateTime(int.Parse(dateStrings[2]), int.Parse(dateStrings[1]), int.Parse(dateStrings[0]), int.Parse(hourStrings[0]), 0, 0);

                for (int i = 2; i < data.Length; i++)
                {

                    bool ok = double.TryParse(data[i], out double val);
                    if (!ok)
                    {
                        val = 0;
                    }
                    spotPrices.ElementAt(i - 2).Value.SetValueAt(date, val);
                }

            }

         return spotPrices;

        }
    }
}
