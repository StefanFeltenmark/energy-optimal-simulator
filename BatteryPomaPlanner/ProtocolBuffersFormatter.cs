using System.Net;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using Powel.Optimal.MultiAsset.Domain;
using ProtoBuf;
using ProtoBuf.Meta;

namespace BatteryPomaPlanner
{
    /// <inheritdoc />
    /// <summary>
    /// Media type formatter Protocol Buffers
    /// </summary>
    public class ProtocolBuffersFormatter : MediaTypeFormatter
    {
        private static readonly Lazy<RuntimeTypeModel> Model = new Lazy<RuntimeTypeModel>(CreateTypeModel);

        /// <inheritdoc />
        /// <summary>
        /// Create an instance of ProtocolBuffersFormatter
        /// </summary>
        public ProtocolBuffersFormatter()
        {
            SupportedMediaTypes.Add(DefaultMediaType);
            Model.Value.IncludeDateTimeKind = true;
        }

        /// <summary>
        /// Get default media type
        /// </summary>
        public static MediaTypeHeaderValue DefaultMediaType { get; } = new MediaTypeHeaderValue("application/x-protobuf");

        /// <inheritdoc />
        public override bool CanReadType(Type type)
        {
            return CanHandleType(type);
        }

        /// <inheritdoc />
        public override bool CanWriteType(Type type)
        {
            return CanHandleType(type);
        }

        /// <inheritdoc />
        public override Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger)
        {
            var tcs = new TaskCompletionSource<object>();

            try
            {
                object result = Model.Value.Deserialize(readStream, null, type);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return tcs.Task;
        }

        /// <inheritdoc />
        public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext)
        {
            content.Headers.ContentType = DefaultMediaType;
            var tcs = new TaskCompletionSource<object>();

            try
            {
                if (value != null)
                {
                    Model.Value.Serialize(writeStream, value);
                }
                
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return tcs.Task;
        }

        private static RuntimeTypeModel CreateTypeModel()
        {
            var typeModel = RuntimeTypeModel.Create();

            typeModel.IncludeDateTimeKind = true;

            foreach (var relation in ProtocolBuffersConfiguration.Relations)
            {
                typeModel[relation.BaseClass].AddSubType(relation.Index, relation.DerivedClass);
            }
            RuntimeTypeModel.Default.IncludeDateTimeKind = true;

            return typeModel;
        }

        private static bool CanHandleType(MemberInfo type)
        {
	        var customAttributes = type.GetCustomAttributes(typeof(DataContractAttribute));

	        foreach (var unused in customAttributes)
	        {
		        return true;	// we have one
	        }

	        customAttributes = type.GetCustomAttributes(typeof(ProtoContractAttribute));

	        foreach (var unused in customAttributes)
	        {
		        return true;    // we have one
			}

	        return false;
        }
	}
}
