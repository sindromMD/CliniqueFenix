using Clinique2000_Core.Models;
using Clinique2000_DataAccess.Data;
using Clinique2000_Services.IServices;
using Clinique2000_Services.Services;
using Clinique2000_Utility;
using Clinique2000_Utility.Enum;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Clinique2000_TestsUnitaires
{
    public class ConsultationDuJourTests
    {
        [Fact]
        public async Task ObtenirRendezVousDuJourAsync_ReturnsOnlyAppointmentsForClinicAndDate()
        {
            var options = new DbContextOptionsBuilder<CliniqueDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var context = new CliniqueDbContext(options);
            var date = new DateTime(2026, 11, 19);
            var clinique1 = CreateClinic(1);
            var clinique2 = CreateClinic(2);
            var liste1 = CreateListeAttente(1, clinique1, date);
            var liste2 = CreateListeAttente(2, clinique2, date);
            var liste3 = CreateListeAttente(3, clinique1, date.AddDays(1));
            var plage1 = CreatePlageHoraire(1, liste1, date.AddHours(9));
            var plage2 = CreatePlageHoraire(2, liste2, date.AddHours(10));
            var plage3 = CreatePlageHoraire(3, liste3, date.AddDays(1).AddHours(9));

            context.Cliniques.AddRange(clinique1, clinique2);
            context.ListeAttentes.AddRange(liste1, liste2, liste3);
            context.PlagesHoraires.AddRange(plage1, plage2, plage3);
            context.Consultations.AddRange(
                CreateAppointment(1, 21, plage1, date.AddHours(9)),
                CreateAppointment(2, 21, plage2, date.AddHours(10)),
                CreateAppointment(3, 21, plage3, date.AddDays(1).AddHours(9)));
            await context.SaveChangesAsync();

            var service = new ConsultationService(
                context,
                new Mock<IPatientService>().Object,
                new HttpContextAccessor(),
                new Mock<IListeAttenteService>().Object,
                new Mock<IEmailService>().Object,
                new Mock<ISMSService>().Object,
                new SystemClock(),
                Mock.Of<ICliniqueDashboardNotifier>());

            var result = await service.ObtenirRendezVousDuJourAsync(21, 1, date);

            Assert.NotNull(result);
            Assert.Equal(1, result.ConsultationID);
        }

        private static Clinique CreateClinic(int id)
        {
            return new Clinique
            {
                CliniqueID = id,
                NomClinique = $"Clinique {id}",
                Courriel = $"clinique{id}@example.com",
                HeureOuverture = TimeSpan.FromHours(8),
                HeureFermeture = TimeSpan.FromHours(17),
                TempsMoyenConsultation = 30,
                AdresseID = id,
                Adresse = new Adresse
                {
                    AdresseID = id,
                    CodePostal = "J4G 1C4",
                    Numero = "505",
                    Rue = "Rue de la Clinique",
                    Ville = "Longueuil",
                    Province = "Québec",
                    Pays = "Canada"
                },
                CreateurID = $"creator-{id}"
            };
        }

        private static ListeAttente CreateListeAttente(int id, Clinique clinique, DateTime date)
        {
            return new ListeAttente
            {
                ListeAttenteID = id,
                DateEffectivite = date,
                HeureOuverture = TimeSpan.FromHours(8),
                HeureFermeture = TimeSpan.FromHours(17),
                NbMedecinsDispo = 1,
                CliniqueID = clinique.CliniqueID,
                Clinique = clinique
            };
        }

        private static PlageHoraire CreatePlageHoraire(int id, ListeAttente liste, DateTime start)
        {
            return new PlageHoraire
            {
                PlageHoraireID = id,
                HeureDebut = start,
                HeureFin = start.AddMinutes(30),
                ListeAttenteID = liste.ListeAttenteID,
                ListeAttente = liste
            };
        }

        private static Consultation CreateAppointment(int id, int patientId, PlageHoraire plage, DateTime start)
        {
            return new Consultation
            {
                ConsultationID = id,
                PatientID = patientId,
                StatutConsultation = StatutConsultation.EnAttente,
                HeureDateDebutPrevue = start,
                HeureDateFinPrevue = start.AddMinutes(30),
                PlageHoraireID = plage.PlageHoraireID,
                PlageHoraire = plage
            };
        }
    }
}
