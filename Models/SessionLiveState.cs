namespace BotApp.Models
{
    public class SessionLiveState
    {
        public string? Page { get; set; } // "Denuncia/Descripcion"
        public string? DescripcionBuffer { get; set; }
        public bool AddMore { get; set; }
        public bool HumanHandoff { get; set; }
    }
}
