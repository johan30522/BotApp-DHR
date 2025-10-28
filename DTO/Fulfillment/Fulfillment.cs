namespace BotApp.DTO.Fulfillment
{
    public class Fulfillment
    {
        public class FulfillmentRequest
        {
            public string? detectIntentResponseId { get; set; }
            public string? text { get; set; }            // texto del usuario (si viene)
            public string? languageCode { get; set; }
            public FulfillmentInfo fulfillmentInfo { get; set; } = default!;
            public SessionInfo sessionInfo { get; set; } = default!;
            public PageInfo? pageInfo { get; set; } = default!;
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

        public class PageInfo
        {
            public FormInfo? formInfo { get; set; }
            public string? currentPage { get; set; } // a veces CX lo envía
        }
        public class FormInfo
        {
            public List<ParameterInfo>? parameterInfo { get; set; }
        }

        public class ParameterInfo
        {
            public string? displayName { get; set; }
            public string? state { get; set; } // REQUIRED / FILLED / etc.

            public bool? justCollected { get; set; }     // <-- IMPORTANTE
            public object? value { get; set; }            // (puede venir)
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
