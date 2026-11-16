using Tankstat.Domain;

namespace Tankstat.Application.Runs;

/// <summary>
/// The save waited for a run (#335) and the run is still open: nothing is wrong with the save, it is to be tried again later. Clients
/// treat it like a server they could not reach for that save (never as a rule's refusal: a device keeps the change), and the sync does
/// not park it.
/// </summary>
public sealed class BusyException() : KeyedException(BusyKey, "The server is busy with an import, try again in a moment.")
{
    public const string BusyKey = "import.busy";
}
