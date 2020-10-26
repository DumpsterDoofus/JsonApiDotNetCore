using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bogus;
using FluentAssertions;
using JsonApiDotNetCore.Serialization.Objects;
using JsonApiDotNetCoreExample;
using JsonApiDotNetCoreExample.Data;
using JsonApiDotNetCoreExample.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Person = JsonApiDotNetCoreExample.Models.Person;

namespace JsonApiDotNetCoreExampleTests.Acceptance.Spec
{
    public sealed class UpdatingRelationshipsTests : IClassFixture<IntegrationTestContext<Startup, AppDbContext>>
    {
        private readonly IntegrationTestContext<Startup, AppDbContext> _testContext;

        private readonly Faker<TodoItem> _todoItemFaker = new Faker<TodoItem>()
            .RuleFor(t => t.Description, f => f.Lorem.Sentence())
            .RuleFor(t => t.Ordinal, f => f.Random.Number())
            .RuleFor(t => t.CreatedDate, f => f.Date.Past());

        private readonly Faker<Person> _personFaker = new Faker<Person>()
            .RuleFor(p => p.FirstName, f => f.Name.FirstName())
            .RuleFor(p => p.LastName, f => f.Name.LastName());

        public UpdatingRelationshipsTests(IntegrationTestContext<Startup, AppDbContext> testContext)
        {
            _testContext = testContext;
        }

        [Fact]
        public async Task Can_Update_Cyclic_ToMany_Relationship_By_Patching_Resource()
        {
            // Arrange
            var todoItem = _todoItemFaker.Generate();
            var otherTodoItem = _todoItemFaker.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.TodoItems.AddRange(todoItem, otherTodoItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    type = "todoItems",
                    id = todoItem.StringId,
                    relationships = new Dictionary<string, object>
                    {
                        ["childrenTodos"] = new
                        {
                            data = new[]
                            {
                                new
                                {
                                    type = "todoItems",
                                    id = todoItem.StringId
                                },
                                new
                                {
                                    type = "todoItems",
                                    id = otherTodoItem.StringId
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/todoItems/" + todoItem.StringId;

            // Act
            var (httpResponse, _) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var todoItemInDatabase = await dbContext.TodoItems
                    .Include(item => item.ChildrenTodos)
                    .FirstAsync(item => item.Id == todoItem.Id);

                todoItemInDatabase.ChildrenTodos.Should().HaveCount(2);
                todoItemInDatabase.ChildrenTodos.Should().ContainSingle(x => x.Id == todoItem.Id);
                todoItemInDatabase.ChildrenTodos.Should().ContainSingle(x => x.Id == otherTodoItem.Id);
            });
        }

        [Fact]
        public async Task Can_Update_Cyclic_ToOne_Relationship_By_Patching_Resource()
        {
            // Arrange
            var todoItem = _todoItemFaker.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.TodoItems.Add(todoItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    type = "todoItems",
                    id = todoItem.StringId,
                    relationships = new Dictionary<string, object>
                    {
                        ["dependentOnTodo"] = new
                        {
                            data = new
                            {
                                type = "todoItems",
                                id = todoItem.StringId
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/todoItems/" + todoItem.StringId;

            // Act
            var (httpResponse, _) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var todoItemInDatabase = await dbContext.TodoItems
                    .Include(item => item.DependentOnTodo)
                    .FirstAsync(item => item.Id == todoItem.Id);

                todoItemInDatabase.DependentOnTodoId.Should().Be(todoItem.Id);
            });
        }

        [Fact]
        public async Task Can_Update_Both_Cyclic_ToOne_And_ToMany_Relationship_By_Patching_Resource()
        {
            // Arrange
            var todoItem = _todoItemFaker.Generate();
            var otherTodoItem = _todoItemFaker.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.TodoItems.AddRange(todoItem, otherTodoItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    type = "todoItems",
                    id = todoItem.StringId,
                    relationships = new Dictionary<string, object>
                    {
                        ["dependentOnTodo"] = new
                        {
                            data = new
                            {
                                type = "todoItems",
                                id = todoItem.StringId
                            }
                        },
                        ["childrenTodos"] = new
                        {
                            data = new[]
                            {
                                new
                                {
                                    type = "todoItems",
                                    id = todoItem.StringId
                                },
                                new
                                {
                                    type = "todoItems",
                                    id = otherTodoItem.StringId
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/todoItems/" + todoItem.StringId;

            // Act
            var (httpResponse, _) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var todoItemInDatabase = await dbContext.TodoItems
                    .Include(item => item.ParentTodo)
                    .FirstAsync(item => item.Id == todoItem.Id);

                todoItemInDatabase.ParentTodoId.Should().Be(todoItem.Id);
            });
        }

        [Fact]
        public async Task Can_Update_ToMany_Relationship_By_Patching_Resource()
        {
            // Arrange
            var person1 = _personFaker.Generate();
            person1.TodoItems = _todoItemFaker.Generate(3).ToHashSet();

            var person2 = _personFaker.Generate();
            person2.TodoItems = _todoItemFaker.Generate(2).ToHashSet();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.People.AddRange(person1, person2);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    type = "people",
                    id = person2.StringId,
                    relationships = new Dictionary<string, object>
                    {
                        ["todoItems"] = new
                        {
                            data = new[]
                            {
                                new
                                {
                                    type = "todoItems",
                                    id = person1.TodoItems.ElementAt(0).StringId
                                },
                                new
                                {
                                    type = "todoItems",
                                    id = person1.TodoItems.ElementAt(1).StringId
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/people/" + person2.StringId;

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePatchAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var personsInDatabase = await dbContext.People
                    .Include(person => person.TodoItems)
                    .ToListAsync();

                personsInDatabase.Single(person => person.Id == person1.Id).TodoItems.Should().HaveCount(1);

                var person2InDatabase = personsInDatabase.Single(person => person.Id == person2.Id);
                person2InDatabase.TodoItems.Should().HaveCount(2);
                person2InDatabase.TodoItems.Should().ContainSingle(x => x.Id == person1.TodoItems.ElementAt(0).Id);
                person2InDatabase.TodoItems.Should().ContainSingle(x => x.Id == person1.TodoItems.ElementAt(1).Id);
            });
        }

        [Fact]
        public async Task Can_Update_ToMany_Relationship_By_Patching_Resource_With_Overlap()
        {
            // Arrange
            var todoItem1 = _todoItemFaker.Generate();
            var todoItem2 = _todoItemFaker.Generate();

            var todoCollection = new TodoItemCollection
            {
                Owner = _personFaker.Generate(),
                TodoItems = new HashSet<TodoItem>
                {
                    todoItem1,
                    todoItem2
                }
            };

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.TodoItemCollections.Add(todoCollection);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    type = "todoCollections",
                    id = todoCollection.StringId,
                    relationships = new Dictionary<string, object>
                    {
                        ["todoItems"] = new
                        {
                            data = new[]
                            {
                                new
                                {
                                    type = "todoItems",
                                    id = todoItem1.StringId
                                },
                                new
                                {
                                    type = "todoItems",
                                    id = todoItem2.StringId
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/todoCollections/" + todoCollection.StringId;

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePatchAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var todoCollectionInDatabase = await dbContext.TodoItemCollections
                    .Include(collection => collection.TodoItems)
                    .FirstAsync(collection => collection.Id == todoCollection.Id);

                todoCollectionInDatabase.TodoItems.Should().HaveCount(2);
            });
        }

        [Fact]
        public async Task Can_Delete_ToOne_Relationship_By_Patching_Resource()
        {
            // Arrange
            var todoItem = _todoItemFaker.Generate();
            todoItem.Owner = _personFaker.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.TodoItems.Add(todoItem);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    id = todoItem.StringId,
                    type = "todoItems",
                    relationships = new
                    {
                        owner = new
                        {
                            data = (object) null
                        }
                    }
                }
            };

            var route = "/api/v1/todoItems/" + todoItem.StringId;

            // Act
            var (httpResponse, _) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var todoItemInDatabase = await dbContext.TodoItems
                    .Include(item => item.Owner)
                    .FirstAsync(item => item.Id == todoItem.Id);

                todoItemInDatabase.Owner.Should().BeNull();
            });
        }

        [Fact]
        public async Task Can_Set_ToOne_Relationship_By_Patching_Resource()
        {
            // Arrange
            var todoItem = _todoItemFaker.Generate();
            var person = _personFaker.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.AddRange(todoItem, person);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    id = todoItem.StringId,
                    type = "todoItems",
                    relationships = new
                    {
                        owner = new
                        {
                            data = new
                            {
                                type = "people",
                                id = person.StringId
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/todoItems/" + todoItem.StringId;

            // Act
            var (httpResponse, _) = await _testContext.ExecutePatchAsync<Document>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var todoItemInDatabase = await dbContext.TodoItems
                    .Include(item => item.Owner)
                    .FirstAsync(item => item.Id == todoItem.Id);

                todoItemInDatabase.Owner.Should().NotBeNull();
                todoItemInDatabase.Owner.Id.Should().Be(person.Id);
            });
        }

        [Fact]
        public async Task Can_Delete_ToMany_Relationship_By_Patching_Resource()
        {
            // Arrange
            var person = _personFaker.Generate();
            person.TodoItems = new HashSet<TodoItem>
            {
                _todoItemFaker.Generate()
            };

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.People.Add(person);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    id = person.StringId,
                    type = "people",
                    relationships = new Dictionary<string, object>
                    {
                        ["todoItems"] = new
                        {
                            data = new object[0]
                        }
                    }
                }
            };

            var route = "/api/v1/people/" + person.StringId;

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePatchAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var personInDatabase = await dbContext.People
                    .Include(p => p.TodoItems)
                    .FirstAsync(p => p.Id == person.Id);

                personInDatabase.TodoItems.Should().BeEmpty();
            });
        }

        [Fact]
        public async Task Updating_OneToOne_Relationship_With_Implicit_Remove()
        {
            // Arrange
            var person1 = _personFaker.Generate();
            var person2 = _personFaker.Generate();

            Passport passport = null;
            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                await dbContext.ClearTableAsync<Person>();

                passport = new Passport(dbContext);
                person1.Passport = passport;

                dbContext.People.AddRange(person1, person2);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    type = "people",
                    id = person2.StringId,
                    relationships = new Dictionary<string, object>
                    {
                        ["passport"] = new
                        {
                            data = new
                            {
                                type = "passports",
                                id = passport.StringId
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/people/" + person2.StringId;

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePatchAsync<string>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NoContent);

            responseDocument.Should().BeEmpty();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var personsInDatabase = await dbContext.People
                    .Include(person => person.Passport)
                    .ToListAsync();

                personsInDatabase.Single(person => person.Id == person1.Id).Passport.Should().BeNull();
                personsInDatabase.Single(person => person.Id == person2.Id).Passport.Id.Should().Be(passport.Id);
            });
        }

        [Fact]
        public async Task Fails_When_Patching_On_Primary_Endpoint_With_Missing_Secondary_Resources()
        {
            // Arrange
            var todoItem = _todoItemFaker.Generate();
            var person = _personFaker.Generate();

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.AddRange(todoItem, person);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                data = new
                {
                    type = "todoItems",
                    id = todoItem.StringId,
                    relationships = new Dictionary<string, object>
                    {
                        ["stakeHolders"] = new
                        {
                            data = new[]
                            {
                                new
                                {
                                    type = "people",
                                    id = person.StringId
                                },
                                new
                                {
                                    type = "people",
                                    id = "900000"
                                },
                                new
                                {
                                    type = "people",
                                    id = "900001"
                                }
                            }
                        },
                        ["parentTodo"] = new
                        {
                            data = new
                            {
                                type = "todoItems",
                                id = "900002"
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/todoItems/" + todoItem.StringId;

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePatchAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

            responseDocument.Errors.Should().HaveCount(3);

            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[0].Title.Should().Be("A resource being assigned to a relationship does not exist.");
            responseDocument.Errors[0].Detail.Should().Be("Resource of type 'people' with ID '900000' being assigned to relationship 'stakeHolders' does not exist.");

            responseDocument.Errors[1].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[1].Title.Should().Be("A resource being assigned to a relationship does not exist.");
            responseDocument.Errors[1].Detail.Should().Be("Resource of type 'people' with ID '900001' being assigned to relationship 'stakeHolders' does not exist.");

            responseDocument.Errors[2].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[2].Title.Should().Be("A resource being assigned to a relationship does not exist.");
            responseDocument.Errors[2].Detail.Should().Be("Resource of type 'todoItems' with ID '900002' being assigned to relationship 'parentTodo' does not exist.");
        }
    }
}
