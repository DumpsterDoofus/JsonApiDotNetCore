using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using JsonApiDotNetCore.Serialization.Objects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JsonApiDotNetCoreExampleTests.IntegrationTests.Writing.Updating.Relationships
{
    public sealed class RemoveFromToManyRelationshipTests
        : IClassFixture<IntegrationTestContext<TestableStartup<WriteDbContext>, WriteDbContext>>
    {
        private readonly IntegrationTestContext<TestableStartup<WriteDbContext>, WriteDbContext> _testContext;
        private readonly WriteFakers _fakers = new WriteFakers();

        public RemoveFromToManyRelationshipTests(IntegrationTestContext<TestableStartup<WriteDbContext>, WriteDbContext> testContext)
        {
            _testContext = testContext;
        }

        [Fact]
        public async Task Cannot_remove_from_HasOne_relationship()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.AssignedTo = _fakers.UserAccount.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = existingWorkItem.AssignedTo.StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/assignedTo";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.Forbidden);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.Forbidden);
            responseDocument.Errors[0].Title.Should().Be("Only to-many relationships can be updated through this endpoint.");
            responseDocument.Errors[0].Detail.Should().Be("Relationship 'assignedTo' must be a to-many relationship.");
        }

        [Fact]
        public async Task Can_remove_from_HasMany_relationship()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.Subscribers = _fakers.UserAccount.Generate(2).ToHashSet();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                await dbContext.ClearTableAsync<UserAccount>();
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = existingWorkItem.Subscribers.ElementAt(0).StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var workItemInDatabase = await dbContext.WorkItems
                    .Include(workItem => workItem.Subscribers)
                    .FirstAsync(workItem => workItem.Id == existingWorkItem.Id);

                workItemInDatabase.Subscribers.Should().HaveCount(1);
                workItemInDatabase.Subscribers.Single().Id.Should().Be(existingWorkItem.Subscribers.ElementAt(1).Id);

                var userAccountsInDatabase = await dbContext.UserAccounts.ToListAsync();
                userAccountsInDatabase.Should().HaveCount(2);
            });
        }

        [Fact]
        public async Task Can_remove_from_HasManyThrough_relationship()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.WorkItemTags = new[]
            {
                new WorkItemTag
                {
                    Tag = _fakers.WorkTags.Generate()
                },
                new WorkItemTag
                {
                    Tag = _fakers.WorkTags.Generate()
                },
                new WorkItemTag
                {
                    Tag = _fakers.WorkTags.Generate()
                }
            };

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                await dbContext.ClearTableAsync<WorkTag>();
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "workTags",
                        id = existingWorkItem.WorkItemTags.ElementAt(0).Tag.StringId
                    },
                    new
                    {
                        type = "workTags",
                        id = existingWorkItem.WorkItemTags.ElementAt(1).Tag.StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/tags";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var workItemInDatabase = await dbContext.WorkItems
                    .Include(workItem => workItem.WorkItemTags)
                    .ThenInclude(workItemTag => workItemTag.Tag)
                    .FirstAsync(workItem => workItem.Id == existingWorkItem.Id);

                workItemInDatabase.WorkItemTags.Should().HaveCount(1);
                workItemInDatabase.WorkItemTags.Single().Tag.Id.Should().Be(existingWorkItem.WorkItemTags.ElementAt(2).Tag.Id);

                var tagsInDatabase = await dbContext.WorkTags.ToListAsync();
                tagsInDatabase.Should().HaveCount(3);
            });
        }

        [Fact]
        public async Task Can_remove_from_HasMany_relationship_with_unrelated_existing_resource()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.Subscribers = _fakers.UserAccount.Generate(2).ToHashSet();
            var existingSubscriber = _fakers.UserAccount.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                await dbContext.ClearTableAsync<UserAccount>();
                dbContext.AddRange(existingWorkItem, existingSubscriber);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = existingSubscriber.StringId
                    },
                    new
                    {
                        type = "userAccounts",
                        id = existingWorkItem.Subscribers.ElementAt(0).StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var workItemInDatabase = await dbContext.WorkItems
                    .Include(workItem => workItem.Subscribers)
                    .FirstAsync(workItem => workItem.Id == existingWorkItem.Id);

                workItemInDatabase.Subscribers.Should().HaveCount(1);
                workItemInDatabase.Subscribers.Single().Id.Should().Be(existingWorkItem.Subscribers.ElementAt(1).Id);

                var userAccountsInDatabase = await dbContext.UserAccounts.ToListAsync();
                userAccountsInDatabase.Should().HaveCount(3);
            });
        }

        [Fact]
        public async Task Can_remove_from_HasManyThrough_relationship_with_unrelated_existing_resource()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.WorkItemTags = new[]
            {
                new WorkItemTag
                {
                    Tag = _fakers.WorkTags.Generate()
                },
                new WorkItemTag
                {
                    Tag = _fakers.WorkTags.Generate()
                }
            };
            var existingTag = _fakers.WorkTags.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                await dbContext.ClearTableAsync<WorkTag>();
                dbContext.AddRange(existingWorkItem, existingTag);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "workTags",
                        id = existingWorkItem.WorkItemTags.ElementAt(1).Tag.StringId
                    },
                    new
                    {
                        type = "workTags",
                        id = existingTag.StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/tags";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var workItemInDatabase = await dbContext.WorkItems
                    .Include(workItem => workItem.WorkItemTags)
                    .ThenInclude(workItemTag => workItemTag.Tag)
                    .FirstAsync(workItem => workItem.Id == existingWorkItem.Id);

                workItemInDatabase.WorkItemTags.Should().HaveCount(1);
                workItemInDatabase.WorkItemTags.Single().Tag.Id.Should().Be(existingWorkItem.WorkItemTags.ElementAt(0).Tag.Id);

                var tagsInDatabase = await dbContext.WorkTags.ToListAsync();
                tagsInDatabase.Should().HaveCount(3);
            });
        }

        [Fact]
        public async Task Cannot_remove_for_missing_type()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.Subscribers = _fakers.UserAccount.Generate(1).ToHashSet();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        id = existingWorkItem.Subscribers.ElementAt(0).StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Request body must include 'type' element.");
            responseDocument.Errors[0].Detail.Should().StartWith("Expected 'type' element in 'data' element. - Request body: <<");
        }

        [Fact]
        public async Task Cannot_remove_for_unknown_type()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "doesNotExist",
                        id = 99999999
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Request body includes unknown resource type.");
            responseDocument.Errors[0].Detail.Should().StartWith("Resource of type 'doesNotExist' does not exist. - Request body: <<");
        }

        [Fact(Skip = "TODO: Fix bug that prevents this test from succeeding.")]
        public async Task Cannot_remove_for_missing_ID()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts"
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Request body must include 'id' element.");
            responseDocument.Errors[0].Detail.Should().StartWith("Expected 'id' element in 'data' element. - Request body: <<");
        }

        [Fact(Skip = "TODO: Fix bug that prevents this test from succeeding.")]
        public async Task Cannot_remove_unknown_IDs_from_HasMany_relationship()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = 88888888
                    },
                    new
                    {
                        type = "userAccounts",
                        id = 99999999
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

            responseDocument.Errors.Should().HaveCount(2);

            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[0].Title.Should().Be("A resource being removed from a relationship does not exist.");
            responseDocument.Errors[0].Detail.Should().Be("Resource of type 'userAccounts' with ID '88888888' being removed from relationship 'subscribers' does not exist.");

            responseDocument.Errors[1].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[1].Title.Should().Be("A resource being removed from a relationship does not exist.");
            responseDocument.Errors[1].Detail.Should().Be("Resource of type 'userAccounts' with ID '99999999' being removed from relationship 'subscribers' does not exist.");
        }

        [Fact(Skip = "TODO: Fix bug that prevents this test from succeeding.")]
        public async Task Cannot_remove_unknown_IDs_from_HasManyThrough_relationship()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "workTags",
                        id = 88888888
                    },
                    new
                    {
                        type = "workTags",
                        id = 99999999
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/tags";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

            responseDocument.Errors.Should().HaveCount(2);

            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[0].Title.Should().Be("A resource being removed from a relationship does not exist.");
            responseDocument.Errors[0].Detail.Should().Be("Resource of type 'workTags' with ID '88888888' being removed from relationship 'tags' does not exist.");

            responseDocument.Errors[1].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[1].Title.Should().Be("A resource being removed from a relationship does not exist.");
            responseDocument.Errors[1].Detail.Should().Be("Resource of type 'workTags' with ID '99999999' being removed from relationship 'tags' does not exist.");
        }

        [Fact]
        public async Task Cannot_remove_from_unknown_resource_type_in_url()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.Subscribers = _fakers.UserAccount.Generate(1).ToHashSet();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = existingWorkItem.Subscribers.ElementAt(0).StringId
                    }
                }
            };

            var route = $"/doesNotExist/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

            responseDocument.Should().BeEmpty();
        }

        [Fact(Skip = "TODO: Fix bug that prevents this test from succeeding.")]
        public async Task Cannot_remove_from_unknown_resource_ID_in_url()
        {
            // Arrange
            var existingSubscriber = _fakers.UserAccount.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.UserAccounts.Add(existingSubscriber);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = existingSubscriber.StringId
                    }
                }
            };

            var route = "/workItems/99999999/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[0].Title.Should().Be("The requested resource does not exist.");
            responseDocument.Errors[0].Detail.Should().Be("Resource of type 'workItems' with ID '99999999' does not exist.");
        }

        [Fact]
        public async Task Cannot_remove_from_unknown_relationship_in_url()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = 99999999
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/doesNotExist";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[0].Title.Should().Be("The requested relationship does not exist.");
            responseDocument.Errors[0].Detail.Should().Be("Resource of type 'workItems' does not contain a relationship named 'doesNotExist'.");
        }

        [Fact]
        public async Task Cannot_remove_for_relationship_mismatch_between_url_and_body()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.Subscribers = _fakers.UserAccount.Generate(1).ToHashSet();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = existingWorkItem.Subscribers.ElementAt(0).StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/tags";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.Conflict);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.Conflict);
            responseDocument.Errors[0].Title.Should().Be("Resource type mismatch between request body and endpoint URL.");
            responseDocument.Errors[0].Detail.Should().Be($"Expected resource of type 'workTags' in DELETE request body at endpoint '/workItems/{existingWorkItem.StringId}/relationships/tags', instead of 'userAccounts'.");
        }

        [Fact]
        public async Task Can_remove_with_duplicates()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.Subscribers = _fakers.UserAccount.Generate(2).ToHashSet();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new[]
                {
                    new
                    {
                        type = "userAccounts",
                        id = existingWorkItem.Subscribers.ElementAt(0).StringId
                    },
                    new
                    {
                        type = "userAccounts",
                        id = existingWorkItem.Subscribers.ElementAt(0).StringId
                    }
                }
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecuteDeleteAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var workItemInDatabase = await dbContext.WorkItems
                    .Include(workItem => workItem.Subscribers)
                    .FirstAsync(workItem => workItem.Id == existingWorkItem.Id);

                workItemInDatabase.Subscribers.Should().HaveCount(1);
                workItemInDatabase.Subscribers.Single().Id.Should().Be(existingWorkItem.Subscribers.ElementAt(1).Id);
            });
        }

        [Fact]
        public async Task Can_remove_with_empty_list()
        {
            // Arrange
            var existingWorkItem = _fakers.WorkItem.Generate();
            existingWorkItem.Subscribers = _fakers.UserAccount.Generate(1).ToHashSet();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.WorkItems.Add(existingWorkItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new object[0]
            };

            var route = $"/workItems/{existingWorkItem.StringId}/relationships/subscribers";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var workItemInDatabase = await dbContext.WorkItems
                    .Include(workItem => workItem.Subscribers)
                    .FirstAsync(workItem => workItem.Id == existingWorkItem.Id);

                workItemInDatabase.Subscribers.Should().HaveCount(1);
                workItemInDatabase.Subscribers.Single().Id.Should().Be(existingWorkItem.Subscribers.ElementAt(0).Id);
            });
        }
    }
}
