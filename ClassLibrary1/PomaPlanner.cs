using BatteryPomaPlanner;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.Common.Solution;
using Powel.Optimal.MultiAsset.Infrastructure;

namespace POMAplanner
{
    public class PomaPlanner
    {
        private ILogger _logger;
        private PomaServiceClient _multiAssetService;
        
        public void RunPOMA()
        {
            _logger = new 
            _multiAssetService = new PomaServiceClient(new AppSettingProvider(), _logger);
        }

        private async Task<PomaSolutionSet> CallMultiAssetService(MultiAssetData options)
        {
            return await _multiAssetService.Run<PomaSolutionSet>("api/hydroThermal/start", options);
        }

    }
}
