namespace BotApp.DTO.Fulfillment
{
    public class Fulfillment
    {
        public class FulfillmentRequest
        {
            public FulfillmentInfo fulfillmentInfo { get; set; } = default!;
            public SessionInfo sessionInfo { get; set; } = default!;
        }

        public class FulfillmentInfo
        {
            public string tag { get; set; } = default!;
        }

        public class SessionInfo
        {
            public string session { get; set; } = default!;
            public Dictionary<string, object>? parameters { get; set; }
        }

        public class FulfillmentResponse
        {
            public FulfillmentMessage[] messages { get; set; } = Array.Empty<FulfillmentMessage>();
        }

        public class FulfillmentMessage
        {
            public FulfillmentText text { get; set; } = default!;
        }

        public class FulfillmentText
        {
            public string[] text { get; set; } = Array.Empty<string>();
        }
    }
}
