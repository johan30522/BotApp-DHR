using static BotApp.DTO.Fulfillment.Fulfillment;

namespace BotApp.Models
{
    public class DfcxRequest
    {
        public string? detectIntentResponseId { get; set; }
        public string? text { get; set; }            // texto del usuario (si viene)
        public string? languageCode { get; set; }
        public FulfillmentInfo fulfillmentInfo { get; set; }
        public SessionInfo sessionInfo { get; set; }
        public PageInfo pageInfo { get; set; }
    }



    public class PageInfo
    {
        public FormInfo formInfo { get; set; }
        public string? currentPage { get; set; } // a veces CX lo envía
    }
    public class FormInfo
    {
        public List<ParameterInfo> parameterInfo { get; set; }
    }
    public class ParameterInfo
    {
        public string displayName { get; set; }
        public string state { get; set; } // REQUIRED / FILLED / etc.

        public bool? justCollected { get; set; }     // <-- IMPORTANTE
        public object? value { get; set; }            // (puede venir)
    }
}

//    public class SessionInfo
//    {
//        public Dictionary<string, string> parameters { get; set; }
//         = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
//    }
//}
