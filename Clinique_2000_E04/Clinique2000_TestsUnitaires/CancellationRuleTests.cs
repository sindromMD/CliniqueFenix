using Clinique2000_Core.Models;
using Clinique2000_DataAccess.Data;
using Clinique2000_Services.Services;
using Clinique2000_Utility;
using Clinique2000_Utility.Enum;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;

namespace Clinique2000_TestsUnitaires
{
    public class CancellationRuleTests
    {
        [Fact]
        public async Task PeutAnnulerAsync_ReturnsFalseAtAppointmentStartTime()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            var service = CreateService(context, new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc));

            var peutAnnuler = await service.PeutAnnulerAsync(consultation);

            Assert.False(peutAnnuler);
        }

        [Fact]
        public async Task AnnulerConsultationAsync_RejectsCancellationAtAppointmentStartTime()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            var service = CreateService(context, new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc));

            var exception = await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
                () => service.AnnulerConsultationAsync(consultation.ConsultationID, 42));

            Assert.Contains("avant l'heure du rendez-vous", exception.Message);
            Assert.Equal(StatutConsultation.EnAttente, consultation.StatutConsultation);
        }

        [Fact]
        public async Task AnnulerConsultationAsync_BeforeAppointmentStart_ReopensSlot()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            var service = CreateService(context, new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc));

            await service.AnnulerConsultationAsync(consultation.ConsultationID, 42);

            Assert.Equal(StatutConsultation.DisponiblePourReservation, consultation.StatutConsultation);
            Assert.Null(consultation.PatientID);
            Assert.True(consultation.PlageHoraire!.ListeAttente!.IsOuverte);
            Assert.True(consultation.PlageHoraire.ListeAttente.ReservationsActivees);
            Assert.Equal(1, consultation.PlageHoraire.ListeAttente.NbAnnulationsPatient);
        }

        [Fact]
        public async Task AnnulerConsultationAsync_DoesNotReenableManualAvailability()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            consultation.PlageHoraire!.ListeAttente!.ReservationsActivees = false;
            await context.SaveChangesAsync();
            var service = CreateService(context, new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc));

            await service.AnnulerConsultationAsync(consultation.ConsultationID, 42);

            Assert.True(consultation.PlageHoraire.ListeAttente.IsOuverte);
            Assert.False(consultation.PlageHoraire.ListeAttente.ReservationsActivees);
            Assert.Equal(1, consultation.PlageHoraire.ListeAttente.NbAnnulationsPatient);
        }

        [Fact]
        public async Task AnnulerConsultationAsync_PatientOverload_IncrementsPatientCancellationCount()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            var patient = await context.Patients.SingleAsync(candidate => candidate.PatientId == 42);
            var service = CreateService(context, new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc));

            await service.AnnulerConsultationAsync(patient);

            Assert.Equal(1, consultation.PlageHoraire!.ListeAttente!.NbAnnulationsPatient);
            Assert.Equal(StatutConsultation.DisponiblePourReservation, consultation.StatutConsultation);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task ReserverConsultationAsync_RejectsClosedListOrDisabledReservations(
            bool reservationsEnabled,
            bool hasOpenSlots)
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            consultation.PatientID = null;
            consultation.StatutConsultation = StatutConsultation.DisponiblePourReservation;
            consultation.PlageHoraire!.ListeAttente!.ReservationsActivees = reservationsEnabled;
            consultation.PlageHoraire.ListeAttente.IsOuverte = hasOpenSlots;
            await context.SaveChangesAsync();

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "patient-user")
                }, "Test"))
            };
            var service = CreateService(context, new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc), httpContext);

            var exception = await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
                () => service.ReserverConsultationAsync(consultation.ConsultationID));

            Assert.Equal("La liste d'attente est fermée.", exception.Message);
        }

        [Fact]
        public async Task ObtenirDashboardAsync_DoesNotCountAutomaticCancellationAsPatientCancellation()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            consultation.StatutConsultation = StatutConsultation.Annulle;
            await context.SaveChangesAsync();
            var service = new ListeAttenteService(
                context,
                Mock.Of<Clinique2000_Services.IServices.IEmailService>());
            var clinic = new Clinique2000_Core.ViewModels.ListeAttenteDashboardCliniqueVM
            {
                CliniqueID = 1,
                NomClinique = "Clinique",
                FuseauHoraire = 5
            };

            var item = Assert.Single(await service.ObtenirDashboardAsync(
                new[] { clinic },
                new DateTime(2026, 11, 20, 22, 0, 0, DateTimeKind.Utc)));

            Assert.Equal(0, item.RendezVousAnnulesParPatient);
            Assert.Equal(1, item.RendezVousNonPresentes);
        }

        [Fact]
        public async Task FermerOuLaisserOuverteListeAttente_LeavesListOpenWhenOtherSlotsAreAvailable()
        {
            await using var context = CreateContext();
            var consultation = await SeedAsync(context);
            var liste = consultation.PlageHoraire!.ListeAttente!;
            var plage = new PlageHoraire
            {
                PlageHoraireID = 2,
                HeureDebut = new DateTime(2026, 11, 20, 10, 30, 0),
                HeureFin = new DateTime(2026, 11, 20, 11, 0, 0),
                ListeAttenteID = liste.ListeAttenteID,
                ListeAttente = liste
            };
            context.PlagesHoraires.Add(plage);
            context.Consultations.Add(new Consultation
            {
                ConsultationID = 2,
                StatutConsultation = StatutConsultation.DisponiblePourReservation,
                PlageHoraireID = plage.PlageHoraireID,
                PlageHoraire = plage
            });
            await context.SaveChangesAsync();

            var service = CreateService(context, new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc));
            await service.FermerOuLaisserOuverteListeAttente(liste.ListeAttenteID);

            Assert.True(liste.IsOuverte);
        }

        private static CliniqueDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<CliniqueDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new CliniqueDbContext(options);
        }

        private static async Task<Consultation> SeedAsync(CliniqueDbContext context)
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
                DateEffectivite = new DateTime(2026, 11, 20),
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
                HeureDebut = new DateTime(2026, 11, 20, 10, 0, 0),
                HeureFin = new DateTime(2026, 11, 20, 10, 30, 0),
                ListeAttenteID = liste.ListeAttenteID,
                ListeAttente = liste
            };
            var consultation = new Consultation
            {
                ConsultationID = 1,
                PatientID = 42,
                StatutConsultation = StatutConsultation.EnAttente,
                HeureDateDebutPrevue = plage.HeureDebut,
                HeureDateFinPrevue = plage.HeureFin,
                PlageHoraireID = plage.PlageHoraireID,
                PlageHoraire = plage
            };
            plage.Consultations = new List<Consultation> { consultation };
            liste.PlagesHoraires = new List<PlageHoraire> { plage };

            context.ApplicationUser.Add(new ApplicationUser
            {
                Id = "patient-user",
                UserName = "patient@example.com",
                Email = "patient@example.com"
            });
            context.Patients.Add(new Patient
            {
                PatientId = 42,
                Nom = "Patient",
                Prenom = "Test",
                NAM = "ABCD 1234 5678",
                CodePostal = "H1A 1A1",
                DateDeNaissance = new DateTime(1990, 1, 1),
                Age = 36,
                Courriel = "patient@example.com",
                NumTelephone = "5145551234",
                UserId = "patient-user"
            });
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
            return consultation;
        }

        private static ConsultationService CreateService(
            CliniqueDbContext context,
            DateTime utcNow,
            HttpContext? httpContext = null)
        {
            return new ConsultationService(
                context,
                Mock.Of<Clinique2000_Services.IServices.IPatientService>(),
                new HttpContextAccessor { HttpContext = httpContext },
                Mock.Of<Clinique2000_Services.IServices.IListeAttenteService>(),
                Mock.Of<Clinique2000_Services.IServices.IEmailService>(),
                Mock.Of<Clinique2000_Services.IServices.ISMSService>(),
                new FixedClock(utcNow),
                Mock.Of<Clinique2000_Services.IServices.ICliniqueDashboardNotifier>());
        }

        private sealed class FixedClock : IClock
        {
            public FixedClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; }
        }
    }
}
