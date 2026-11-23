using Clinique2000_Core.Models;
using Clinique2000_Core.ViewModels;
using Clinique2000_DataAccess.Data;
using Clinique2000_Services.Services;
using Clinique2000_Utility.Enum;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Clinique2000_TestsUnitaires
{
    public class ListeAttenteDashboardServiceTests
    {
        [Fact]
        public async Task ObtenirCliniquesAutoriseesDashboardAsync_MatchesUserIdAndConfirmedEmailOnly()
        {
            await using var context = CreateContext();
            var user = new ApplicationUser { Id = "staff-1", Email = "staff@example.com" };
            var clinics = new[]
            {
                CreateClinic(1, "Clinique A", "owner-1"),
                CreateClinic(2, "Clinique B", "owner-2"),
                CreateClinic(3, "Clinique C", "owner-3")
            };
            context.ApplicationUser.Add(user);
            context.Cliniques.AddRange(clinics);
            context.EmployesClinique.AddRange(
                CreateEmployee(1, clinics[0], user.Id, "other@example.com"),
                CreateEmployee(2, clinics[1], "another-user", user.Email),
                CreateEmployee(3, clinics[2], "another-user", "another@example.com"));
            await context.SaveChangesAsync();
            var service = CreateService(context);

            var unconfirmed = await service.ObtenirCliniquesAutoriseesDashboardAsync(
                user.Id, user.Email, false, false, false);
            var confirmed = await service.ObtenirCliniquesAutoriseesDashboardAsync(
                user.Id, user.Email, true, false, false);

            Assert.Equal(new[] { 1 }, unconfirmed.Select(clinic => clinic.CliniqueID));
            Assert.Equal(new[] { 1, 2 }, confirmed.Select(clinic => clinic.CliniqueID));
        }

        [Fact]
        public async Task ObtenirDashboardAsync_UsesClinicLocalDateAndSeparatesPatientCancellationsFromNoShows()
        {
            await using var context = CreateContext();
            var clinic = CreateClinic(1, "Clinique A", "owner-1");
            context.Cliniques.Add(clinic);
            context.AdressesQuebec.Add(new AdressesQuebec
            {
                Id = 1,
                PostalCode = clinic.Adresse.CodePostal,
                City = "Montréal",
                ProvinceAbbr = "QC",
                TimeZone = 5
            });

            var today = CreateList(1, clinic, new DateTime(2026, 11, 23), 2);
            var tomorrow = CreateList(2, clinic, new DateTime(2026, 11, 24), 0);
            var past = CreateList(3, clinic, new DateTime(2026, 11, 22), 0);
            today.PlagesHoraires![0].Consultations = new List<Consultation>
            {
                CreateConsultation(1, StatutConsultation.EnAttente, 101),
                CreateConsultation(2, StatutConsultation.EnRetard, 102),
                CreateConsultation(3, StatutConsultation.EnCours, 103),
                CreateConsultation(4, StatutConsultation.Termine, 104),
                CreateConsultation(5, StatutConsultation.Annulle, 105),
                CreateConsultation(6, StatutConsultation.Annulle, null),
                CreateConsultation(7, StatutConsultation.DisponiblePourReservation, null)
            };
            today.NbAnnulationsPatient = 2;
            context.ListeAttentes.AddRange(today, tomorrow, past);
            await context.SaveChangesAsync();

            var service = CreateService(context);
            var clinics = new List<ListeAttenteDashboardCliniqueVM>
            {
                new() { CliniqueID = 1, NomClinique = "Clinique A", FuseauHoraire = 5 }
            };
            var result = await service.ObtenirDashboardAsync(
                clinics,
                new DateTime(2026, 11, 24, 4, 30, 0, DateTimeKind.Utc));

            Assert.Equal(new[] { 1, 2 }, result.Select(item => item.ListeAttenteID));
            var todayItem = result[0];
            Assert.Equal("Aujourd'hui", todayItem.Quand);
            Assert.Equal("Terminée", todayItem.EtatTemporel);
            Assert.Equal(2, todayItem.RendezVousAVenir);
            Assert.Equal(1, todayItem.RendezVousEnCours);
            Assert.Equal(1, todayItem.ConsultationsTerminees);
            Assert.Equal(2, todayItem.RendezVousAnnulesParPatient);
            Assert.Equal(1, todayItem.RendezVousNonPresentes);
            Assert.Equal(7, todayItem.TotalRendezVous);
            Assert.Equal("Demain", result[1].Quand);
            Assert.Equal("À venir", result[1].EtatTemporel);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ModifierListeAttenteAsync_SynchronizesOpenStateWithAvailableSlots(bool hasAvailableSlot)
        {
            await using var context = CreateContext();
            var clinic = CreateClinic(1, "Clinique A", "owner-1");
            var list = CreateList(1, clinic, DateTime.Today.AddDays(1), 0);
            list.IsOuverte = !hasAvailableSlot;
            var slot = list.PlagesHoraires![0];
            slot.Consultations = new List<Consultation>
            {
                CreateConsultation(
                    1,
                    hasAvailableSlot ? StatutConsultation.DisponiblePourReservation : StatutConsultation.EnAttente,
                    hasAvailableSlot ? null : 42)
            };
            context.Cliniques.Add(clinic);
            context.ListeAttentes.Add(list);
            await context.SaveChangesAsync();

            var service = CreateService(context);

            await service.ModifierListeAttenteAsync(list);

            Assert.Equal(hasAvailableSlot, list.IsOuverte);
        }

        private static CliniqueDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<CliniqueDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new CliniqueDbContext(options);
        }

        private static Clinique CreateClinic(int id, string name, string creatorId)
        {
            var address = new Adresse
            {
                AdresseID = id,
                Numero = "505",
                Rue = "Rue de la Clinique",
                Ville = "Montréal",
                Province = "Québec",
                Pays = "Canada",
                CodePostal = $"H{id}A 1A1"
            };
            return new Clinique
            {
                CliniqueID = id,
                NomClinique = name,
                Courriel = $"clinique{id}@example.com",
                HeureOuverture = TimeSpan.FromHours(8),
                HeureFermeture = TimeSpan.FromHours(17),
                TempsMoyenConsultation = 30,
                AdresseID = address.AdresseID,
                Adresse = address,
                CreateurID = creatorId
            };
        }

        private static EmployesClinique CreateEmployee(
            int id,
            Clinique clinic,
            string userId,
            string email)
        {
            return new EmployesClinique
            {
                EmployeCliniqueID = id,
                CliniqueID = clinic.CliniqueID,
                Clinique = clinic,
                UserID = userId,
                EmployeCliniqueCourriel = email,
                EmployeCliniquePrenom = "Sophie",
                EmployeCliniqueNom = "Gagnon",
                EmployeCliniquePosition = EmployeCliniquePosition.Receptionniste
            };
        }

        private static ListeAttente CreateList(int id, Clinique clinic, DateTime date, int patientCancellations)
        {
            var slot = new PlageHoraire
            {
                PlageHoraireID = id,
                HeureDebut = date.AddHours(9),
                HeureFin = date.AddHours(9.5),
                ListeAttenteID = id
            };
            var list = new ListeAttente
            {
                ListeAttenteID = id,
                CliniqueID = clinic.CliniqueID,
                Clinique = clinic,
                DateEffectivite = date,
                HeureOuverture = TimeSpan.FromHours(8),
                HeureFermeture = TimeSpan.FromHours(17),
                NbMedecinsDispo = 1,
                IsOuverte = true,
                ReservationsActivees = false,
                NbAnnulationsPatient = patientCancellations,
                PlagesHoraires = new List<PlageHoraire> { slot }
            };
            slot.ListeAttente = list;
            return list;
        }

        private static Consultation CreateConsultation(int id, StatutConsultation status, int? patientId)
        {
            return new Consultation
            {
                ConsultationID = id,
                PlageHoraireID = 1,
                StatutConsultation = status,
                PatientID = patientId
            };
        }

        private static ListeAttenteService CreateService(CliniqueDbContext context)
        {
            return new ListeAttenteService(context, Mock.Of<Clinique2000_Services.IServices.IEmailService>());
        }
    }
}
