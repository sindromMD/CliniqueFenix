using Clinique2000_Core.Models;
using Clinique2000_Services.IServices;

using Microsoft.AspNetCore.SignalR;
using Clinique2000_Utility.Constants;

namespace Clinique2000_MVC.Hubs
{
    public class MiseAJourListeAttentePatientHub : Hub
    {
        private readonly IClinique2000Services _services;

        public MiseAJourListeAttentePatientHub(IClinique2000Services services)
        {
            _services = services;
        }

        public static string GroupeClinique(int cliniqueId) => $"clinique-{cliniqueId}";

        public async Task RejoindreCliniques()
        {
            var user = await _services.patient.GetUserAuthAsync();
            if (user == null)
            {
                throw new HubException("Vous devez être connecté pour rejoindre les mises à jour des cliniques.");
            }

            var cliniques = await _services.listeAttente.ObtenirCliniquesAutoriseesDashboardAsync(
                user.Id,
                user.Email,
                user.EmailConfirmed,
                Context.User?.IsInRole(AppConstants.AdminCliniqueRole) == true,
                Context.User?.IsInRole(AppConstants.SuperAdminRole) == true);

            foreach (var clinique in cliniques)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupeClinique(clinique.CliniqueID));
            }
        }

        /// <summary>
        /// Hub pour la mise à jour de la liste d'attente des patients
        /// </summary>
        /// <returns></returns>
        public async Task MiseAJourListeAttentePatient()
        {
            await Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
        }
    }
}
