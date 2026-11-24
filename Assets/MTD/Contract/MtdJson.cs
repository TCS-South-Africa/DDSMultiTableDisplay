using System;
using System.Text;
using Newtonsoft.Json;

namespace MTD
{
    // One place for the JSON settings every MTD message is read and written with.
    public static class MtdJson
    {
        static readonly JsonSerializerSettings settings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.DateTimeOffset,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, settings);
        }

        // Returns false for anything that is not a usable message: empty, not JSON, or the wrong shape.
        public static bool TryDeserialize<T>(byte[] payload, out T message, out string error) where T : class
        {
            message = null;
            error = null;

            if (payload == null || payload.Length == 0)
            {
                error = "empty payload";
                return false;
            }

            try
            {
                message = JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(payload), settings);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (message == null)
            {
                error = "payload is null";
                return false;
            }

            return true;
        }
    }
}
