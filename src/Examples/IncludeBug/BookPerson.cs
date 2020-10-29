using System.ComponentModel.DataAnnotations.Schema;

namespace Test
{
    public class BookPerson
    {
        public int BookId { get; set; }
        [ForeignKey("BookId")]
        public virtual Book Book { get; set; }

        public int PersonId { get; set; }
        [ForeignKey("PersonId")]
        public virtual Person Person { get; set; }
    }
}
