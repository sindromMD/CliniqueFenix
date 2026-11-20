using Clinique2000_Services.IServices;
using Clinique2000_Utility;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Clinique2000_Services.Services
{
    [DisallowConcurrentExecution]
    public class AnnulerRendezVousNonUtilisesJob : IJob
    {
        private readonly IConsultationService _consultationService;
        private readonly IClock _clock;
        private readonly ILogger<AnnulerRendezVousNonUtilisesJob> _logger;

        public AnnulerRendezVousNonUtilisesJob(
            IConsultationService consultationService,
            IClock clock,
            ILogger<AnnulerRendezVousNonUtilisesJob> logger)
        {
            _consultationService = consultationService;
            _clock = clock;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var nombreAnnulees = await _consultationService.AnnulerRendezVousNonUtilisesAsync(_clock.UtcNow);
            _logger.LogInformation(
                "{NombreAnnulees} rendez-vous inutilisés ont été annulés automatiquement.",
                nombreAnnulees);
        }
    }
}
