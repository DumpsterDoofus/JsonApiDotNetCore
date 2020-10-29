﻿using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;
using Microsoft.Extensions.Logging;

namespace Test
{
    public class PeopleController : JsonApiController<Person>
    {
        public PeopleController(
            IJsonApiOptions options,
            ILoggerFactory loggerFactory,
            IResourceService<Person> resourceService)
        : base(options, loggerFactory, resourceService)
        { }
    }
}
