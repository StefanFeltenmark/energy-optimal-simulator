using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MathNet.Numerics.Interpolation;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.OPS.Core.String;

namespace BatterySimulator
{
    public class SpotPriceReader
    {
        
        
        public SpotPriceReader() { }

        public static Dictionary<string, TimeSeries> ReadFile(string filename)
        {
            Dictionary<string, TimeSeries> spotPrices = new Dictionary<string, TimeSeries>();

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
            

            var lines = str.Split("\n",StringSplitOptions.RemoveEmptyEntries);

            var lineData = lines[2].Split(";");

            for (int i = 2; i < lineData.Length; i++)
            {
                string priceArea = lineData[i];
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
