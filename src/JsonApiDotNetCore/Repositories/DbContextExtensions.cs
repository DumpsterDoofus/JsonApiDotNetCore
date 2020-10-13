using System;
using System.Linq;
using System.Threading.Tasks;
using JsonApiDotNetCore.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JsonApiDotNetCore.Repositories
{
    public static class DbContextExtensions
    {
        internal static object GetTrackedIdentifiable(this DbContext context, IIdentifiable identifiable)
        {
            if (identifiable == null) throw new ArgumentNullException(nameof(identifiable));

            var entityType = identifiable.GetType();
            var entityEntry = context.ChangeTracker
                .Entries()
                .FirstOrDefault(entry =>
                    entry.Entity.GetType() == entityType &&
                    ((IIdentifiable) entry.Entity).StringId == identifiable.StringId);

            return entityEntry?.Entity;
        }

        /// <summary>
        /// Gets the current transaction or creates a new one.
        /// If a transaction already exists, commit, rollback and dispose
        /// will not be called. It is assumed the creator of the original
        /// transaction should be responsible for disposal.
        /// </summary>
        ///
        /// <example>
        /// <code>
        /// using(var transaction = _context.GetCurrentOrCreateTransaction())
        /// {
        ///     // perform multiple operations on the context and then save...
        ///     _context.SaveChanges();
        /// }
        /// </code>
        /// </example>
        public static async Task<IDbContextTransaction> GetCurrentOrCreateTransactionAsync(this DbContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            return await SafeTransactionProxy.GetOrCreateAsync(context.Database);
        }
    }
}