using GTA;

namespace MidnightClub.Core
{
    /// <summary>Non-blocking model loading for things that spawn while you drive: ask for the model and come back
    /// next tick, instead of freezing the script (and the HUD) for up to seconds while it streams in.</summary>
    public static class Assets
    {
        public static bool Ready(Model m)
        {
            if (!m.IsValid || !m.IsInCdImage) return false;
            if (m.IsLoaded) return true;
            m.Request();
            return false;
        }
    }
}
