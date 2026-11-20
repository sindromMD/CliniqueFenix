using Clinique2000_Core.Models;
using Clinique2000_Core.ViewModels;
using Clinique2000_Services.IServices;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Clinique2000_Utility.Enum;
using Clinique2000_Utility.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Clinique2000_MVC.Hubs;
using Clinique2000_Utility;
using System.Security.Claims;

namespace Clinique2000_MVC.Areas.Cliniques.Controllers
{
    [Area("Cliniques")]
    public class ListeAttenteController : Controller
    {

        public IClinique2000Services _services { get; set; }
        private readonly IHubContext<MiseAJourListeAttentePatientHub> _hubContext;

        public ListeAttenteController(IClinique2000Services service,
            IHubContext<MiseAJourListeAttentePatientHub> hubContext)
        {
            _services = service;
            _hubContext = hubContext;
        }


        /// <summary>
        /// Obtient tout les listes d'Attente ordered par date d'effectivité ,
        /// qui sont pertinantes a la receptionniste
        /// , puis les renvoie a la vue.
        /// </summary>
        /// <returns></returns>
        public async Task<ActionResult> Index()
        {
            var user = await _services.patient.GetUserAuthAsync();
            var employee= await _services.employesClinique.GetEmployeUserID(user.Email, user.Id);

            //var employee = await _services.employesClinique.FindOneAsync(x => x.UserID == user.Id && x.EmployeCliniquePosition == EmployeCliniquePosition.Receptionniste);

            //AFFICHE LES LISTES DATTENTE DE LA CLINIQUE DANS Laquelle la receptionniste travaille
            if (employee!=null && employee.EmployeCliniquePosition == EmployeCliniquePosition.Receptionniste
                /* await _services.employesClinique.EmployeCliniqueEstReceptionniste(employee) == true*/)
            {
                List<ListeAttente> listListAttente = await _services.listeAttente.ObtenirToutAsync();

                listListAttente = listListAttente.Where(x => x.DateEffectivite >= DateTime.Now && x.CliniqueID== employee.CliniqueID)
                    .OrderBy(x => x.DateEffectivite)
                    .ToList();

                return View(listListAttente);

            }
            else
            {

                //affiche tout les listes dèattentes de la bd
                IList<ListeAttente> listListAttente = await _services.listeAttente.ObtenirToutAsync();
                listListAttente = listListAttente.Where(x => x.DateEffectivite >= DateTime.Now)
                .OrderBy(x => x.DateEffectivite)
                .ToList();
                if(User.IsInRole(AppConstants.SuperAdminRole))
                {
                    return View(listListAttente);
                }
                //Affiche les listes d'attentes de la clinique du createur
                if (User.IsInRole(AppConstants.AdminCliniqueRole))
                {
                    listListAttente = listListAttente.Where(l => l.Clinique.CreateurID == user.Id).ToList();  
                }
                return View(listListAttente);
            }
        }

        /// <summary>
        /// Retourne tout les Listes d'attente 
        /// </summary>
        /// <returns></returns>
        public async Task<ActionResult> HistoriqueListeAttentes()
        {

            IList<ListeAttente> listListAttente = await _services.listeAttente
        .ObtenirToutAsync();

            listListAttente = listListAttente.OrderByDescending(x => x.DateEffectivite).ToList();

            return View(listListAttente);
        }


        /// <summary>
        /// Obtient une ListeAttenteId en parametre, recupere la liste d'attente et la renvoie a la vue, 
        /// si le listeAttenteId recu en parametre est pas valide, retourne NotFound
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<ActionResult> Details(int? id)
        {
            if (id >= 0)
            {
                ListeAttente listeAttente = await _services.listeAttente.ObtenirParIdAsync(id);
                if (listeAttente == null)
                {
                    TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente avec l'identifiant {id} n'a été trouvée.";
                    return View("NotFound");
                }

                var plagesHoraires = listeAttente.PlagesHoraires ?? new List<PlageHoraire>();
                ListeAttenteVM listeAttenteVM = new ListeAttenteVM
                {
                    ListeAttente = listeAttente,
                    PlagesHoraires = listeAttente.PlagesHoraires.OrderBy(ph => ph.PlageHoraireID).ToList()
                };
                return View(listeAttenteVM);
            }
            TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente n'a été trouvée.";
            return View("NotFound");
        }



        /// <summary>
        /// Obtient une ListeAttenteId en parametre, recupere la liste d'attente et la renvoie a la vue,
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<ActionResult> DetailsOrdonnes(int id)
        {
            ListeAttenteVM listeAttenteVM = new ListeAttenteVM();
            try
            {
                if (id >= 0)
                {
                    listeAttenteVM = await _services.listeAttente.GetListeAttenteOrdonnee(id);
                    if (listeAttenteVM == null)
                    {
                        TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente avec l'identifiant {id} n'a été trouvée.";
                        return View("NotFound");
                    }

                    var listeAttente = listeAttenteVM.ListeAttente;
                    var clinique = listeAttente.Clinique;
                    var adresse = clinique.Adresse;
                    var fuseauHoraire = await _services.adresse.GetLocationByPostalCodeAsync(adresse.CodePostal);
                    if (fuseauHoraire == null)
                    {
                        throw new ValidationException("Le fuseau horaire de la clinique est introuvable.");
                    }

                    listeAttenteVM.MaintenantClinique =
                        _services.consultation.ObtenirMaintenantClinique(fuseauHoraire.TimeZone);

                    var authentifie = User.Identity?.IsAuthenticated == true;
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var courriel = User.FindFirstValue(ClaimTypes.Email);
                    var employeDeClinique = authentifie
                        ? await _services.employesClinique.FindOneAsync(employe =>
                            employe.CliniqueID == clinique.CliniqueID &&
                            ((userId != null && employe.UserID == userId) ||
                             (courriel != null && employe.EmployeCliniqueCourriel == courriel)) &&
                            (employe.EmployeCliniquePosition == EmployeCliniquePosition.Medecin ||
                             employe.EmployeCliniquePosition == EmployeCliniquePosition.AdminClinique ||
                             employe.EmployeCliniquePosition == EmployeCliniquePosition.Receptionniste))
                        : null;
                    var administrateurProprietaire =
                        User.IsInRole(AppConstants.AdminCliniqueRole) &&
                        clinique.CreateurID == userId;
                    var estPersonnelClinique =
                        employeDeClinique != null ||
                        administrateurProprietaire ||
                        User.IsInRole(AppConstants.SuperAdminRole);

                    listeAttenteVM.AfficherNomsPatients = estPersonnelClinique;
                    listeAttenteVM.EstPatientSeulement = !estPersonnelClinique;

                    if (listeAttenteVM.EstPatientSeulement)
                    {
                        if (authentifie && await _services.patient.UserAuthEstPatientAsync())
                        {
                            var patientId = await _services.consultation.ObtenirIdPatientAsync();
                            if (patientId > 0)
                            {
                                var rendezVous = await _services.consultation.ObtenirRendezVousDuJourAsync(
                                    patientId,
                                    clinique.CliniqueID,
                                    listeAttenteVM.MaintenantClinique.Date);
                                if (rendezVous != null)
                                {
                                    listeAttenteVM.PeutAnnulerMonRendezVous =
                                        await _services.consultation.PeutAnnulerAsync(rendezVous);
                                    listeAttenteVM.MonRendezVousAujourdhui = new Consultation
                                    {
                                        ConsultationID = rendezVous.ConsultationID,
                                        HeureDateDebutPrevue = rendezVous.HeureDateDebutPrevue,
                                        StatutConsultation = rendezVous.StatutConsultation,
                                        PlageHoraire = new PlageHoraire
                                        {
                                            HeureDebut = rendezVous.PlageHoraire.HeureDebut,
                                            HeureFin = rendezVous.PlageHoraire.HeureFin,
                                            ListeAttenteID = rendezVous.PlageHoraire.ListeAttenteID
                                        }
                                    };
                                }
                            }
                        }

                        if (listeAttenteVM.MonRendezVousAujourdhui == null)
                        {
                            var plagesHoraires = listeAttenteVM.PlagesHoraires ?? new List<PlageHoraire>();
                            listeAttenteVM.CreneauxDisponibles = plagesHoraires
                                .Select(plage =>
                                {
                                    var consultationsDisponibles = (plage.Consultations ?? new List<Consultation>())
                                        .Where(consultation =>
                                            consultation.StatutConsultation == StatutConsultation.DisponiblePourReservation)
                                        .ToList();

                                    return new CreneauDisponibleVM
                                    {
                                        HeureDebut = plage.HeureDebut,
                                        NombrePlacesDisponibles = consultationsDisponibles.Count,
                                        ConsultationDisponibleID = consultationsDisponibles
                                            .Select(consultation => consultation.ConsultationID)
                                            .FirstOrDefault()
                                    };
                                })
                                .ToList();
                        }

                        listeAttenteVM.PlagesHoraires = null;
                        listeAttenteVM.ListeAttente = CreerListeAttenteSansDonneesPrivees(listeAttente);
                        listeAttenteVM.Consultations = new List<Consultation>();
                    }

                    ViewData["AfficherNomsPatients"] = estPersonnelClinique;
                    return View("Details", listeAttenteVM);
                }
                TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente n'a été trouvée.";
                return View("NotFound");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("Erreur", ex.Message);
                TempData[AppConstants.Error] = $"Erreur : {ex.Message}";
                return View("NotFound");
            }
        }

        private static ListeAttente CreerListeAttenteSansDonneesPrivees(ListeAttente listeAttente)
        {
            var clinique = listeAttente.Clinique;
            var adresse = clinique.Adresse;

            return new ListeAttente
            {
                ListeAttenteID = listeAttente.ListeAttenteID,
                IsOuverte = listeAttente.IsOuverte,
                DateEffectivite = listeAttente.DateEffectivite,
                HeureOuverture = listeAttente.HeureOuverture,
                HeureFermeture = listeAttente.HeureFermeture,
                NbMedecinsDispo = listeAttente.NbMedecinsDispo,
                CliniqueID = listeAttente.CliniqueID,
                Clinique = new Clinique
                {
                    CliniqueID = clinique.CliniqueID,
                    NomClinique = clinique.NomClinique,
                    AdresseID = clinique.AdresseID,
                    Adresse = new Adresse
                    {
                        AdresseID = adresse.AdresseID,
                        Numero = adresse.Numero,
                        Rue = adresse.Rue,
                        Ville = adresse.Ville,
                        Province = adresse.Province,
                        Pays = adresse.Pays,
                        CodePostal = adresse.CodePostal
                    }
                },
                PlagesHoraires = null,
                Consultations = new List<Consultation>()
            };
        }


        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var user = await _services.patient.GetUserAuthAsync();
            var userid = await _services.employesClinique.GetEmployeUserID(user.Email, user.Id);
            var employee = await _services.employesClinique.FindOneAsync(x => x.UserID == user.Id && x.EmployeCliniquePosition == EmployeCliniquePosition.Receptionniste);
            if (employee != null)
            {
                ListeAttente listeAttente = await _services.listeAttente.GenererListeAttenteParDefaut(employee);

                return View(listeAttente);
            }

            return View();
        }

        // POST: ListeAttenteController/Create
        /// <summary>
        /// Action de creation de la liste d'attente.
        /// Si le formulaire est valide, on redirige l'utilisateur vers la page index,
        /// sinon, on lui retourne le formulaire comme il l'avait remplit avec l'erreur de validation
        /// </summary>
        /// <param name="listeAttente"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(ListeAttente listeAttente)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var user = await _services.patient.GetUserAuthAsync();
                    var userid = await _services.employesClinique.GetEmployeUserID(user.Email, user.Id);
                    var employee = await _services.employesClinique.FindOneAsync(x => x.UserID == user.Id && x.EmployeCliniquePosition == EmployeCliniquePosition.Receptionniste);

                    if (employee != null)
                    {
                        _services.listeAttente.AssignerCliniqueIDaListeAttente(listeAttente, employee.CliniqueID);
                        await _services.listeAttente.CreerListeAttenteAsync(listeAttente);
                        await _services.listeAttente.GenererPlagesHorairesAsync(listeAttente.ListeAttenteID);
                        TempData[AppConstants.Success] = "Vous avez créé avec succès la liste d'attente";
                        listeAttente = null;
                        RedirectToAction("Index");
                    }
                    else
                    {
                        TempData[AppConstants.Error] = "Vous n'êtes pas un employé clinique valide";
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                TempData[AppConstants.Error] = $"Erreur : {ex.Message}";
                return View("create", listeAttente);
            }
            return RedirectToAction("Index");
        }

        // GET: ListeAttenteController/Edit/5
        [HttpGet]
        public async Task<ActionResult> Edit(int id)
        {
            ListeAttente list = await _services.listeAttente.ObtenirParIdAsync(id);
            return View(list);
        }

        // POST: ListeAttenteController/Edit/5
        /// <summary>
        /// Action de modification de la liste d'attente.
        /// Si le formulaire est valide, la modification est effectuee et l'utilisateur est redirige vers la page index,
        /// dans le cas contraire, on lui envoie l'erreur de validation
        /// </summary>
        /// <param name="listeAttente"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(ListeAttente listeAttente)
        {

            try
            {
                if (ModelState.IsValid)
                {
                    //hardcodé
                    listeAttente.CliniqueID = 1;

                    await _services.listeAttente.ModifierListeAttenteAsync(listeAttente);
                    TempData[AppConstants.Success] = $"Les données de la liste d'attente ont été modifiées avec succès.";
                    return RedirectToAction("Index");
                }
                TempData[AppConstants.Error] = $"Les champs obligatoires n'ont pas été remplis correctement";
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("Erreur", ex.Message);
                TempData[AppConstants.Error] = $"Erreur {ex.Message}";
            }
            return View(listeAttente);
        }

        /// <summary>
        /// Recoit un ListeAttenteID en parametre et recupere la liste d'Attente puis l'envoie a la vue.
        /// Redirige l'utilisateur vers la vue Delete
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        // GET: ListeAttenteController/Delete/5
        [HttpGet]
        public async Task<ActionResult> Delete(int id)
        {
            if (id >= 0)
            {
                ListeAttente listeattente = _services.listeAttente.ObtenirParIdAsync(id).Result;
                TempData[AppConstants.Warning] = $"Vous êtes sûr de vouloir supprimer la liste d'attente ??";
                return View("Delete", listeattente);
            }
            TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente avec l'identifiant {id} n'a été trouvée.";
            return View("NotFound");

        }



        // POST: ListeAttenteController/Delete/5
        /// <summary>
        /// Verifie si la liste d'Attente peut etre supprimee, si elle peut etre supprimee,
        /// on la supprime, sinon, on retourne la meme vue avec l'erreur de validation.
        /// </summary>
        /// <param name="listeAttente"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Delete(ListeAttente listeAttente)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (!_services.listeAttente.PeutSupprimmer(listeAttente))
                    {
                        ModelState.AddModelError("", "Cette liste d'attente ne peut etre supprimee.");
                        TempData[AppConstants.Error] = $"Cette liste d'attente ne peut etre supprimee.";
                        return View(listeAttente);
                    }
                    else
                    {
                        await _services.listeAttente.SupprimmerListeAttente(listeAttente);
                        TempData[AppConstants.Success] = $"La liste d'attente de la clinique a été supprimée avec succès.";
                        return RedirectToAction("index");
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception details as needed
                    ModelState.AddModelError("", "Une erreur s'est produite lors de la suppression : " + ex.Message);
                    TempData[AppConstants.Error] = $"Erreur : {ex.Message}";
                    return View(listeAttente);
                }
            }

            return View(listeAttente);
        }




        /// <summary>
        /// Genere des plages horaires
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> AjouterDesPlagesHoraires(int ID)
        {

            await _services.listeAttente.GenererPlagesHorairesAsync(ID);

            var listeAttente = await _services.listeAttente.ObtenirParIdAsync(ID);
            ListeAttenteVM listeAttenteVM = new ListeAttenteVM
            {
                ListeAttente = listeAttente,
                PlagesHoraires = listeAttente.PlagesHoraires.OrderBy(ph => ph.PlageHoraireID).ToList()
            };

            TempData[AppConstants.Success] = $"Les plages horaires ont été générées avec succès";
            return View("Details", listeAttenteVM);
        }



        [HttpGet]
        public async Task<ActionResult> ShowReservationConfirmation(int id)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Challenge();
            }

            var consultation = await _services.consultation.ObtenirConsultationParIdAsync(id);
            if (consultation?.PlageHoraire == null)
            {
                TempData[AppConstants.Warning] = "Désolé, mais aucune consultation n'a été trouvée.";
                return View("NotFound");
            }

            var listeAttenteId = consultation.PlageHoraire.ListeAttenteID;
            try
            {
                var patientId = await _services.consultation.ObtenirIdPatientAsync();
                if (patientId <= 0)
                {
                    TempData[AppConstants.Warning] = "Vous devez être connecté avec un compte patient pour réserver.";
                    return RedirectToAction(nameof(DetailsOrdonnes), new { id = listeAttenteId });
                }

                if (await _services.consultation.ObtenirRendezVousDuJourAsync(
                        patientId,
                        consultation.PlageHoraire.ListeAttente.CliniqueID,
                        consultation.PlageHoraire.HeureDebut.Date) != null)
                {
                    TempData[AppConstants.Warning] = "Vous avez déjà un rendez-vous aujourd'hui dans cette clinique.";
                    return RedirectToAction(nameof(DetailsOrdonnes), new { id = listeAttenteId });
                }

                return View("ReservationSuccess", consultation);
            }
            catch (ValidationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                TempData[AppConstants.Error] = $"Erreur : {ex.Message}";
                return RedirectToAction(nameof(DetailsOrdonnes), new { id = listeAttenteId });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AnnulerMonRendezVous(int consultationId, int listeAttenteId)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Challenge();
            }

            if (!await _services.patient.UserAuthEstPatientAsync())
            {
                return Forbid();
            }

            var patientId = await _services.consultation.ObtenirIdPatientAsync();
            if (patientId <= 0)
            {
                return Forbid();
            }

            var consultation = await _services.consultation.ObtenirConsultationParIdAsync(consultationId);
            if (consultation?.PlageHoraire?.ListeAttente == null ||
                consultation.PatientID != patientId ||
                consultation.PlageHoraire.ListeAttenteID != listeAttenteId)
            {
                return Forbid();
            }

            try
            {
                await _services.consultation.AnnulerConsultationAsync(consultationId, patientId);
                await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
                TempData[AppConstants.Success] = "Votre rendez-vous a été annulé.";
            }
            catch (ValidationException ex)
            {
                TempData[AppConstants.Warning] = ex.Message;
            }

            return RedirectToAction(nameof(DetailsOrdonnes), new { id = listeAttenteId });
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ReserverConsultation(int id)
        {
            try
            {
                await _services.consultation.ReserverConsultationAsync(id);
                var consultation = await _services.consultation.ObtenirParIdAsync(id);
                if (consultation == null)
                {
                    TempData[AppConstants.Warning] = $"Désolé, mais aucune consultation avec l'identifiant {id} n'a été trouvée.";
                    return View("NotFound");
                }

                TempData[AppConstants.Success] = $"Vous avez réservé avec succès la consultation : {consultation.Patient.Nom} {consultation.Patient.Prenom} pour le {consultation.HeureDateDebutPrevue.ToShortDateString()} à {consultation.HeureDateDebutPrevue.ToShortTimeString()}";
              
                if (consultation.Patient.preferenceNotification != PreferenceNotification.SMS)
                     await _services.email.SendConsultationConfirmationEmail(consultation);
                else
                    _services.sms.SendConfirmationSMS(consultation,consultation.Patient.NumTelephone);



                await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
                return RedirectToAction("Index", "Home", new { area = "" });
            }
            catch (ValidationException ex)
            {
                ModelState.AddModelError("Erreur", ex.Message);
                TempData[AppConstants.Error] = $"Erreur : {ex.Message}";

                return RedirectToAction("Index", "Home", new { area = "" });
            }
            catch (Exception ex)
            {
                TempData[AppConstants.Error] = $"Erreur lors de l'envoi de l'email de confirmation : {ex.Message}";

                return RedirectToAction("Index", "Home", new { area = "" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AnnulerRendezVous(int consultationId)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Challenge();
            }
            if (!await _services.patient.UserAuthEstPatientAsync())
            {
                return Forbid();
            }

            var patientId = await _services.consultation.ObtenirIdPatientAsync();
            var consultation = await _services.consultation.ObtenirConsultationParIdAsync(consultationId);
            if (consultation?.PlageHoraire == null)
            {
                return NotFound();
            }

            var listeAttenteId = consultation.PlageHoraire.ListeAttenteID;
            try
            {
                await _services.consultation.AnnulerConsultationAsync(consultationId, patientId);
                await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
                TempData[AppConstants.Success] = "Votre rendez-vous a été annulé.";
            }
            catch (ValidationException ex)
            {
                TempData[AppConstants.Warning] = ex.Message;
            }

            return RedirectToAction(nameof(DetailsOrdonnes), new { id = listeAttenteId });
        }


        public async Task<ActionResult> TestReservationSuccess()
        {

            var clinique = new Clinique2000_Core.Models.Clinique
            {
                CliniqueID = 1, // ID ficticio
                NomClinique = "Santé pour tous",
                Courriel = "abc@dcv",
                //NumTelephone="514-123 4567",
                HeureOuverture = new TimeSpan(8, 0, 0),
                HeureFermeture = new TimeSpan(17, 0, 0),

                Adresse = new Adresse
                {
                    AdresseID = 1, // ID ficticio
                    Numero = "123",
                    Rue = "Rue de la Clinique",
                    Ville = "Montréal",
                    Province = "Québec",
                    CodePostal = "H1H 1H1"
                },

            };


            var listeAttente = new ListeAttente
            {
                ListeAttenteID = 1, // ID ficticio
                IsOuverte = true,
                DateEffectivite = new DateTime(2024, 1, 19),
                HeureOuverture = new TimeSpan(8, 0, 0), // Ejemplo: 8:00 AM
                HeureFermeture = new TimeSpan(17, 0, 0), // Ejemplo: 5:00 PM
                NbMedecinsDispo = 5,
                CliniqueID = clinique.CliniqueID,
                Clinique = clinique

            };


            var plageHoraire = new PlageHoraire
            {
                PlageHoraireID = 1,
                HeureDebut = new DateTime(2024, 1, 19, 9, 0, 0),
                HeureFin = new DateTime(2024, 1, 19, 9, 30, 0),
                ListeAttenteID = listeAttente.ListeAttenteID,
                ListeAttente = listeAttente
            };

            var consultation = new Consultation
            {
                ConsultationID = 1,
                PlageHoraireID = plageHoraire.PlageHoraireID,
                PlageHoraire = plageHoraire,
                PatientID = 1,
                Patient = new Patient
                {
                    Nom = "Emond",
                    Prenom = "Benoit"
                },



            };

            TempData[AppConstants.Success] = $"Vous avez réservé avec succès la consultation : {consultation.Patient.Nom} {consultation.Patient.Prenom} pour le {consultation.PlageHoraire.HeureDebut.ToShortDateString()} à {consultation.PlageHoraire.HeureDebut.ToShortTimeString()}";
            return View("ReservationSuccess", consultation);
        }

        public async Task<IActionResult> IndexlisteSalleAttente(int listeAttenteID)
        {
            ListeAttenteVM listeSalleAttenteVM = new ListeAttenteVM();
            if (listeAttenteID > 0)
            {
                listeSalleAttenteVM = await _services.listeAttente.GetListeSalleAttenteOrdonnee(listeAttenteID);
                if (listeSalleAttenteVM == null)
                {
                    TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente avec l'identifiant {listeAttenteID} n'a été trouvée.";
                    return View("NotFound");
                }

                return View(listeSalleAttenteVM);

            }
            else
            {
                TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente avec l'identifiant {listeAttenteID} n'a été trouvée.";
                return View("NotFound");
            }

        }

        //public async Task<IActionResult> DataSalleAttente(int listeAttenteID)
        //{
        //    Console.WriteLine("Se ajunge la DataSalleAttente");

        //    try
        //    {
        //        ListeAttenteVM listeSalleAttenteVM = await _services.listeAttente.GetListeSalleAttenteOrdonnee(listeAttenteID);

        //        if (listeSalleAttenteVM == null)
        //        {
        //            TempData[AppConstants.Warning] = $"Désolé, mais aucune liste d'attente avec l'identifiant {listeAttenteID} n'a été trouvée.";
        //            return View("NotFound");
        //        }

        //        if (HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        //        {
        //            // Este o solicitare Ajax
        //            return PartialView("IndeListeSalleAttente", listeSalleAttenteVM);
        //        }

        //        return View(listeSalleAttenteVM);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Eroare în DataSalleAttente: {ex.Message}");
        //        throw; // aruncă excepția pentru a o vedea în consola browser-ului
        //    }
        //}

        [HttpPost]
        public async Task<IActionResult> ChangerStatutConsultation(int consultationID, int employesID, DetailsConsultation details)
        {


            //var ListeSalleAttenteVM = await _services.listeAttente.TerminerConsultationEtAppellerProchainPatient(consultationID, employesID, details);
            var listeSalleAttenteVm = await _services.consultation.TerminerConsultation(consultationID, details);
            await _services.consultation.AppelerProchainPatient(employesID);
            await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");

            if (listeSalleAttenteVm == null)
            {
                TempData[AppConstants.Warning] = $"Désolé, mais aucune consultation avec l'identifiant {consultationID} n'a été trouvée.";
                return View("NotFound");
            }


            return RedirectToAction("Details", "EmployesCliniques", new { id = employesID });
        }


        [HttpPost]
        public async Task<IActionResult> AppelerProchainPatient(int employeCliniqueID)
        {
            try
            {
                // Call the service method to process calling the next patient.
                var updatedListeSalleAttente = await _services.consultation.AppelerProchainPatient(employeCliniqueID);


                if (updatedListeSalleAttente != null)
                {
                    // Redirect to the "ListeSalleAttente" view with the updated waiting room data.
                    TempData[AppConstants.Success] = $"Le prochain patient a été appelé.";

                    await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");

                    return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
                }
                else
                {
                    TempData[AppConstants.Info] = $"Il n'y a pas de patient en attente.";
                    return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });

                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("Erreur", ex.Message);
                TempData[AppConstants.Error] = $"Erreur : {ex.Message}";

                return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TerminerConsultationEtAppellerProchainPatient(int consultationId, int employeCliniqueID, EmployesCliniqueVM employes)
        {
            try
            {
                await _services.consultation.TerminerConsultation(consultationId, employes.DetailsConsultation);
                var updatedListeSalleAttente = await _services.consultation.AppelerProchainPatient(employeCliniqueID);
                await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
                if (updatedListeSalleAttente != null)
                {
                    TempData[AppConstants.Success] = $"Le prochain patient a été appelé.";

                    // Redirect to the "ListeSalleAttente" view with the updated waiting room data.
                    return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
                }
                else
                {
                    // Handle the case where no updated waiting room data is available.
                    TempData[AppConstants.Info] = $"Il n'y a pas de patient en attente.";
                    return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("Erreur", ex.Message);
                TempData[AppConstants.Error] = $"Erreur : {ex.Message}";

                return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TerminerConsultation(int consultationId, int employeCliniqueID, EmployesCliniqueVM employes)
        {
            try
            {
                // Call the service method to complete the current consultation.
                var updatedListeSalleAttente = await _services.consultation.TerminerConsultation(consultationId, employes.DetailsConsultation);
                //await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
                if (updatedListeSalleAttente != null)
                {
                    // Redirect to the "ListeSalleAttente" view with the updated waiting room data.
                    return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
                }
                else
                {
                    // Handle the case where no updated waiting room data is available.
                    TempData[AppConstants.Info] = $"Il n'y a pas de patient en attente.";
                    return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("Erreur", ex.Message);
                TempData[AppConstants.Error] = $"Erreur : {ex.Message}";

                return RedirectToAction("Details", "EmployesCliniques", new { id = employeCliniqueID });
            }
        }

        public async Task<IActionResult> AnnulerConsultation(int PatientId)
        {

            if (PatientId >= 0)
            {
                var patient = await _services.patient.ObtenirParIdAsync(PatientId);
                if (patient == null)
                {
                    TempData[AppConstants.Warning] = $"Désolé, mais aucune patient avec l'identifiant {PatientId} n'a été trouvée.";
                    return View("NotFound");
                }
                await _services.consultation.AnnulerConsultationAsync(patient);
                await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
                return RedirectToAction("Details", "Patients", new { area = "Patients", id = PatientId });
               
            }
            return View();
        }


    }
}
