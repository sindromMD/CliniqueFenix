namespace Clinique2000_Services.IServices
{
    public interface ICliniqueDashboardNotifier
    {
        Task NotifierChangementAsync(int cliniqueId);
    }
}
