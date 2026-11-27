using Clinique2000_Utility;

namespace Clinique2000_TestsUnitaires
{
    public class CliniqueTimeZoneTests
    {
        [Fact]
        public void ConvertUtcToLocal_UsesDaylightSavingForEasternClinics()
        {
            var winterDate = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var summerDate = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

            Assert.Equal(new DateTime(2026, 1, 15, 7, 0, 0), CliniqueTimeZone.ConvertUtcToLocal(winterDate, 5));
            Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0), CliniqueTimeZone.ConvertUtcToLocal(summerDate, 5));
        }

        [Fact]
        public void ConvertUtcToLocal_RejectsUnsupportedOffset()
        {
            var utcDate = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            Assert.Throws<InvalidOperationException>(() => CliniqueTimeZone.ConvertUtcToLocal(utcDate, 3));
        }
    }
}
