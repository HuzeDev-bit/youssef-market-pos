namespace MarketPos.Models;

/// <summary>
/// Where a sale came from, when it is not the machine writing it.
///
/// A sale saved on the back-office server happened somewhere else, minutes or hours earlier,
/// rung up by somebody who is not signed in here. Left to defaults the row would say the owner
/// sold it, just now, which is the wrong answer to two questions the shop will ask later.
///
/// <see cref="TillReference"/> is what makes handing a sale over safe to retry. A till cannot
/// know whether a request that timed out was written, so it sends the same one again; the
/// reference lets the server recognise it rather than taking the money twice.
/// </summary>
public sealed record SaleOrigin(
    DateTime SoldAt,
    int? WorkerId,
    string WorkerName,
    string TillReference);
