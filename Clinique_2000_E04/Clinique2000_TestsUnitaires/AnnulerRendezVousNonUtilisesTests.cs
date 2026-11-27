using Clinique2000_Core.Models;
using Clinique2000_DataAccess.Data;
using Clinique2000_Services.Services;
using Clinique2000_Utility;
using Clinique2000_Utility.Enum;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Clinique2000_TestsUnitaires
{
    public class AnnulerRendezVousNonUtilisesTests
    {
        [Fact]
        public async Task AnnulerRendezVousNonUtilisesAsync_BeforeClosing_LeavesAppointmentUnchanged()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context, new DateTime(2026, 11, 20));
            var service = CreateService(context);

            var count = await service.AnnulerRendezVousNonUtilisesAsync(
                new DateTime(2026, 11, 20, 21, 59, 0, DateTimeKind.Utc));

            Assert.Equal(0, count);
            Assert.Equal(StatutConsultation.EnAttente, consultation.StatutConsultation);
            Assert.True(consultation.PlageHoraire!.ListeAttente!.IsOuverte);
        }

        [Fact]
        public async Task AnnulerRendezVousNonUtilisesAsync_AfterClosing_CancelsBookedAppointmentAndIsIdempotent()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context, new DateTime(2026, 11, 20));
            var service = CreateService(context);
            var now = new DateTime(2026, 11, 20, 22, 0, 0, DateTimeKind.Utc);

            var firstCount = await service.AnnulerRendezVousNonUtilisesAsync(now);
            var secondCount = await service.AnnulerRendezVousNonUtilisesAsync(now);

            Assert.Equal(1, firstCount);
            Assert.Equal(0, secondCount);
            Assert.Equal(StatutConsultation.Annulle, consultation.StatutConsultation);
            Assert.Equal(42, consultation.PatientID);
            Assert.False(consultation.PlageHoraire!.ListeAttente!.IsOuverte);
        }

        [Fact]
        public async Task AnnulerRendezVousNonUtilisesAsync_AfterClosing_ClosesListWithoutCancellingUnbookedSlots()
        {
            await using var context = CreateContext();
            var consultation = CreateConsultation(1, StatutConsultation.DisponiblePourReservation, null);
            await SeedAsync(context, new DateTime(2026, 11, 20), consultation);
            var service = CreateService(context);

            var count = await service.AnnulerRendezVousNonUtilisesAsync(
                new DateTime(2026, 11, 20, 22, 0, 0, DateTimeKind.Utc));

            Assert.Equal(0, count);
            Assert.Equal(StatutConsultation.DisponiblePourReservation, consultation.StatutConsultation);
            Assert.False(consultation.PlageHoraire!.ListeAttente!.IsOuverte);
            var persistedListe = await context.ListeAttentes
                .AsNoTracking()
                .SingleAsync(liste => liste.ListeAttenteID == 1);
            Assert.False(persistedListe.IsOuverte);
        }

        [Fact]
        public async Task AnnulerRendezVousNonUtilisesAsync_PreservesInProgressAndCompletedAppointments()
        {
            await using var context = CreateContext();
            var waiting = CreateConsultation(1, StatutConsultation.EnAttente, 42);
            var inProgress = CreateConsultation(2, StatutConsultation.EnCours, 43);
            var completed = CreateConsultation(3, StatutConsultation.Termine, 44);
            await SeedAsync(context, new DateTime(2026, 11, 20), waiting, inProgress, completed);
            var service = CreateService(context);

            var count = await service.AnnulerRendezVousNonUtilisesAsync(
                new DateTime(2026, 11, 20, 22, 0, 0, DateTimeKind.Utc));

            Assert.Equal(1, count);
            Assert.Equal(StatutConsultation.Annulle, waiting.StatutConsultation);
            Assert.Equal(StatutConsultation.EnCours, inProgress.StatutConsultation);
            Assert.Equal(StatutConsultation.Termine, completed.StatutConsultation);
        }

        [Fact]
        public async Task AnnulerRendezVousNonUtilisesAsync_PastList_IsProcessed()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context, new DateTime(2026, 11, 19));
            var service = CreateService(context);

            var count = await service.AnnulerRendezVousNonUtilisesAsync(
                new DateTime(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc));

            Assert.Equal(1, count);
            Assert.Equal(StatutConsultation.Annulle, consultation.StatutConsultation);
        }

        [Fact]
        public async Task AnnulerRendezVousNonUtilisesAsync_LeavesUnbookedSlotsUnchanged()
        {
            await using var context = CreateContext();
            var consultation = CreateConsultation(1, StatutConsultation.DisponiblePourReservation, null);
            await SeedAsync(context, new DateTime(2026, 11, 20), consultation);
            var service = CreateService(context);

            var count = await service.AnnulerRendezVousNonUtilisesAsync(
                new DateTime(2026, 11, 20, 22, 0, 0, DateTimeKind.Utc));

            Assert.Equal(0, count);
            Assert.Equal(StatutConsultation.DisponiblePourReservation, consultation.StatutConsultation);
            Assert.Null(consultation.PatientID);
        }

        private static CliniqueDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<CliniqueDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new CliniqueDbContext(options);
        }

        private static async Task<Consultation> SeedAsync(
            CliniqueDbContext context,
            DateTime date,
            params Consultation[] consultations)
        {
            if (consultations.Length == 0)
            {
                consultations = new[] { CreateConsultation(1, StatutConsultation.EnAttente, 42) };
            }

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
                Courriel = "clinique@example.com",
                HeureOuverture = TimeSpan.FromHours(8),
                HeureFermeture = TimeSpan.FromHours(17),
                TempsMoyenConsultation = 30,
                AdresseID = adresse.AdresseID,
                Adresse = adresse,
                CreateurID = "creator"
            };
            var liste = new ListeAttente
            {
                ListeAttenteID = 1,
                DateEffectivite = date,
                HeureOuverture = TimeSpan.FromHours(8),
                HeureFermeture = TimeSpan.FromHours(17),
                NbMedecinsDispo = 1,
                IsOuverte = true,
                CliniqueID = clinique.CliniqueID,
                Clinique = clinique
            };
            var plage = new PlageHoraire
            {
                PlageHoraireID = 1,
                HeureDebut = date.AddHours(10),
                HeureFin = date.AddHours(10.5),
                ListeAttenteID = liste.ListeAttenteID,
                ListeAttente = liste,
                Consultations = consultations.ToList()
            };
            liste.PlagesHoraires = new List<PlageHoraire> { plage };

            foreach (var consultation in consultations)
            {
                consultation.PlageHoraireID = plage.PlageHoraireID;
                consultation.PlageHoraire = plage;
            }

            context.AdressesQuebec.Add(new AdressesQuebec
            {
                Id = 1,
                PostalCode = adresse.CodePostal,
                City = "Longueuil",
                ProvinceAbbr = "QC",
                TimeZone = 5
            });
            context.ListeAttentes.Add(liste);
            await context.SaveChangesAsync();

            return consultations[0];
        }

        private static Consultation CreateConsultation(int id, StatutConsultation status, int? patientId)
        {
            return new Consultation
            {
                ConsultationID = id,
                PatientID = patientId,
                StatutConsultation = status,
                HeureDateDebutPrevue = new DateTime(2026, 11, 20, 10, 0, 0),
                HeureDateFinPrevue = new DateTime(2026, 11, 20, 10, 30, 0)
            };
        }

        private static ConsultationService CreateService(CliniqueDbContext context)
        {
            return new ConsultationService(
                context,
                Mock.Of<Clinique2000_Services.IServices.IPatientService>(),
                new HttpContextAccessor(),
                Mock.Of<Clinique2000_Services.IServices.IListeAttenteService>(),
                Mock.Of<Clinique2000_Services.IServices.IEmailService>(),
                Mock.Of<Clinique2000_Services.IServices.ISMSService>(),
                new SystemClock(),
                Mock.Of<Clinique2000_Services.IServices.ICliniqueDashboardNotifier>());
        }
    }
}
