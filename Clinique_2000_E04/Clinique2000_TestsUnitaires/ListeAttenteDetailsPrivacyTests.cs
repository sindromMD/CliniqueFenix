using System.Linq.Expressions;
using System.Security.Claims;
using Clinique2000_Core.Models;
using Clinique2000_Core.ViewModels;
using Clinique2000_MVC.Areas.Cliniques.Controllers;
using Clinique2000_MVC.Hubs;
using Clinique2000_Services.IServices;
using Clinique2000_Utility.Constants;
using Clinique2000_Utility.Enum;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Clinique2000_TestsUnitaires
{
    public class ListeAttenteDetailsPrivacyTests
    {
        [Fact]
        public async Task DetailsOrdonnes_PatientSeulement_ReturnsNameFreeModel()
        {
            var fixture = CreateFixture(patientOnly: true);

            var result = await fixture.Controller.DetailsOrdonnes(1);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ListeAttenteVM>(view.Model);
            Assert.True(model.EstPatientSeulement);
            Assert.False(model.AfficherNomsPatients);
            Assert.Null(model.PlagesHoraires);
            Assert.Empty(model.ListeAttente.Consultations);
            Assert.Null(model.ListeAttente.PlagesHoraires);
            Assert.Null(model.MonRendezVousAujourdhui?.Patient);
            Assert.Null(model.MonRendezVousAujourdhui?.EmployeClinique);
            Assert.Empty(model.CreneauxDisponibles);
        }

        [Fact]
        public async Task DetailsOrdonnes_PatientWithoutAppointment_ReturnsAvailableSlotSummariesOnly()
        {
            var fixture = CreateFixture(patientOnly: true, hasAppointment: false);

            var result = await fixture.Controller.DetailsOrdonnes(1);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ListeAttenteVM>(view.Model);
            Assert.Null(model.MonRendezVousAujourdhui);
            Assert.Single(model.CreneauxDisponibles);
            Assert.Equal(1, model.CreneauxDisponibles[0].NombrePlacesDisponibles);
            Assert.Null(model.PlagesHoraires);
        }

        [Fact]
        public async Task DetailsOrdonnes_PatientWithoutAvailableSlots_ReturnsFullSlotSummary()
        {
            var fixture = CreateFixture(
                patientOnly: true,
                hasAppointment: false,
                hasAvailableSlot: false);

            var result = await fixture.Controller.DetailsOrdonnes(1);

            var model = Assert.IsType<ListeAttenteVM>(Assert.IsType<ViewResult>(result).Model);
            Assert.Single(model.CreneauxDisponibles);
            Assert.Equal(0, model.CreneauxDisponibles[0].NombrePlacesDisponibles);
            Assert.Equal(0, model.CreneauxDisponibles[0].ConsultationDisponibleID);
        }

        [Fact]
        public async Task DetailsOrdonnes_AnonymousVisitor_ReturnsAvailableSlotSummaries()
        {
            var fixture = CreateFixture(patientOnly: false, hasAppointment: false, authenticated: false);

            var result = await fixture.Controller.DetailsOrdonnes(1);

            var model = Assert.IsType<ListeAttenteVM>(Assert.IsType<ViewResult>(result).Model);
            Assert.True(model.EstPatientSeulement);
            Assert.Single(model.CreneauxDisponibles);
            Assert.Null(model.PlagesHoraires);
        }

        [Fact]
        public async Task DetailsOrdonnes_ClinicEmployee_PreservesNamesAndFullRoster()
        {
            var fixture = CreateFixture(patientOnly: false);

            var result = await fixture.Controller.DetailsOrdonnes(1);

            var view = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<ListeAttenteVM>(view.Model);
            Assert.False(model.EstPatientSeulement);
            Assert.True(model.AfficherNomsPatients);
            Assert.Equal("Patient", model.PlagesHoraires![0].Consultations[0].Patient!.Prenom);
            Assert.Equal("Médecin", model.PlagesHoraires[0].Consultations[0].EmployeClinique!.EmployeCliniquePrenom);
        }

        private static Fixture CreateFixture(
            bool patientOnly,
            bool hasAppointment = true,
            bool authenticated = true,
            bool hasAvailableSlot = true)
        {
            var adresse = new Adresse
            {
                AdresseID = 1,
                Numero = "505",
                Rue = "Rue de la Clinique",
                Ville = "Longueuil",
                Province = "Québec",
                Pays = "Canada",
                CodePostal = "J4G 1C4"
            };
            var clinique = new Clinique
            {
                CliniqueID = 1,
                NomClinique = "Clinique",
                AdresseID = adresse.AdresseID,
                Adresse = adresse,
                CreateurID = "creator"
            };
            var consultation = new Consultation
            {
                ConsultationID = 5,
                PatientID = 22,
                StatutConsultation = StatutConsultation.EnAttente,
                Patient = new Patient { PatientId = 22, Prenom = "Patient", Nom = "Privé" },
                EmployeClinique = new EmployesClinique
                {
                    EmployeCliniquePrenom = "Médecin",
                    EmployeCliniqueNom = "Privé"
                }
            };
            var consultationDisponible = new Consultation
            {
                ConsultationID = 6,
                PatientID = hasAvailableSlot ? null : 23,
                StatutConsultation = hasAvailableSlot
                    ? StatutConsultation.DisponiblePourReservation
                    : StatutConsultation.EnAttente
            };
            var plage = new PlageHoraire
            {
                PlageHoraireID = 2,
                HeureDebut = new DateTime(2026, 11, 20, 10, 0, 0),
                HeureFin = new DateTime(2026, 11, 20, 10, 30, 0),
                Consultations = new List<Consultation> { consultation, consultationDisponible }
            };
            var liste = new ListeAttente
            {
                ListeAttenteID = 1,
                DateEffectivite = new DateTime(2026, 11, 20),
                HeureOuverture = TimeSpan.FromHours(8),
                HeureFermeture = TimeSpan.FromHours(17),
                NbMedecinsDispo = 1,
                CliniqueID = clinique.CliniqueID,
                Clinique = clinique,
                PlagesHoraires = new List<PlageHoraire> { plage },
                Consultations = new List<Consultation> { consultation, consultationDisponible }
            };
            plage.ListeAttenteID = liste.ListeAttenteID;
            plage.ListeAttente = liste;
            consultation.PlageHoraire = plage;
            consultationDisponible.PlageHoraire = plage;

            var listeAttenteService = new Mock<IListeAttenteService>();
            listeAttenteService.Setup(service => service.GetListeAttenteOrdonnee(1))
                .ReturnsAsync(new ListeAttenteVM { ListeAttente = liste, PlagesHoraires = liste.PlagesHoraires });
            var patientService = new Mock<IPatientService>();
            patientService.Setup(service => service.UserAuthEstPatientAsync()).ReturnsAsync(authenticated);
            var employeService = new Mock<IEmployesCliniqueService>();
            employeService.Setup(service => service.FindOneAsync(It.IsAny<Expression<Func<EmployesClinique, bool>>>()))
                .ReturnsAsync(patientOnly ? null : new EmployesClinique
                {
                    CliniqueID = clinique.CliniqueID,
                    EmployeCliniquePosition = EmployeCliniquePosition.Medecin
                });
            var adresseService = new Mock<IAdresseService>();
            adresseService.Setup(service => service.GetLocationByPostalCodeAsync(adresse.CodePostal))
                .ReturnsAsync(new AdressesQuebec { PostalCode = adresse.CodePostal, TimeZone = 5 });
            var consultationService = new Mock<IConsultationService>();
            consultationService.Setup(service => service.ObtenirMaintenantClinique(5))
                .Returns(new DateTime(2026, 11, 20, 9, 0, 0));
            consultationService.Setup(service => service.ObtenirIdPatientAsync()).ReturnsAsync(22);
            consultationService.Setup(service => service.ObtenirRendezVousDuJourAsync(
                    22,
                    clinique.CliniqueID,
                    new DateTime(2026, 11, 20)))
                .ReturnsAsync(hasAppointment ? consultation : null);
            consultationService.Setup(service => service.PeutAnnulerAsync(consultation)).ReturnsAsync(true);

            var services = new Mock<IClinique2000Services>();
            services.SetupGet(service => service.listeAttente).Returns(listeAttenteService.Object);
            services.SetupGet(service => service.patient).Returns(patientService.Object);
            services.SetupGet(service => service.employesClinique).Returns(employeService.Object);
            services.SetupGet(service => service.adresse).Returns(adresseService.Object);
            services.SetupGet(service => service.consultation).Returns(consultationService.Object);

            var hubClients = new Mock<IHubClients>();
            hubClients.Setup(clients => clients.All).Returns(Mock.Of<IClientProxy>());
            var hubContext = new Mock<IHubContext<MiseAJourListeAttentePatientHub>>();
            hubContext.Setup(context => context.Clients).Returns(hubClients.Object);

            var controller = new ListeAttenteController(services.Object, hubContext.Object);
            var identity = authenticated
                ? new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "user-1"),
                        new Claim(ClaimTypes.Email, "patient@example.com"),
                        new Claim(ClaimTypes.Role, AppConstants.PatientRole)
                    },
                    authenticationType: "test")
                : new ClaimsIdentity();
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };

            return new Fixture(controller);
        }

        private sealed record Fixture(ListeAttenteController Controller);
    }
}
