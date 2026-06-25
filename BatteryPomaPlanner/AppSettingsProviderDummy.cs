
using Powel.Optimal.Domain.Infrastructure.Interfaces;
using Volue.Optimal.Log.Client;

namespace BatteryPomaPlanner
{
    public class AppSettingsProviderDummy : IAppSettingProvider
    {
        // ILogClientOptions
        public List<string> LogServiceBaseAddresses { get; set; } = new();
        public bool LogClientEnableDetailedErrors { get; set; }
        public bool LogClientConfirmServerDelivery { get; set; }

        // IAppSettingProvider
        public bool ExcelSolutionOutput { get; set; }
        public bool StartBrowser { get; set; }
        public bool UseJsonData { get; set; }
        public bool UseSSL { get; set; }
        public bool GcBlocking { get; set; }
        public int CplexMaxThreads { get; set; }
        public int DeleteFilesOlderThanInDays { get; set; }
        public int ServerPort { get; set; }
        public LogLevel LogLevel { get; set; }
        public TimeSpan HttpHealthCheckTimeout { get; set; }
        public string BaseAddress { get; set; } = string.Empty;
        public string JsonDataPath { get; set; } = string.Empty;
        public string LagrangeConfigFile { get; set; } = string.Empty;
        public string LocalAddress { get; set; } = string.Empty;
        public string LogFile { get; set; } = string.Empty;
        public string Protocol { get; set; } = string.Empty;
        public string SolverDirectory { get; set; } = string.Empty;
    }
}
