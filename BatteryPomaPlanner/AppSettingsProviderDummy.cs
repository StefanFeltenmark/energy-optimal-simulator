using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Powel.Optimal.Log.Client;
using Powel.Optimal.MultiAsset.Infrastructure;

namespace BatteryPomaPlanner
{
    public class AppSettingsProviderDummy :IAppSettingProvider
    {
        public bool ExcelSolutionOutput { get; set; }
        public bool LogServiceTrace { get; set; }
        public bool StartBrowser { get; set; }
        public bool UseJsonData { get; set; }
        public bool UseSSL { get; set; }
        public int CplexThreadLimitLicense { get; set; }
        public int DeleteFilesOlderThanInDays { get; set; }
        public int LagrangeThreadCount { get; set; }
        public int ServerPort { get; set; }
        public string LogServiceBaseAddress { get; set; }
        public List<string> LogServiceBaseAddresses { get; set; }
        public LogLevel LogLevel { get; set; }
        public string BaseAddress { get; set; }
        public string JsonDataPath { get; set; }
        public string LagrangeConfigFile { get; set; }
        public string LocalAddress { get; set; }
        public string LogFile { get; set; }
        public string Protocol { get; set; }
        public string SolverDirectory { get; set; }
        public int HttpHealthCheckTimeoutSeconds { get; set; }
        public TimeSpan HttpHealthCheckTimeout { get; set; }
    }
}
