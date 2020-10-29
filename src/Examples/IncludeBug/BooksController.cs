using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;
using Microsoft.Extensions.Logging;

namespace Test
{
    public class BooksController : JsonApiController<Book>
    {
        public BooksController(
            IJsonApiOptions options,
            ILoggerFactory loggerFactory,
            IResourceService<Book> resourceService)
        : base(options, loggerFactory, resourceService)
        { }
    }
}
