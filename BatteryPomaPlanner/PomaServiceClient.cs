using System.Net.Http.Headers;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.Domain.Infrastructure.Interfaces;

namespace BatteryPomaPlanner
{
    public class PomaServiceClient 
    {
        private readonly ILogger _logger;
        private string _baseAddress;
        private readonly IAppSettingProvider _appSettingProvider;
        
        public PomaServiceClient(IAppSettingProvider appSettingProvider, ILogger logger)
        {
            _logger = logger;
            _appSettingProvider = appSettingProvider;
        }

        private static string EnsureTrailingSlash(string address)
        {
            return address.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar;
        }

        
        public async Task<TResult> Run<TResult>(string requestUri, MultiAssetData parameters)
        {
            HttpResponseMessage response;

            var endpoint = "http://localhost:18262/MultiAsset/";
            
            
            _logger.Info($"Connecting to POMA service: {endpoint}");
            _baseAddress = EnsureTrailingSlash(endpoint);
        
            using (var client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true,
                       MaxConnectionsPerServer = 1}))
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-protobuf"));
                client.Timeout = TimeSpan.FromMinutes(10);
                
                client.BaseAddress = new Uri(_baseAddress);

                // How to call the health endpoint
                //Uri? uri  = new Uri("http://localhost:18262/MultiAsset/health");
                //HttpResponseMessage httpResponseMessage = client.GetAsync(uri).Result;
                //if(httpResponseMessage.IsSuccessStatusCode && httpResponseMessage.StatusCode == HttpStatusCode.OK)
                //{
                //    _logger.Info("Health check passed");
                //}
                //else
                //{
                //    _logger.Error("Health check failed");
                //    throw new Exception("Health check failed");
                //}

                response = await client.PostAsync(requestUri, parameters, new ProtocolBuffersFormatter());

                if (response == null)
                    throw new Exception("Unable to connect to the multi asset service");
                
                if (!response.IsSuccessStatusCode)
                    _logger.Error($"Received error code: {response.StatusCode} from multi asset service");

            }
                
            
            VerifyResponse(requestUri, response);
            var result = await response.Content.ReadAsAsync<TResult>(new[] { new ProtocolBuffersFormatter() });
            return result;
        }

       

        private void VerifyResponse(string requestUri, HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"Failed to call MultiAsset at {requestUri}");
                _logger.Error($"StatusCode: {response.StatusCode}");
                _logger.Error($"ReasonPhrase: {response.ReasonPhrase}");
                var content = response.Content.ReadAsStringAsync().Result;
                _logger.Error($"Content: {content}");

                throw new InvalidOperationException("Failed to call MultiAsset");
            }
        }
    }
    
}
