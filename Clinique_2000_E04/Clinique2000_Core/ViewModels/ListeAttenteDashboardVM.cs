namespace Clinique2000_Core.ViewModels
{
    public class ListeAttenteDashboardVM
    {
        public string NomUtilisateur { get; set; } = string.Empty;
        public string Courriel { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public List<ListeAttenteDashboardItemVM> ListesAttente { get; set; } = new();
    }

    public class ListeAttenteDashboardItemVM
    {
        public int ListeAttenteID { get; set; }
        public string NomClinique { get; set; } = string.Empty;
        public DateTime DateEffectivite { get; set; }
        public TimeSpan HeureOuverture { get; set; }
        public TimeSpan HeureFermeture { get; set; }
        public bool IsOuverte { get; set; }
        public int NbMedecinsDispo { get; set; }
        public int RendezVousPlanifies { get; set; }
        public int RendezVousAnnules { get; set; }
        public int ConsultationsTerminees { get; set; }
    }
}
