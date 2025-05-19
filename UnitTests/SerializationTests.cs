using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage.Providers;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using ProtoBuf.Meta;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests
{
    [Collection("Battery collection")]
    public class SerializationTests
    {
        private readonly ITestOutputHelper _output;


        public SerializationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ProtobufSerializesDataCorrectly2()
        {

            string path = "C:\\temp\\Simulator";

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            const string protobufFileName = "ProtoBuf.bin";

           var data = GetData();

            MultiAssetData? recovered = null;

            var settings1 = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto, Formatting = Formatting.Indented };

            var json1 = JsonConvert.SerializeObject(data, settings1);

            var size1 = json1.Length;
            _output.WriteLine($"Json1 size : {size1}");

            var settings2 = new JsonSerializerSettings { Formatting = Formatting.Indented };

            var recover = JsonConvert.DeserializeObject<Power>(json1, settings2);
            if (recover != null)
            {
                _output.WriteLine("Recovered Successfully");
            }

            var typeModel = CreateTypeModel();

            // protobuf serialization
            using (var fs = new FileStream(protobufFileName, FileMode.Create))
            {
                typeModel.Serialize(fs, data);
                fs.Close();
            }

            // protobuf deserialization
            using (var fs = File.OpenRead(protobufFileName))
            {
                recovered = typeModel.Deserialize(fs, null, typeof(MultiAssetData)) as MultiAssetData;
                fs.Close();
            }

            var json2 = JsonConvert.SerializeObject(recovered, settings1);

            if (!json1.Equals(json2))
            {
                File.WriteAllText(Path.Combine(path,@"ProtobufSerializesCorrectly1.json"), json1);
                File.WriteAllText(Path.Combine(path,@"ProtobufSerializesCorrectly2.json"), json2);
            }
            else
            {
                File.WriteAllText(Path.Combine(path, @"ProtobufSerializesCorrectly2.json"), json2);
            }

            Assert.Equal(json1, json2);
        }

        private static RuntimeTypeModel CreateTypeModel()
        {
            var typeModel = RuntimeTypeModel.Create();
       
            typeModel.IncludeDateTimeKind = true;

            foreach (var relation in ProtocolBuffersConfiguration.Relations)
            {
                typeModel[relation.BaseClass].AddSubType(relation.Index, relation.DerivedClass);
            }

            return typeModel;
        }

        private MultiAssetData GetData()
        {
            MultiAssetData data = new MultiAssetData();

            data.OptimizationId = Guid.NewGuid();

            EnergyMarket market = new EnergyMarket(Guid.NewGuid(), "EPEX Intraday");

            Battery battery = new Battery
            {
                Id = Guid.NewGuid(),
                Name = "Battery1",
                NominalChargeCapacity = new Power(10,Units.MegaWatt),
                NominalEnergyCapacity =  new Energy(20, Units.MegaWattHour),
                InitialSoHc = new Percentage(100),
                InitialSoHe = new Percentage(100),
                InitialCapacityC = new Power(10,Units.MegaWatt),
                InitialCapacityE = new Energy(20, Units.MegaWattHour),
                ChargeEfficiency = new DimensionlessQuantity(0.95),
                DischargeEfficiency = new DimensionlessQuantity(0.95),
                MaxNumberOfEfcPerHour = 100
            };

            
            PriceUnit priceUnit = new PriceUnit(Currencies.Euro, Units.MegaWatt);
            battery.DischargePoints =
            [
                new ChargePoint(new Power(0, Units.MegaWatt), new UnitPrice(0, priceUnit)),
                new ChargePoint(new Power(10, Units.MegaWatt), new UnitPrice(0, priceUnit))
            ];
            battery.ChargePoints =
            [
                new ChargePoint(new Power(0, Units.MegaWatt), new UnitPrice(0, priceUnit)),
                new ChargePoint(new Power(10, Units.MegaWatt), new UnitPrice(0, priceUnit))

            ];


            data.EnergyStorage = new EnergyStorageData();
            
            data.EnergyStorage.Add(battery);

             data.CommonData = new CommonData();
            

            data.CommonData.EnergyMarkets.Add(market);

            // Market data
            market.Ts.PowerBuyMax = new TimeSeries();
            market.Ts.PowerBuyMax.DefaultValue = 1000000;
            market.Ts.PowerBuyMin = new TimeSeries();
            market.Ts.PowerSellMin = new TimeSeries();
            market.Ts.PowerSellMax = new TimeSeries();
            market.Ts.PowerSellMax.DefaultValue = 1000000;
            market.Ts.EnergyDeficitPenaltyPrice = new TimeSeries();
            market.Ts.EnergyDeficitPenaltyPrice.DefaultValue = 1000000;
            market.Ts.EnergySurplusPenaltyPrice = new TimeSeries();
            market.Ts.EnergySurplusPenaltyPrice.DefaultValue = 1000000;
            market.Ts.PowerLoad = new TimeSeries();

            market.EnergyProviders = new List<EnergyProvider>();
            BatteryEnergyProvider provider = new BatteryEnergyProvider(market.Id, battery.Id)
            {
                Battery = battery,
                EnergyMarket = market,
                Availability = new TimeSeries()
            };
            provider.Availability.DefaultValue = 1;
            provider.MaxDelivery = new TimeSeries();
            provider.MaxDelivery.DefaultValue = 100;
            provider.MinDelivery = new TimeSeries();
            provider.MinDelivery.DefaultValue = -100;
            
            market.EnergyProviders.Add(provider);

            // Battery time series
            battery.Ts.SocSoftMaxPenaltyPrice = new TimeSeries();
            battery.Ts.SocSoftMinPenaltyPrice = new TimeSeries();
            battery.Ts.SocSoftMaxPenaltyPrice.DefaultValue = 1000000;
            battery.Ts.SocSoftMinPenaltyPrice.DefaultValue = 1000000;
            battery.Ts.ChargeCost = new TimeSeries();
            battery.Ts.DischargeCost = new TimeSeries();
            battery.Ts.AvailabilityFlag = new TimeSeries();
            battery.Ts.AvailabilityFlag.DefaultValue = 1;
            battery.Ts.AvailableEnergyPercent = new TimeSeries();
            battery.Ts.AvailableEnergyPercent.DefaultValue = 100;
            battery.Ts.ChargeMax = new TimeSeries();
            battery.Ts.ChargeMax.DefaultValue = battery.NominalChargeCapacity.Value;
            battery.Ts.DischargeMax = new TimeSeries();
            battery.Ts.DischargeMax.DefaultValue = battery.NominalChargeCapacity.Value;
            battery.Ts.EnergyContentCorrection = new TimeSeries();
            battery.Ts.MipFlag = new TimeSeries();
            battery.Ts.SocMax = new TimeSeries();
            battery.Ts.SocMax.DefaultValue = 1;
            battery.Ts.SocMin = new TimeSeries();
            battery.Ts.SoftSchedule = new TimeSeries();
            battery.Ts.SoftScheduleFlag = new TimeSeries();
            battery.Ts.SocSoftMax = new TimeSeries();
            battery.Ts.SocSoftMax.DefaultValue = 1;
            battery.Ts.SocSoftMin = new TimeSeries();
            battery.FinalSocMax = new Percentage(100);
            battery.FinalSocMin = new Percentage(0);
         //   battery.FinalStoragePrice = new UnitPrice(1000, new PriceUnit(Currencies.Euro, Units.MegaWattHour));
         //   battery.FinalSocPenaltyPrice = new UnitPrice(1000, new PriceUnit(Currencies.Euro, Units.Percent));

            data.CommonData.Parameters.CaseName = "BatterySimulation";

            return data;
        }
    }
}

   
