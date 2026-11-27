namespace Clinique2000_Core.ViewModels
{
    public class ListeAttenteDashboardVM
    {
        public string NomUtilisateur { get; set; } = string.Empty;
        public string Courriel { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public List<ListeAttenteDashboardCliniqueVM> Cliniques { get; set; } = new();
        public List<ListeAttenteDashboardItemVM> ListesAttente { get; set; } = new();
    }

    public class ListeAttenteDashboardCliniqueVM
    {
        public int CliniqueID { get; set; }
        public string NomClinique { get; set; } = string.Empty;
        public int? FuseauHoraire { get; set; }
        public string? NomEmploye { get; set; }
        public bool EstReceptionniste { get; set; }
        public bool EstAdminClinique { get; set; }
        public DateTime MaintenantLocale { get; set; }
    }

    public class ListeAttenteDashboardItemVM
    {
        public int ListeAttenteID { get; set; }
        public int CliniqueID { get; set; }
        public string NomClinique { get; set; } = string.Empty;
        public DateTime DateEffectivite { get; set; }
        public TimeSpan HeureOuverture { get; set; }
        public TimeSpan HeureFermeture { get; set; }
        public bool IsOuverte { get; set; }
        public bool ReservationsActivees { get; set; }
        public int NbMedecinsDispo { get; set; }
        public int RendezVousAVenir { get; set; }
        public int RendezVousEnCours { get; set; }
        public int ConsultationsTerminees { get; set; }
        public int RendezVousAnnulesParPatient { get; set; }
        public int RendezVousNonPresentes { get; set; }
        public int TotalRendezVous { get; set; }
        public string EtatTemporel { get; set; } = string.Empty;
        public string Quand { get; set; } = string.Empty;
    }
}
