using Microsoft.EntityFrameworkCore;

namespace Test
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<Person> People { get; set; }
        public DbSet<Book> Books { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<BookPerson>()
                .HasKey(bookPerson => new { bookPerson.BookId, bookPerson.PersonId });
        }
    }
}
