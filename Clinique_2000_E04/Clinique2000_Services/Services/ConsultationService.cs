using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using Clinique2000_Core.Models;
using Clinique2000_Core.ViewModels;
using Clinique2000_DataAccess.Data;
using Clinique2000_Services.IServices;
using Clinique2000_Utility;
using Clinique2000_Utility.Enum;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Clinique2000_Services.Services
{
    public class ConsultationService : ServiceBaseAsync<Consultation>, IConsultationService
    {

        private readonly CliniqueDbContext _context;
        private readonly IPatientService _servicePatient;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IListeAttenteService _listeAttenteService;
        private readonly IEmailService _emailService;
        private readonly ISMSService _sMSService;
        private readonly IClock _clock;



        public ConsultationService(CliniqueDbContext context,
            IPatientService servicePatient,
            IHttpContextAccessor httpContextAccessor,
            IListeAttenteService listeAttenteService,
            IEmailService emailService,
            ISMSService sMSService,
            IClock clock) : base(context)
        {
            _context = context;
            _servicePatient = servicePatient;
            _httpContextAccessor = httpContextAccessor;
            _listeAttenteService = listeAttenteService;
            _emailService = emailService;
            _sMSService = sMSService;
            _clock = clock;

        }

        /// <summary>
        /// Réserve une consultation pour un patient.
        /// </summary>
        /// <param name="consultationId">L'identifiant de la consultation à réserver.</param>
        /// <exception cref="ValidationException">En cas d'échec des vérifications.</exception>
        public async Task ReserverConsultationAsync(int consultationId)
        {
            var consultation = await _context.Consultations
                .Include(c => c.PlageHoraire)
                    .ThenInclude(p => p.ListeAttente)
                        .ThenInclude(l => l.Clinique)
                            .ThenInclude(cl => cl.Adresse)
                .FirstOrDefaultAsync(c => c.ConsultationID == consultationId);

            if (consultation == null)
            {
                throw new ValidationException("Consultation introuvable.");
            }

            if (consultation.StatutConsultation != StatutConsultation.DisponiblePourReservation)
            {
                throw new ValidationException("Cette plage horaire n'est plus disponible.");
            }

            var patientId = await ObtenirIdPatientAsync();

            if (patientId <= 0)
            {
                throw new ValidationException("Vous devez être connecté avec un compte patient pour réserver.");
            }

            var listeAttente = consultation.PlageHoraire.ListeAttente;
            if (await ObtenirRendezVousDuJourAsync(
                    patientId,
                    listeAttente.CliniqueID,
                    consultation.PlageHoraire.HeureDebut.Date) != null)
            {
                throw new ValidationException("Le patient a déjà une consultation planifiée.");
            }

            if (await ListeAttenteOuverteAsync(consultationId) == false)
            {
                throw new ValidationException("La liste d'attente est fermée.");
            }

            consultation.PatientID = patientId;
            consultation.StatutConsultation = StatutConsultation.EnAttente;
            consultation.HeureDateDebutPrevue = consultation.PlageHoraire.HeureDebut;
            consultation.HeureDateFinPrevue = consultation.PlageHoraire.HeureFin;


            await FermerOuLaisserOuverteListeAttente(consultation.PlageHoraire.ListeAttenteID);
            var patient= await _servicePatient.ObtenirParIdAsync(patientId);
            //_sMSService.SendConfirmationSMS(consultation, patient.NumTelephone);
            await _context.SaveChangesAsync();

        }

        /// <summary>
        /// Verifie si la liste dattente doit etre fermee ou non en consequence du statut des consultations
        /// </summary>
        /// <param name="listeattenteid"></param>
        /// <returns>true si tout les consultations sont reserves
        /// false si ils le sont pas tous</returns>
        public async Task FermerOuLaisserOuverteListeAttente(int listeattenteid)
        {

            ListeAttente listeAttente = await _context.ListeAttentes.FindAsync(listeattenteid);
            var plagesHoraires = await _context.PlagesHoraires
                .Where(plage => plage.ListeAttenteID == listeattenteid)
                .Include(plage => plage.Consultations)
                .ToListAsync();

            listeAttente.IsOuverte = plagesHoraires
                .SelectMany(plage => plage.Consultations)
                .Any(consultation => consultation.StatutConsultation == StatutConsultation.DisponiblePourReservation);

        }



        /// <summary>
        /// Récupère de manière asynchrone la plage horaire et la liste d'attente associées à un identifiant de consultation donné.
        /// Lance une ValidationException si la consultation, la plage horaire ou la liste d'attente ne peuvent être trouvées.
        /// </summary>
        /// <param name="consultationId">L'identifiant de la consultation.</param>
        /// <returns>Un tuple contenant les objets PlageHoraire et ListeAttente associés.</returns>
        public async Task<(PlageHoraire, ListeAttente)> ObtenirPlageHoraireEtListeAttenteAsync(int consultationId)
        {
            if (consultationId == null)
            {
                throw new ValidationException("Consultation introuvable.");
            }

            Consultation consultation = await _context.Consultations.FindAsync(consultationId);

            var plageHoraire = await _context.PlagesHoraires.FindAsync(consultation.PlageHoraireID);
            if (plageHoraire == null)
            {
                throw new ValidationException("Plage horaire non trouvée.");
            }

            var listeAttente = await _context.ListeAttentes.FindAsync(plageHoraire.ListeAttenteID);
            if (listeAttente == null)
            {
                throw new ValidationException("Liste d'attente non trouvée.");
            }

            return (plageHoraire, listeAttente);
        }

        /// <summary>
        /// Vérifie si la liste d'attente est ouverte pour une consultation donnée.
        /// </summary>
        /// <param name="consultationId">L'identifiant de la consultation.</param>
        /// <returns>true si la liste d'attente est ouverte, sinon false.</returns>
        public async Task<bool> ListeAttenteOuverteAsync(int consultationId)
        {
            var tuple = await ObtenirPlageHoraireEtListeAttenteAsync(consultationId);
            ListeAttente listeAttente = tuple.Item2;

            // Vérifie si la liste d'attente est ouverte
            return listeAttente != null && listeAttente.IsOuverte;
        }

        public async Task<int> ObtenirIdPatientAsync()
        {
            var userId = ObtenirIdUtilisateur();
            return await ObtenirIdPatientDepuisUtilisateurAsync(userId);
        }

        public DateTime ObtenirMaintenantClinique(int utcOffset)
        {
            return CliniqueTimeZone.ConvertUtcToLocal(_clock.UtcNow, utcOffset);
        }

        public string ObtenirIdUtilisateur()
        {
            return _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        public async Task<int> ObtenirIdPatientDepuisUtilisateurAsync(string userId)
        {
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
            return patient?.PatientId ?? 0; // Retourne 0 ou un ID de patient valide
        }

        /// <summary>
        /// Vérifie si un patient a une consultation planifiée en attente.
        /// </summary>
        /// <param name="patientId">L'identifiant du patient.</param>
        /// <returns>true si le patient a une consultation planifiée en attente, sinon false.</returns>
        public async Task<bool> PatientAConsultationPlanifieeAsync(int patientId)
        {
            if (await _context.Consultations
                .AnyAsync(c => c.PatientID == patientId && c.StatutConsultation == StatutConsultation.EnAttente))
            {
                throw new ValidationException("Vous avez déjà une consultation planifiée.");
            }
            else
                return false;
        }

        public async Task<Consultation?> ObtenirRendezVousDuJourAsync(int patientId, int cliniqueId, DateTime date)
        {
            var dateDebut = date.Date;
            var dateFin = dateDebut.AddDays(1);

            return await _context.Consultations
                .Include(c => c.PlageHoraire)
                    .ThenInclude(p => p.ListeAttente)
                    .ThenInclude(l => l.Clinique)
                    .ThenInclude(cl => cl.Adresse)
                .Where(c =>
                    c.PatientID == patientId &&
                    (c.StatutConsultation == StatutConsultation.EnAttente ||
                     c.StatutConsultation == StatutConsultation.EnCours ||
                     c.StatutConsultation == StatutConsultation.EnRetard) &&
                    c.PlageHoraire.ListeAttente.CliniqueID == cliniqueId &&
                    c.HeureDateDebutPrevue >= dateDebut &&
                    c.HeureDateDebutPrevue < dateFin)
                .OrderBy(c => c.HeureDateDebutPrevue)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> PeutAnnulerAsync(Consultation consultation)
        {
            if (consultation.PatientID == null ||
                consultation.StatutConsultation != StatutConsultation.EnAttente)
            {
                return false;
            }

            var plageHoraire = consultation.PlageHoraire;
            var listeAttente = plageHoraire?.ListeAttente;
            var codePostal = listeAttente?.Clinique?.Adresse?.CodePostal;
            if (plageHoraire == null || listeAttente == null || string.IsNullOrWhiteSpace(codePostal))
            {
                throw new ValidationException("Les informations de la clinique du rendez-vous sont introuvables.");
            }

            var utcOffset = await ObtenirFuseauHoraireAsync(codePostal);
            var maintenantClinique = ObtenirMaintenantClinique(utcOffset);
            return maintenantClinique < plageHoraire.HeureDebut;
        }


        /// <summary>
        /// Obtient une consultation à partir de son identifiant.
        /// </summary>
        /// <param name="consultationId">L'identifiant de la consultation.</param>
        /// <returns>La consultation correspondante.</returns>
        public async Task<Consultation> ObtenirConsultationParIdAsync(int consultationId)
        {
            return await _context.Consultations
                .Include(c => c.PlageHoraire)
                    .ThenInclude(p => p.ListeAttente)
                        .ThenInclude(l => l.Clinique)
                            .ThenInclude(cl => cl.Adresse)
                .FirstOrDefaultAsync(c => c.ConsultationID == consultationId);
        }

        ///// <summary>
        ///// Obtient consultations avec les patients inclus
        ///// </summary>
        ///// <returns>Liste de consultation </returns>
        //public async Task<List<Consultation>> ObtenirListesConsultationIncludeUsersCourriel()
        //{
        //    var listeConsultations = await _context.Consultations.Include(x => x.Patient).ToListAsync();
        //    return listeConsultations;
        //}

        /// <summary>
        /// Appelle le prochain patient en attente pour une consultation.
        /// </summary>
        /// <param name="employeCliniqueID">Id de l'employé de la clinique qui appelle le prochain patient.</param> 
        /// <returns> La liste d'attente mise à jour. </returns>
        public async Task<ListeAttenteVM> AppelerProchainPatient(int employeCliniqueID)
        {
            var employeClinique = await _context.EmployesClinique.FirstAsync(e => e.EmployeCliniqueID == employeCliniqueID);
            var consultation = await _context.Consultations.Where(x => x.StatutConsultation == StatutConsultation.EnAttente).FirstOrDefaultAsync();
            //var consultation = await _context.Consultations.FirstAsync(c => c.ConsultationID == prochaineConsultation.ConsultationID);
            if (consultation != null)
            {
                consultation.EmployeCliniqueID = employeCliniqueID;
                consultation.StatutConsultation = StatutConsultation.EnCours;
                consultation.HeureDateDebutReele = DateTime.Now;
                await EditerAsync(consultation);
                ListeAttenteVM nouvelleListeAttenteVM = await _listeAttenteService.GetListeSalleAttenteOrdonnee(consultation.PlageHoraire.ListeAttenteID);
                return nouvelleListeAttenteVM;

            }

            return null;
        }

        /// <summary>
        /// Modifier le statut de la consultation pour la marquer comme terminée et enregistre les détails de la consultation.
        /// </summary>
        /// <param name="consultaionID"> Id de la consultation terminer.</param>
        /// <param name="details"> Les détails de la consultation. </param>
        /// <returns> La liste d'attente mise à jour.</returns>
        /// <exception cref="ValidationException"> Si la consultation n'a pas été trouvée ou si elle n'était pas en cours.</exception>
        public async Task<ListeAttenteVM> TerminerConsultation(int consultaionID, DetailsConsultation details)
        {
            // R�cup�re la consultation en fonction de l'ID pass� en param�tre.
            Consultation consultation = await ObtenirConsultationParIdAsync(consultaionID);

            // V�rifie si une consultation a �t� trouv�e.
            if (consultation != null)
            {
                // V�rifie si la consultation est en cours.
                if (consultation.StatutConsultation == StatutConsultation.EnCours)
                {
                    // Met � jour le statut de la consultation pour la marquer comme termin�e.
                    consultation.StatutConsultation = StatutConsultation.Termine;
                    //_emailService.ConsultationCompleted(consultation.Patient);
                    // Enregistre l'heure de fin r�elle de la consultation � l'heure actuelle.
                    consultation.HeureDateFinReele = DateTime.Now;

                    // Mettre à jour les détails de la consultation avec les données fournies.
                    consultation.DetailsConsultation = details;
                    // Enregistre les modifications dans la base de donn�es.
                    await EditerAsync(consultation);

                    // Obtient la liste d'attente mise � jour.
                    ListeAttenteVM nouvelleListeAttenteVM = await _listeAttenteService.GetListeSalleAttenteOrdonnee(consultation.PlageHoraire.ListeAttenteID);
                    return nouvelleListeAttenteVM;
                }
                else if (consultation.StatutConsultation != StatutConsultation.EnCours)
                {
                    throw new ValidationException("Une erreure est survenue.");
                }
            }

            // Si aucune consultation n'a �t� trouv�e ou si la consultation n'�tait pas en cours, retourne null.
            return null;
        }

        /// <summary>
        /// Anule la consultation d'un patient
        /// </summary>
        /// <param name="patient"> Le patient qui veut annuler sa consultation</param>
        /// <returns> rien</returns>
        /// <exception cref="ArgumentNullException"> Si le patient est null.</exception>
        public async Task AnnulerConsultationAsync(Patient patient)
        {
            if (patient == null)
            {
                throw new ArgumentNullException(nameof(patient));
            }

            var consultation = await _context.Consultations
                .Include(c => c.PlageHoraire)
                    .ThenInclude(plage => plage.ListeAttente)
                        .ThenInclude(liste => liste.Clinique)
                            .ThenInclude(clinique => clinique.Adresse)
                .FirstOrDefaultAsync(c =>
                    c.PatientID == patient.PatientId &&
                    c.StatutConsultation == StatutConsultation.EnAttente);
            if (consultation != null)
            {
                if (!await PeutAnnulerAsync(consultation))
                {
                    throw new ValidationException("L'annulation est possible uniquement avant l'heure du rendez-vous.");
                }

                consultation.StatutConsultation = StatutConsultation.DisponiblePourReservation;
                _emailService.ConsultationCompleted(consultation.Patient);
                consultation.PatientID = null;
                ListeAttente listeAttente = await _context.ListeAttentes.Where(la => la.ListeAttenteID == consultation.PlageHoraire.ListeAttenteID).FirstOrDefaultAsync();
                if (listeAttente != null)
                {
                    listeAttente.IsOuverte = true;

                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task AnnulerConsultationAsync(int consultationId, int patientId)
        {
            var consultation = await _context.Consultations
                .Include(c => c.PlageHoraire)
                    .ThenInclude(p => p.ListeAttente)
                    .ThenInclude(l => l.Clinique)
                    .ThenInclude(cl => cl.Adresse)
                .FirstOrDefaultAsync(c => c.ConsultationID == consultationId);

            if (consultation == null ||
                consultation.PatientID != patientId ||
                consultation.StatutConsultation != StatutConsultation.EnAttente)
            {
                throw new ValidationException("Ce rendez-vous ne peut pas être annulé.");
            }

            if (!await PeutAnnulerAsync(consultation))
            {
                throw new ValidationException("L'annulation est possible uniquement avant l'heure du rendez-vous.");
            }

            consultation.StatutConsultation = StatutConsultation.DisponiblePourReservation;
            consultation.PatientID = null;
            consultation.PlageHoraire.ListeAttente.IsOuverte = true;
            await _context.SaveChangesAsync();
        }

        public async Task<int> AnnulerRendezVousNonUtilisesAsync(DateTime maintenantUtc)
        {
            if (maintenantUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("La date doit être en UTC.", nameof(maintenantUtc));
            }

            var listesAttente = await _context.ListeAttentes
                .Include(liste => liste.Clinique)
                    .ThenInclude(clinique => clinique.Adresse)
                .Include(liste => liste.PlagesHoraires)
                    .ThenInclude(plage => plage.Consultations)
                .ToListAsync();

            if (listesAttente.Count == 0)
            {
                return 0;
            }

            var codesPostaux = listesAttente
                .Select(liste => liste.Clinique?.Adresse?.CodePostal)
                .Where(codePostal => !string.IsNullOrWhiteSpace(codePostal))
                .Distinct()
                .ToList();
            var fuseauxParCodePostal = await _context.AdressesQuebec
                .Where(adresse => codesPostaux.Contains(adresse.PostalCode))
                .ToDictionaryAsync(adresse => adresse.PostalCode, adresse => adresse.TimeZone);

            var maintenantParFuseau = new Dictionary<int, DateTime>();
            var nombreAnnulees = 0;

            foreach (var listeAttente in listesAttente)
            {
                var codePostal = listeAttente.Clinique?.Adresse?.CodePostal;
                if (string.IsNullOrWhiteSpace(codePostal) ||
                    !fuseauxParCodePostal.TryGetValue(codePostal, out var utcOffset))
                {
                    throw new ValidationException($"Le fuseau horaire de la clinique de la liste {listeAttente.ListeAttenteID} est introuvable.");
                }

                if (!maintenantParFuseau.TryGetValue(utcOffset, out var maintenantClinique))
                {
                    maintenantClinique = CliniqueTimeZone.ConvertUtcToLocal(maintenantUtc, utcOffset);
                    maintenantParFuseau.Add(utcOffset, maintenantClinique);
                }

                var fermetureClinique = listeAttente.DateEffectivite.Date.Add(listeAttente.HeureFermeture);
                if (maintenantClinique < fermetureClinique)
                {
                    continue;
                }

                foreach (var plage in listeAttente.PlagesHoraires ?? new List<PlageHoraire>())
                {
                    foreach (var consultation in plage.Consultations ?? new List<Consultation>())
                    {
                        if (!consultation.PatientID.HasValue ||
                            consultation.StatutConsultation == StatutConsultation.EnCours ||
                            consultation.StatutConsultation == StatutConsultation.Termine ||
                            consultation.StatutConsultation == StatutConsultation.Annulle)
                        {
                            continue;
                        }

                        consultation.StatutConsultation = StatutConsultation.Annulle;
                        nombreAnnulees++;
                    }
                }
            }

            if (nombreAnnulees > 0)
            {
                await _context.SaveChangesAsync();
            }

            return nombreAnnulees;
        }

        private async Task<int> ObtenirFuseauHoraireAsync(string codePostal)
        {
            var location = await _context.AdressesQuebec
                .FirstOrDefaultAsync(adresse => adresse.PostalCode == codePostal);
            if (location == null)
            {
                throw new ValidationException("Le fuseau horaire de la clinique est introuvable.");
            }

            return location.TimeZone;
        }

        public async Task DebutterConsultation(int consultationId)
        {
            var consultation = await _context.Consultations.FindAsync(consultationId);
            if (consultation != null)
            {
                consultation.StatutConsultation = StatutConsultation.EnCours;
                consultation.HeureDateDebutReele = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }
    }
}
