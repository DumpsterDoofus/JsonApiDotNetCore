using JsonApiDotNetCore.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Test
{
    public class Book : Identifiable
    {
        [Attr]
        public string Title { get; set; }

        [NotMapped]
        [HasManyThrough(nameof(BookPeople))]
        public List<Person> People { get; set; }
        public virtual List<BookPerson> BookPeople { get; set; }
    }
}
