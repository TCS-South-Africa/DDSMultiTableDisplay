using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MTD
{
    // The T-Connect messages MTD exchanges with the server. Shapes follow DDS's copies of the same messages
    // (DeviceAssignmentMessage, PingRequest) and the server's MtdTableListMessage.

    public class DeviceAssignmentMessage
    {
        public Guid RequestId { get; set; }
        public Guid DeviceId { get; set; }
        public string DeviceType { get; set; } // "MTD" for a multi-table display; DDS tables send "TABLE".
        public string TableName { get; set; } // For an MTD this is the display's name.
    }

    public enum AssignmentStatus
    {
        Assigned,
        Conflict,
        Error,
    }

    public class DeviceAssignmentResponse
    {
        public Guid? TableId { get; set; } // For an MTD this is the display's ID.
        public string TableName { get; set; }
        public string AssignmentCode { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public AssignmentStatus Status { get; set; }
    }

    public class PingRequest
    {
        public Guid DeviceId { get; set; }
        public DateTimeOffset ClientUtcTime { get; set; }
    }

    public class PingResponse
    {
        public Guid DeviceId { get; set; }
        public DateTimeOffset ServerUtcTime { get; set; }
    }

    // The value of the display's entry in the "mtd-table-list" Key-Value bucket.
    public class MtdTableListMessage
    {
        public Guid DisplayId { get; set; }
        public List<Guid> AvailableTableIds { get; set; }
    }
}
