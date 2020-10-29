using JsonApiDotNetCore.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Test
{
    public class Person : Identifiable
    {
        [Attr]
        public string Name { get; set; }

        [NotMapped]
        [HasManyThrough(nameof(BookPeople))]
        public List<Book> Books { get; set; }
        public virtual List<BookPerson> BookPeople { get; set; }
    }
}
