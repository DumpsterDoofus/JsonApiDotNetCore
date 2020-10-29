using JsonApiDotNetCore;
using JsonApiDotNetCore.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;

namespace Test
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // Add the Entity Framework Core DbContext like you normally would
            services.AddDbContext<AppDbContext>(options =>
            {
                // Use whatever provider you want, this is just an example
                options.UseSqlServer("Data Source=.\\SQLEXPRESS;Initial Catalog=testDb;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False;ApplicationIntent=ReadWrite");
            });

            // Add JsonApiDotNetCore
            services.AddJsonApi<AppDbContext>();
        }

        public void Configure(IApplicationBuilder app, AppDbContext appDbContext)
        {
            appDbContext.Database.EnsureCreated();
            if (!appDbContext.People.Any())
            {
                var person1 = new Person
                {
                    Name = "John Doe"
                };
                var person2 = new Person
                {
                    Name = "Alan Thrall"
                };
                var book1 = new Book
                {
                    Title = "Explode"
                };
                var book2 = new Book
                {
                    Title = "Blastoise"
                };
                var bookPerson1 = new BookPerson
                {
                    Book = book1,
                    Person = person1
                };
                var bookPerson2 = new BookPerson
                {
                    Book = book2,
                    Person = person2
                };
                var bookPerson3 = new BookPerson
                {
                    Book = book1,
                    Person = person2
                };
                person1.BookPeople = new List<BookPerson> { bookPerson1 };
                person2.BookPeople = new List<BookPerson> { bookPerson2, bookPerson3 };
                appDbContext.People.Add(person1);
                appDbContext.People.Add(person2);
                appDbContext.Books.Add(book1);
                appDbContext.Books.Add(book2);
                appDbContext.SaveChanges();
            }
            app.UseJsonApi();
        }
    }
}
