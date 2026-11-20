using Clinique2000_Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clinique2000_Core.ViewModels
{
    public class ListeAttenteVM
    {
        public ListeAttente ListeAttente { get; set; }
        public List<PlageHoraire>? PlagesHoraires { get; set; }
        public List<Consultation> Consultations { get; set; } = new();
        public List<CreneauDisponibleVM> CreneauxDisponibles { get; set; } = new();
        public Consultation? MonRendezVousAujourdhui { get; set; }
        public DateTime MaintenantClinique { get; set; }
        public bool PeutAnnulerMonRendezVous { get; set; }
        public bool EstPatientSeulement { get; set; }
        public bool AfficherNomsPatients { get; set; }
    }


}
