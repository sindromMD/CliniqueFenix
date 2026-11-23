using Clinique2000_Services.IServices;
using Microsoft.AspNetCore.SignalR;

namespace Clinique2000_MVC.Hubs
{
    public class SignalRCliniqueDashboardNotifier : ICliniqueDashboardNotifier
    {
        private readonly IHubContext<MiseAJourListeAttentePatientHub> _hubContext;

        public SignalRCliniqueDashboardNotifier(IHubContext<MiseAJourListeAttentePatientHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task NotifierChangementAsync(int cliniqueId)
        {
            await _hubContext.Clients
                .Group(MiseAJourListeAttentePatientHub.GroupeClinique(cliniqueId))
                .SendAsync("MiseAJourDashboardClinique");
            await _hubContext.Clients.All.SendAsync("MiseAJourListeSalleDAttentePatient");
        }
    }
}
