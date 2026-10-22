using Microsoft.EntityFrameworkCore;
using Store.Data;

namespace Store.Functions;

internal static class Retry
{
  /// <summary>
  /// Re-runs <paramref name="action"/> from a clean slate when another writer changed the same stock
  /// rows first (rowversion conflict). After the last attempt the exception propagates and Service Bus redelivers.
  /// </summary>
  public static async Task<T> OnConcurrencyAsync<T>(StoreDbContext db, Func<Task<T>> action, int attempts = 3)
  {
    for (var attempt = 1; ; attempt++)
    {
      try
      {
        return await action();
      }
      catch (DbUpdateConcurrencyException) when (attempt < attempts)
      {
        db.ChangeTracker.Clear();
      }
    }
  }
}
