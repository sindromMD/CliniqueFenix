using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Clinique2000_DataAccess.Data;
using Clinique2000_Services.IServices;
using Clinique2000_Core.Models;
using Clinique2000_Core.ViewModels;
using Microsoft.EntityFrameworkCore;
using Clinique2000_Utility.Enum;
using Clinique2000_Utility;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Clinique2000_Services.Services
{
    public class ListeAttenteService : ServiceBaseAsync<ListeAttente>, IListeAttenteService
    {

        private readonly CliniqueDbContext _context;
        private readonly IEmailService _emailService;

        public ListeAttenteService(CliniqueDbContext context, IEmailService emailService) : base(context)
        {
            _context = context;
            _emailService = emailService;
        }

        public async Task<List<ListeAttenteDashboardCliniqueVM>> ObtenirCliniquesAutoriseesDashboardAsync(
            string userId,
            string? email,
            bool emailConfirmed,
            bool isAdminClinique,
            bool isSuperAdmin)
        {
            var employesAutorises = _context.EmployesClinique.Where(employe =>
                (employe.UserID == userId ||
                 (emailConfirmed && email != null && employe.EmployeCliniqueCourriel == email)) &&
                (employe.EmployeCliniquePosition == EmployeCliniquePosition.Receptionniste ||
                 employe.EmployeCliniquePosition == EmployeCliniquePosition.AdminClinique));

            return await _context.Cliniques
                .AsNoTracking()
                .Where(clinique =>
                    isSuperAdmin ||
                    (isAdminClinique && clinique.CreateurID == userId) ||
                    employesAutorises.Any(employe => employe.CliniqueID == clinique.CliniqueID))
                .OrderBy(clinique => clinique.NomClinique)
                .Select(clinique => new ListeAttenteDashboardCliniqueVM
                {
                    CliniqueID = clinique.CliniqueID,
                    NomClinique = clinique.NomClinique,
                    FuseauHoraire = _context.AdressesQuebec
                        .Where(adresse => adresse.PostalCode == clinique.Adresse.CodePostal)
                        .Select(adresse => (int?)adresse.TimeZone)
                        .FirstOrDefault(),
                    NomEmploye = employesAutorises
                        .Where(employe => employe.CliniqueID == clinique.CliniqueID)
                        .Select(employe => (employe.EmployeCliniquePrenom + " " + employe.EmployeCliniqueNom).Trim())
                        .FirstOrDefault(),
                    EstReceptionniste = employesAutorises.Any(employe =>
                        employe.CliniqueID == clinique.CliniqueID &&
                        employe.EmployeCliniquePosition == EmployeCliniquePosition.Receptionniste),
                    EstAdminClinique = employesAutorises.Any(employe =>
                        employe.CliniqueID == clinique.CliniqueID &&
                        employe.EmployeCliniquePosition == EmployeCliniquePosition.AdminClinique)
                })
                .ToListAsync();
        }

        public async Task<List<ListeAttenteDashboardItemVM>> ObtenirDashboardAsync(
            IReadOnlyCollection<ListeAttenteDashboardCliniqueVM> cliniques,
            DateTime maintenantUtc)
        {
            if (maintenantUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("La date doit être en UTC.", nameof(maintenantUtc));
            }

            foreach (var clinique in cliniques)
            {
                if (!clinique.FuseauHoraire.HasValue)
                {
                    throw new ValidationException($"Le fuseau horaire de la clinique {clinique.NomClinique} est introuvable.");
                }

                clinique.MaintenantLocale = CliniqueTimeZone.ConvertUtcToLocal(
                    maintenantUtc,
                    clinique.FuseauHoraire.Value);
            }

            if (cliniques.Count == 0)
            {
                return new List<ListeAttenteDashboardItemVM>();
            }

            var listeParameter = Expression.Parameter(typeof(ListeAttente), "liste");
            var dateEffectivite = Expression.Property(
                Expression.Property(listeParameter, nameof(ListeAttente.DateEffectivite)),
                nameof(DateTime.Date));
            Expression filtre = Expression.Constant(false);

            foreach (var clinique in cliniques)
            {
                var clinicAndDate = Expression.AndAlso(
                    Expression.Equal(
                        Expression.Property(listeParameter, nameof(ListeAttente.CliniqueID)),
                        Expression.Constant(clinique.CliniqueID)),
                    Expression.GreaterThanOrEqual(
                        dateEffectivite,
                        Expression.Constant(clinique.MaintenantLocale.Date)));
                filtre = Expression.OrElse(filtre, clinicAndDate);
            }

            var predicate = Expression.Lambda<Func<ListeAttente, bool>>(filtre, listeParameter);
            var maintenantParClinique = cliniques.ToDictionary(clinique => clinique.CliniqueID);
            var resultats = await _context.ListeAttentes
                .AsNoTracking()
                .Where(predicate)
                .OrderBy(liste => liste.DateEffectivite)
                .ThenBy(liste => liste.Clinique.NomClinique)
                .Select(liste => new ListeAttenteDashboardItemVM
                {
                    ListeAttenteID = liste.ListeAttenteID,
                    CliniqueID = liste.CliniqueID,
                    NomClinique = liste.Clinique.NomClinique,
                    DateEffectivite = liste.DateEffectivite,
                    HeureOuverture = liste.HeureOuverture,
                    HeureFermeture = liste.HeureFermeture,
                    IsOuverte = liste.IsOuverte,
                    ReservationsActivees = liste.ReservationsActivees,
                    NbMedecinsDispo = liste.NbMedecinsDispo,
                    RendezVousAVenir = _context.Consultations.Count(consultation =>
                        consultation.PlageHoraire.ListeAttenteID == liste.ListeAttenteID &&
                        consultation.PatientID.HasValue &&
                        (consultation.StatutConsultation == StatutConsultation.EnAttente ||
                         consultation.StatutConsultation == StatutConsultation.EnRetard)),
                    RendezVousEnCours = _context.Consultations.Count(consultation =>
                        consultation.PlageHoraire.ListeAttenteID == liste.ListeAttenteID &&
                        consultation.StatutConsultation == StatutConsultation.EnCours),
                    ConsultationsTerminees = _context.Consultations.Count(consultation =>
                        consultation.PlageHoraire.ListeAttenteID == liste.ListeAttenteID &&
                        consultation.StatutConsultation == StatutConsultation.Termine),
                    RendezVousAnnulesParPatient = liste.NbAnnulationsPatient,
                    RendezVousNonPresentes = _context.Consultations.Count(consultation =>
                        consultation.PlageHoraire.ListeAttenteID == liste.ListeAttenteID &&
                        consultation.PatientID.HasValue &&
                        consultation.StatutConsultation == StatutConsultation.Annulle)
                })
                .ToListAsync();

            var culture = CultureInfo.GetCultureInfo("fr-CA");
            foreach (var item in resultats)
            {
                var maintenantLocale = maintenantParClinique[item.CliniqueID].MaintenantLocale;
                var ouverture = item.DateEffectivite.Date.Add(item.HeureOuverture);
                var fermeture = item.DateEffectivite.Date.Add(item.HeureFermeture);
                item.EtatTemporel = maintenantLocale >= fermeture
                    ? "Terminée"
                    : maintenantLocale >= ouverture
                        ? "En cours"
                        : "À venir";
                item.Quand = item.DateEffectivite.Date == maintenantLocale.Date
                    ? "Aujourd'hui"
                    : item.DateEffectivite.Date == maintenantLocale.Date.AddDays(1)
                        ? "Demain"
                        : culture.TextInfo.ToTitleCase(item.DateEffectivite.ToString("dddd", culture));
                item.TotalRendezVous =
                    item.RendezVousAVenir +
                    item.RendezVousEnCours +
                    item.ConsultationsTerminees +
                    item.RendezVousAnnulesParPatient +
                    item.RendezVousNonPresentes;
            }

            return resultats;
        }

        public async Task<ListeAttente> CreerListeAttenteAsync(ListeAttente listeAttente)
        {


            if (VerifierSiListeAttenteExisteMemeJourClinique(listeAttente.DateEffectivite, listeAttente.CliniqueID))
            {
                throw new ValidationException("Il existe deja une liste d'attente dans la meme clinique pour la meme date.");
            }

            if (!VerifierSiDateEffectiviteValide(listeAttente) || !VerifierSiHeureOuvertureValide(listeAttente))
            {
                throw new ValidationException("La liste n'est pas valide");
            }
            else
            {
                await CreerAsync(listeAttente);
                return listeAttente;
            }
        }

        /// <summary>
        /// Cette methode s occupe de faire les verifications lors d'une creation de liste d'attente, puis faire la creation elle meme
        /// si une des verifications echoue, elle renvoie une erreur de validation 
        /// </summary>
        /// <param name="listeAttente"></param>
        /// <returns></returns>
        /// <exception cref="ValidationException"></exception>
        public async Task<ListeAttente> ModifierListeAttenteAsync(ListeAttente listeAttente)
        {
            if (VerifierSiListeAttenteExisteMemeJourClinique(listeAttente.DateEffectivite, listeAttente.CliniqueID, listeAttente.ListeAttenteID))
            {
                throw new ValidationException("Il existe deja une liste d'attente dans la meme clinique pour la meme date.");
            }

            if (!VerifierSiDateEffectiviteValide(listeAttente) || !VerifierSiHeureOuvertureValide(listeAttente))
            {
                throw new ValidationException("La liste n'est pas valide");
            }
            else
            {
                listeAttente.IsOuverte = await ContientCreneauxDisponiblesAsync(listeAttente.ListeAttenteID);
                await EditerAsync(listeAttente);
                return listeAttente;
            }
        }

        public Task<bool> ContientCreneauxDisponiblesAsync(int listeAttenteID)
        {
            return _context.Consultations.AnyAsync(consultation =>
                consultation.PlageHoraire.ListeAttenteID == listeAttenteID &&
                consultation.StatutConsultation == StatutConsultation.DisponiblePourReservation);
        }

        /// <summary>
        /// Cette methode verifie que la liste dattente passee en parametre est pas ouverte
        /// </summary>
        /// <param name="listeAttente"></param>
        /// <returns></returns>
        public bool PeutSupprimmer(ListeAttente listeAttente)
        {
            if (listeAttente.IsOuverte == false)
                return true;
            else
                return false;

        }


        /// <summary>
        /// Cette methode supprimme une liste d'attente de la base de donnees
        /// et sauvegarde les changements
        /// </summary>
        /// <param name="listeAttente"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task SupprimmerListeAttente(ListeAttente listeAttente)
        {

            _context.ListeAttentes.Remove(listeAttente);
            await _context.SaveChangesAsync();

        }

        /// <summary>
        /// Cette methode Verifie s'il existe deja une liste d'attente dans la meme clinique pour la meme date.
        /// </summary>
        /// <param name="dateEffectivite"></param>
        /// <param name="cliniqueID"></param>
        /// <param name="listeAttenteID"></param>
        /// <returns></returns>
        /// <exception cref="ValidationException"></exception>
        public bool VerifierSiListeAttenteExisteMemeJourClinique(DateTime dateEffectivite, int cliniqueID, int? listeAttenteID = null)
        {
            var query = _context.ListeAttentes.Where(la => la.DateEffectivite == dateEffectivite && la.CliniqueID == cliniqueID);

            if (listeAttenteID.HasValue)
            {
                query = query.Where(la => la.ListeAttenteID != listeAttenteID.Value);
                return query.Any();
            }
            else
            {
                return query.Any()
             ? throw new ValidationException("Il existe deja une liste d'attente dans la meme clinique pour la meme date.")
             : false;
            }
        }
        /// <summary>
        /// Verifie le nombree de Medecins et l existance de la liste d'attente avent d'ajouter les plages horaires. 
        /// </summary>
        /// <param name="listeAttente"></param>
        /// <returns></returns>
        public bool ListeAttenteIsValid(ListeAttente listeAttente)
        {

            if (!VerifierSiListeAttenteEstCree(listeAttente) ||
            !VerifierSiNbMedecinsDisponibles(listeAttente))
            {
                return false;
            }


            return true;
        }



        public bool VerifierSiListeAttenteEstCree(ListeAttente listeAttente)
        {
            return _context.ListeAttentes.Any(l => l.DateEffectivite == listeAttente.DateEffectivite && l.CliniqueID == listeAttente.CliniqueID)
                        ? true
                        : throw new ValidationException("Il faut creer une liste d'attente avant");
        }

        public bool VerifierSiNbMedecinsDisponibles(ListeAttente listeAttente)
        {
            return listeAttente.NbMedecinsDispo >= 1
                    ? true
                    : throw new ValidationException("On doit avoir au moins un medecin disponible.");
        }

        public bool VerifierSiHeureOuvertureValide(ListeAttente listeAttente)
        {
            return listeAttente.HeureOuverture < listeAttente.HeureFermeture
                     ? true
                     : throw new ValidationException("L'heure d'ouverture doit etre inferieure a l'heure de fermeture.");

        }
        public bool VerifierSiDateEffectiviteValide(ListeAttente listeAttente)
        {

            return listeAttente.DateEffectivite >= DateTime.Now.Date
                   ? true
                   : throw new ValidationException("La date d'effectivite n'est pas valide. Elle doit etre posterieure a la date actuelle.");


        }

        public async Task GenererPlagesHorairesAsync(int ID)
        {
            var listeAttente = await _context.ListeAttentes.FindAsync(ID);
            if (!ListeAttenteIsValid(listeAttente))
            {
                throw new Exception("Liste d'attente invalide");
            }

            DateTime heureDebut = listeAttente.DateEffectivite.Date.Add(listeAttente.HeureOuverture);
            DateTime finService = listeAttente.DateEffectivite.Date.Add(listeAttente.HeureFermeture);
            PlageHoraire plageHoraire;

            while (heureDebut < finService)
            {
                Clinique clinique = await _context.Cliniques.FindAsync(listeAttente.CliniqueID);
                DateTime nouvelleHeureFin = heureDebut.AddMinutes((double)clinique.TempsMoyenConsultation);
                plageHoraire = new PlageHoraire
                {
                    HeureDebut = heureDebut,
                    HeureFin = nouvelleHeureFin,
                    ListeAttente = listeAttente

                };
                _context.PlagesHoraires.Add(plageHoraire);
                await _context.SaveChangesAsync();

                for (int i = 0; i < listeAttente.NbMedecinsDispo; i++)
                {
                    Consultation consultation = new Consultation
                    {
                        //HeureDateDebutPrevue = heureDebut,
                        //HeureDateFinPrevue = nouvelleHeureFin,
                        StatutConsultation = StatutConsultation.DisponiblePourReservation,
                        PlageHoraireID = plageHoraire.PlageHoraireID,
                        PatientID = null,
                        ///Salle=i++;

                    };
                    _context.Consultations.Add(consultation);

                }
                heureDebut = nouvelleHeureFin;
            }
            await _context.SaveChangesAsync();
            await RetirerPausesListeAttente(listeAttente);
            listeAttente.IsOuverte = await _context.Consultations.AnyAsync(consultation =>
                consultation.PlageHoraire.ListeAttenteID == ID &&
                consultation.StatutConsultation == StatutConsultation.DisponiblePourReservation);
            await _context.SaveChangesAsync();
        }


        public async Task<ListeAttenteVM> GetListeAttenteOrdonnee(int listeAttenteID)
        {
            var listeAttente = await _context.ListeAttentes
                                     .FirstOrDefaultAsync(la => la.ListeAttenteID == listeAttenteID);

            if (listeAttente == null)
            {
                throw new Exception("La liste d'attente n'existe pas");
            }


            var plagesHoraires = await _context.PlagesHoraires
                                       .Where(ph => ph.ListeAttenteID == listeAttenteID)
                                       .OrderBy(ph => ph.PlageHoraireID)
                                       //.SkipWhile(ph => ph.Consultations.All(c => c.StatutConsultation != StatutConsultation.DisponiblePourReservation))

                                       .ToListAsync();


            foreach (var plageHoraire in plagesHoraires)
            {
                plageHoraire.Consultations = await _context.Consultations
                                                           .Where(c => c.PlageHoraireID == plageHoraire.PlageHoraireID)
                                                           .OrderBy(c => c.ConsultationID)
                                                           .ToListAsync();
            }


            var listeAttenteVM = new ListeAttenteVM
            {
                ListeAttente = listeAttente,
                PlagesHoraires = plagesHoraires
            };

            return listeAttenteVM;
        }



        public async Task<ListeAttenteVM> GetListeSalleAttenteOrdonnee(int listeAttenteID)
        {
            var listeAttente = await _context.ListeAttentes
                                    .FirstOrDefaultAsync(la => la.ListeAttenteID == listeAttenteID);

            if (listeAttente == null)
            {
                throw new Exception("La liste d'attente n'existe pas");
            }


            var plagesHoraires = await _context.PlagesHoraires
                                       .Where(ph => ph.ListeAttenteID == listeAttenteID).ToListAsync();


            List<Consultation> consultations = await _context.Consultations
                                                            .Where(c => c.PlageHoraire.ListeAttenteID == listeAttenteID && c.StatutConsultation == StatutConsultation.EnAttente)
                                                            .OrderBy(c => c.PlageHoraire.HeureDebut)
                                                            .ThenBy(c => c.Patient.Prenom)
                                                            .ThenBy(c => c.Patient.Nom)
                                                            .ToListAsync();



            var listeSalleAttente = new ListeAttenteVM
            {
                ListeAttente = listeAttente,
                PlagesHoraires = plagesHoraires,
                Consultations = consultations

            };

            return listeSalleAttente;

        }


        /// <summary>
        /// Obtient la liste d'attente et la plage horaire associee a une consultation
        /// </summary>
        /// <param name="listeAttente"> La liste d'attente</param>
        /// <param name="cliniqueID"> L'ID de la clinique</param>
        public void AssignerCliniqueIDaListeAttente(ListeAttente listeAttente, int cliniqueID)
        {
            if (listeAttente != null && cliniqueID != null)
            {
                listeAttente.CliniqueID = cliniqueID;
            }
            else { }

            _context.SaveChanges();

        }

        public async Task<ListeAttente> ObtenirValeursParDefautCreationAsync(int cliniqueID)
        {
            var clinique = await _context.Cliniques
                .Include(candidate => candidate.EmployesCliniques)
                .FirstOrDefaultAsync(candidate => candidate.CliniqueID == cliniqueID);
            if (clinique == null)
            {
                throw new ValidationException("La clinique sélectionnée est introuvable.");
            }

            var derniereDate = await _context.ListeAttentes
                .Where(liste => liste.CliniqueID == cliniqueID)
                .Select(liste => (DateTime?)liste.DateEffectivite)
                .MaxAsync();
            var dateMinimale = DateTime.Today;
            var dateEffectivite = !derniereDate.HasValue || derniereDate.Value.Date < dateMinimale
                ? dateMinimale
                : derniereDate.Value.Date.AddDays(1);

            return new ListeAttente
            {
                DateEffectivite = dateEffectivite,
                HeureOuverture = clinique.HeureOuverture ?? TimeSpan.Zero,
                HeureFermeture = clinique.HeureFermeture ?? TimeSpan.Zero,
                NbMedecinsDispo = clinique.EmployesCliniques.Count(employee =>
                    employee.EmployeCliniquePosition == EmployeCliniquePosition.Medecin),
                CliniqueID = clinique.CliniqueID,
                HeurePauseDebut = clinique.HeurePauseDebut ?? TimeSpan.Zero,
                HeurePauseFin = clinique.HeurePauseFin ?? TimeSpan.Zero
            };
        }

        public async Task RetirerPausesListeAttente(ListeAttente listeAttente)
        {
            var plagesHoraires = await _context.PlagesHoraires.Where(ph => ph.ListeAttenteID == listeAttente.ListeAttenteID).ToListAsync();
            foreach (var plage in plagesHoraires)
            {
                if (plage.HeureDebut.TimeOfDay >= listeAttente.HeurePauseDebut && plage.HeureFin.TimeOfDay <= listeAttente.HeurePauseFin)
                {
                    _context.PlagesHoraires.Remove(plage);
                }
            }
            await _context.SaveChangesAsync();
        }

        public async Task<int> ObtenirIDListeAttente (int cliniqueID, DateTime dateEffectivite)
        {
            var listeAttente = await _context.ListeAttentes.FirstOrDefaultAsync(la => la.CliniqueID == cliniqueID && la.DateEffectivite == dateEffectivite);
            return listeAttente.ListeAttenteID;
        }
    }
}
