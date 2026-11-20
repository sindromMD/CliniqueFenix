namespace Clinique2000_Utility
{
    public static class CliniqueTimeZone
    {
        private static readonly IReadOnlyDictionary<int, string> TimeZoneIds = new Dictionary<int, string>
        {
            [4] = "Atlantic Standard Time",
            [5] = "Eastern Standard Time",
            [6] = "Central Standard Time",
            [7] = "Mountain Standard Time",
            [8] = "Pacific Standard Time",
            [9] = "Alaskan Standard Time",
            [10] = "Hawaiian Standard Time"
        };

        public static DateTime ConvertUtcToLocal(DateTime utcDateTime, int utcOffset)
        {
            if (utcDateTime.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("La date doit être en UTC.", nameof(utcDateTime));
            }

            if (!TimeZoneIds.TryGetValue(utcOffset, out var timeZoneId))
            {
                throw new InvalidOperationException($"Le fuseau horaire UTC-{utcOffset} n'est pas pris en charge.");
            }

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timeZone);
        }
    }
}
