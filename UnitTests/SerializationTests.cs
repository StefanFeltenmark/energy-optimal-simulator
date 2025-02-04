using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
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
            const string protobufFileName = "ProtoBuf.bin";

            MultiAssetData data = new MultiAssetData();
            data.OptimizationId = Guid.NewGuid();
            Battery battery = new Battery();
            data.EnergyStorage = new EnergyStorageData();
            data.EnergyStorage.Add(battery);

            MultiAssetData recovered = null;

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
                File.WriteAllText(@"ProtobufSerializesCorrectly1.json", json1);
                File.WriteAllText(@"ProtobufSerializesCorrectly2.json", json2);
            }
            else
            {
                File.WriteAllText(@"ProtobufSerializesCorrectly2.json", json2);
            }

            Assert.Equal(json1, json2);
        }

        private static RuntimeTypeModel CreateTypeModel()
        {
            var typeModel = TypeModel.Create();
       
            typeModel.IncludeDateTimeKind = true;

            foreach (var relation in ProtocolBuffersConfiguration.Relations)
            {
                typeModel[relation.BaseClass].AddSubType(relation.Index, relation.DerivedClass);
            }

            return typeModel;
        }
    }
}
