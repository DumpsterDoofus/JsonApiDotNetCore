using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Queries.Expressions;
using JsonApiDotNetCore.Queries.Internal.QueryableBuilding;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Resources.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace JsonApiDotNetCore.Repositories
{
    /// <summary>
    /// Implements the foundational repository implementation that uses Entity Framework Core.
    /// </summary>
    public class EntityFrameworkCoreRepository<TResource> : EntityFrameworkCoreRepository<TResource, int>, IResourceRepository<TResource>
        where TResource : class, IIdentifiable<int>
    {
        public EntityFrameworkCoreRepository(
            ITargetedFields targetedFields, 
            IDbContextResolver contextResolver, 
            IResourceGraph resourceGraph,
            IResourceFactory resourceFactory,
            IEnumerable<IQueryConstraintProvider> constraintProviders,
            ILoggerFactory loggerFactory)
            : base(targetedFields, contextResolver, resourceGraph, resourceFactory, constraintProviders, loggerFactory) 
        { }
    }

    /// <summary>
    /// Implements the foundational Repository layer in the JsonApiDotNetCore architecture that uses Entity Framework Core.
    /// </summary>
    public class EntityFrameworkCoreRepository<TResource, TId> : IResourceRepository<TResource, TId>
        where TResource : class, IIdentifiable<TId>
    {
        private readonly ITargetedFields _targetedFields;
        private readonly DbContext _dbContext;
        private readonly IResourceGraph _resourceGraph;
        private readonly IResourceFactory _resourceFactory;
        private readonly IEnumerable<IQueryConstraintProvider> _constraintProviders;
        private readonly TraceLogWriter<EntityFrameworkCoreRepository<TResource, TId>> _traceWriter;

        public EntityFrameworkCoreRepository(
            ITargetedFields targetedFields,
            IDbContextResolver contextResolver,
            IResourceGraph resourceGraph,
            IResourceFactory resourceFactory,
            IEnumerable<IQueryConstraintProvider> constraintProviders,
            ILoggerFactory loggerFactory)
        {
            if (contextResolver == null) throw new ArgumentNullException(nameof(contextResolver));
            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));

            _targetedFields = targetedFields ?? throw new ArgumentNullException(nameof(targetedFields));
            _resourceGraph = resourceGraph ?? throw new ArgumentNullException(nameof(resourceGraph));
            _resourceFactory = resourceFactory ?? throw new ArgumentNullException(nameof(resourceFactory));
            _constraintProviders = constraintProviders ?? throw new ArgumentNullException(nameof(constraintProviders));
            _dbContext = contextResolver.GetContext();
            _traceWriter = new TraceLogWriter<EntityFrameworkCoreRepository<TResource, TId>>(loggerFactory);
        }

        /// <inheritdoc />
        public virtual async Task<IReadOnlyCollection<TResource>> GetAsync(QueryLayer layer)
        {
            _traceWriter.LogMethodStart(new {layer});
            if (layer == null) throw new ArgumentNullException(nameof(layer));

            IQueryable<TResource> query = ApplyQueryLayer(layer);
            
            return await query.ToListAsync();
        }

        /// <inheritdoc />
        public virtual async Task<int> CountAsync(FilterExpression topFilter)
        {
            _traceWriter.LogMethodStart(new {topFilter});

            var resourceContext = _resourceGraph.GetResourceContext<TResource>();
            var layer = new QueryLayer(resourceContext)
            {
                Filter = topFilter
            };

            IQueryable<TResource> query = ApplyQueryLayer(layer);
            return await query.CountAsync();
        }

        protected virtual IQueryable<TResource> ApplyQueryLayer(QueryLayer layer)
        {
            _traceWriter.LogMethodStart(new {layer});
            if (layer == null) throw new ArgumentNullException(nameof(layer));

            IQueryable<TResource> source = GetAll();

            var queryableHandlers = _constraintProviders
                .SelectMany(p => p.GetConstraints())
                .Where(expressionInScope => expressionInScope.Scope == null)
                .Select(expressionInScope => expressionInScope.Expression)
                .OfType<QueryableHandlerExpression>()
                .ToArray();

            foreach (var queryableHandler in queryableHandlers)
            {
                source = queryableHandler.Apply(source);
            }

            var nameFactory = new LambdaParameterNameFactory();
            var builder = new QueryableBuilder(source.Expression, source.ElementType, typeof(Queryable), nameFactory, _resourceFactory, _resourceGraph, _dbContext.Model);

            var expression = builder.ApplyQuery(layer);
            return source.Provider.CreateQuery<TResource>(expression);
        }

        protected virtual IQueryable<TResource> GetAll()
        {
            return _dbContext.Set<TResource>();
        }

        /// <inheritdoc />
        public virtual async Task CreateAsync(TResource resource)
        {
            _traceWriter.LogMethodStart(new {resource});
            if (resource == null) throw new ArgumentNullException(nameof(resource));

            foreach (var relationship in _targetedFields.Relationships)
            {
                var rightValue = relationship.GetValue(resource);
                await AssignValueToRelationship(relationship, resource, rightValue);
            }

            _dbContext.Set<TResource>().Add(resource);

            await SaveChangesAsync();

            FlushFromCache(resource);

            // This ensures relationships get reloaded from the database if they have
            // been requested. See https://github.com/json-api-dotnet/JsonApiDotNetCore/issues/343.
            DetachRelationships(resource);
        }

        public async Task AddToToManyRelationshipAsync(TId id, IReadOnlyCollection<IIdentifiable> secondaryResourceIds)
        {
            _traceWriter.LogMethodStart(new {id, secondaryResourceIds});
            if (secondaryResourceIds == null) throw new ArgumentNullException(nameof(secondaryResourceIds));
            
            var relationship = _targetedFields.Relationships.Single();
            var primaryResource = (TResource)_dbContext.GetTrackedOrAttach(CreateInstanceWithAssignedId(id));

            await AssignValueToRelationship(relationship, primaryResource, secondaryResourceIds);

            await SaveChangesAsync();
        }

        public async Task SetRelationshipAsync(TId id, object secondaryResourceIds)
        {
            _traceWriter.LogMethodStart(new {id, secondaryResourceIds});

            var relationship = _targetedFields.Relationships.Single();

            TResource primaryResource;
            if (relationship is HasOneAttribute hasOneRelationship && HasForeignKeyAtSideOfHasOneRelationship(hasOneRelationship))
            {
                primaryResource = await _dbContext.Set<TResource>()
                    .Include(relationship.Property.Name)
                    .Where(r => r.Id.Equals(id))
                    .FirstAsync();
            }
            else
            {
                primaryResource = (TResource) _dbContext.GetTrackedOrAttach(CreateInstanceWithAssignedId(id));
                await LoadRelationship(primaryResource, relationship);
            }
            
            await AssignValueToRelationship(relationship, primaryResource, secondaryResourceIds);

            await SaveChangesAsync();
        }

        /// <inheritdoc />
        public virtual async Task UpdateAsync(TResource resourceFromRequest, TResource resourceFromDatabase)
        {
            _traceWriter.LogMethodStart(new {resourceFromRequest, resourceFromDatabase});
            if (resourceFromRequest == null) throw new ArgumentNullException(nameof(resourceFromRequest));
            if (resourceFromDatabase == null) throw new ArgumentNullException(nameof(resourceFromDatabase));

            foreach (var attribute in _targetedFields.Attributes)
            {
                attribute.SetValue(resourceFromDatabase, attribute.GetValue(resourceFromRequest));
            }

            foreach (var relationship in _targetedFields.Relationships)
            {
                if (relationship is HasOneAttribute hasOneRelationship &&
                    HasForeignKeyAtSideOfHasOneRelationship(hasOneRelationship))
                {
                    FlushFromCache(resourceFromDatabase);

                    resourceFromDatabase = await _dbContext.Set<TResource>()
                        .Include(relationship.Property.Name)
                        .Where(r => r.Id.Equals(resourceFromRequest.Id))
                        .FirstAsync();
                }
                else
                {
                    // A database entity might not be tracked if it was retrieved through projection. 
                    resourceFromDatabase = (TResource) _dbContext.GetTrackedOrAttach(resourceFromDatabase);

                    // Ensures complete replacements of relationships.
                    await LoadRelationship(resourceFromDatabase, relationship);
                }

                var relationshipAssignment = relationship.GetValue(resourceFromRequest);
                await AssignValueToRelationship(relationship, resourceFromDatabase, relationshipAssignment);

                //_dbContext.Entry(resourceFromDatabase).State = EntityState.Modified;
            }

            await SaveChangesAsync();
        }

        /// <inheritdoc />
        public virtual async Task DeleteAsync(TId id)
        {
            _traceWriter.LogMethodStart(new {id});

            var resource = _dbContext.GetTrackedOrAttach(CreateInstanceWithAssignedId(id));
            _dbContext.Remove(resource);

            await SaveChangesAsync();
        }

        public async Task RemoveFromToManyRelationshipAsync(TId id, IReadOnlyCollection<IIdentifiable> secondaryResourceIds)
        {
            _traceWriter.LogMethodStart(new {id, secondaryResourceIds});
            if (secondaryResourceIds == null) throw new ArgumentNullException(nameof(secondaryResourceIds));
            
            var relationship = _targetedFields.Relationships.Single();
            var primaryResource = (TResource)_dbContext.GetTrackedOrAttach(CreateInstanceWithAssignedId(id));
            
            await LoadRelationship(primaryResource, relationship);

            var currentRelationshipAssignment = (IReadOnlyCollection<IIdentifiable>)relationship.GetValue(primaryResource);
            var newRelationshipAssignment = currentRelationshipAssignment.Where(i => secondaryResourceIds.All(r => r.StringId != i.StringId)).ToArray();
            
            if (newRelationshipAssignment.Length < currentRelationshipAssignment.Count)
            {
                await AssignValueToRelationship(relationship, primaryResource, newRelationshipAssignment);
                await SaveChangesAsync();
            }
        }

        private TResource CreateInstanceWithAssignedId(TId id)
        {
            var resource = _resourceFactory.CreateInstance<TResource>();
            resource.Id = id;

            return resource;
        }

        /// <inheritdoc />
        public virtual void FlushFromCache(TResource resource)
        {
            _traceWriter.LogMethodStart(new {resource});

            var trackedResource = _dbContext.GetTrackedIdentifiable(resource);
            _dbContext.Entry(trackedResource).State = EntityState.Detached;
        }

        private void DetachRelationships(TResource resource)
        {
            foreach (var relationship in _targetedFields.Relationships)
            {
                var rightValue = relationship.GetValue(resource);

                if (rightValue is IEnumerable<IIdentifiable> rightResources)
                {
                    foreach (var rightResource in rightResources)
                    {
                        _dbContext.Entry(rightResource).State = EntityState.Detached;
                    }

                    // Detaching to-many relationships is not sufficient to 
                    // trigger a full reload of relationships: the navigation 
                    // property actually needs to be nulled out, otherwise
                    // EF Core will still add duplicate instances to the collection.
                    relationship.SetValue(resource, null, _resourceFactory);
                }
                else if (rightValue != null)
                {
                    _dbContext.Entry(rightValue).State = EntityState.Detached;
                }
            }
        }

        /// <summary>
        /// Before assigning new relationship values (UpdateAsync), we need to
        /// attach the current database values of the relationship to the dbContext, else 
        /// it will not perform a complete-replace which is required for 
        /// one-to-many and many-to-many.
        /// <para />
        /// For example: a person `p1` has 2 todo-items: `t1` and `t2`.
        /// If we want to update this todo-item set to `t3` and `t4`, simply assigning
        /// `p1.todoItems = [t3, t4]` will result in EF Core adding them to the set,
        /// resulting in `[t1 ... t4]`. Instead, we should first include `[t1, t2]`,
        /// after which the reassignment  `p1.todoItems = [t3, t4]` will actually 
        /// make EF Core perform a complete replace. This method does the loading of `[t1, t2]`.
        /// </summary>
        protected async Task LoadRelationship(TResource resource, RelationshipAttribute relationship)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (relationship == null) throw new ArgumentNullException(nameof(relationship));
            
            var entityEntry = _dbContext.Entry(resource);
            NavigationEntry navigationEntry = null;
    
            if (relationship is HasManyThroughAttribute hasManyThroughRelationship)
            {
                navigationEntry = entityEntry.Collection(hasManyThroughRelationship.ThroughProperty.Name);
            }
            else if (relationship is HasManyAttribute hasManyRelationship)
            {
                navigationEntry = entityEntry.Collection(hasManyRelationship.Property.Name);
            }
            else if (relationship is HasOneAttribute hasOneRelationship)
            {
                navigationEntry = entityEntry.Reference(hasOneRelationship.Property.Name);

                /*var foreignKey = GetForeignKeyAtSideOfHasOneRelationship(hasOneRelationship);
                if (foreignKey == null || foreignKey.Properties.Count != 1)
                {   
                    // If the primary resource is the dependent side of a to-one relationship, there can be no FK
                    // violations resulting from the implicit removal.
                    navigationEntry = entityEntry.Reference(hasOneRelationship.Property.Name);
                }*/
            }

            if (navigationEntry != null)
            {
                await navigationEntry.LoadAsync();
            }
        }
        
        /// <summary>
        /// Loads the inverse relationships to prevent foreign key constraints from being violated
        /// to support implicit removes, see https://github.com/json-api-dotnet/JsonApiDotNetCore/issues/502.
        /// <remarks>
        /// Consider the following example: 
        /// person.todoItems = [t1, t2] is updated to [t3, t4]. If t3 and/or t4 was
        /// already related to another person, and these persons are NOT loaded into the 
        /// DbContext, then the query may fail with a foreign key constraint violation. Loading
        /// these "inverse relationships" into the DbContext ensures EF Core to take this into account.
        /// </remarks>
        /// </summary>
        private async Task LoadInverseRelationshipsInChangeTracker(RelationshipAttribute relationship, object resource)
        {
            if (relationship.InverseNavigationProperty != null)
            {
                if (relationship is HasOneAttribute hasOneRelationship)
                {
                    var entityEntry = _dbContext.Entry(resource);

                    var isOneToOne = IsOneToOne(hasOneRelationship);

                    if (isOneToOne == true)
                    {
                        await entityEntry.Reference(relationship.InverseNavigationProperty.Name).LoadAsync();
                    }
                    else if (isOneToOne == false)
                    {
                        await entityEntry.Collection(relationship.InverseNavigationProperty.Name).LoadAsync();
                    }
                    else
                    {
                        // TODO: What should happen if no inverse navigation exists?
                    }
                }
                else if (relationship is HasManyThroughAttribute)
                {
                    // Do nothing. Implicit removal is not possible for many-to-many relationships.
                }
                else
                {
                    var resources = (IEnumerable<IIdentifiable>)resource;

                    foreach (var nextResource in resources)
                    {
                        var nextEntityEntry = _dbContext.Entry(nextResource);
                        await nextEntityEntry.Reference(relationship.InverseNavigationProperty.Name).LoadAsync();
                    }
                }
            }
        }

        private bool? IsOneToOne(HasOneAttribute hasOneRelationship)
        {
            if (hasOneRelationship.InverseNavigationProperty != null)
            {
                var inversePropertyIsCollection = TypeHelper.TryGetCollectionElementType(hasOneRelationship.InverseNavigationProperty.PropertyType) != null;
                return !inversePropertyIsCollection;
            }

            return null;
        }

        private async Task AssignValueToRelationship(RelationshipAttribute relationship, TResource leftResource,
            object valueToAssign)
        {
            // Ensures the new relationship assignment will not result in entities being tracked more than once.
            object trackedValueToAssign = null;

            if (valueToAssign != null)
            {
                trackedValueToAssign = EnsureRelationshipValueToAssignIsTracked(valueToAssign, relationship.Property.PropertyType);
                
                // Ensures successful handling of implicit removals of relationships.
                await LoadInverseRelationshipsInChangeTracker(relationship, trackedValueToAssign);
            }

            if (relationship is HasOneAttribute hasOneRelationship)
            {
                var rightResourceId = trackedValueToAssign is IIdentifiable rightResource
                    ? rightResource.GetTypedId()
                    : null;

                // https://docs.microsoft.com/en-us/ef/core/saving/related-data
                /*
                var foreignKey = GetForeignKeyAtSideOfHasOneRelationship(hasOneRelationship);
                if (foreignKey != null)
                {
                    foreach (var foreignKeyProperty in foreignKey.Properties)
                    {
                        if (foreignKeyProperty.IsShadowProperty())
                        {
                            _dbContext.Entry(leftResource).Property(foreignKeyProperty.Name).CurrentValue = rightResourceId;
                        }
                        else
                        {
                            foreignKeyProperty.PropertyInfo.SetValue(leftResource, rightResourceId);
                            _dbContext.Entry(leftResource).State = EntityState.Modified;
                        }
                    }
                }*/
            }

            relationship.SetValue(leftResource, trackedValueToAssign, _resourceFactory);
        }

        private object EnsureRelationshipValueToAssignIsTracked(object valueToAssign, Type relationshipPropertyType)
        {
            if (valueToAssign is IReadOnlyCollection<IIdentifiable> rightResourcesInToManyRelationship)
            {
                return EnsureToManyRelationshipValueToAssignIsTracked(rightResourcesInToManyRelationship, relationshipPropertyType);
            }

            if (valueToAssign is IIdentifiable rightResourceInToOneRelationship)
            {
                return _dbContext.GetTrackedOrAttach(rightResourceInToOneRelationship);
            }

            return null;
        }

        private object EnsureToManyRelationshipValueToAssignIsTracked(IReadOnlyCollection<IIdentifiable> rightResources, Type rightCollectionType)
        {
            var rightResourcesTracked = new object[rightResources.Count];

            int index = 0;
            foreach (var rightResource in rightResources)
            {
                var trackedIdentifiable = _dbContext.GetTrackedOrAttach(rightResource);

                // We should recalculate the target type for every iteration because types may vary. This is possible with resource inheritance.
                var identifiableRuntimeType = trackedIdentifiable.GetType();
                rightResourcesTracked[index] = Convert.ChangeType(trackedIdentifiable, identifiableRuntimeType);

                index++;
            }

            return TypeHelper.CopyToTypedCollection(rightResourcesTracked, rightCollectionType);
        }

        private bool HasForeignKeyAtSideOfHasOneRelationship(HasOneAttribute relationship)
        {
            var entityType = _dbContext.Model.FindEntityType(typeof(TResource));
            var navigation = entityType.FindNavigation(relationship.Property.Name);

            return navigation.ForeignKey.DeclaringEntityType.ClrType == typeof(TResource);
        }

        private IForeignKey GetForeignKeyAtSideOfHasOneRelationship(HasOneAttribute relationship)
        {
            var entityType = _dbContext.Model.FindEntityType(typeof(TResource));
            var navigation = entityType.FindNavigation(relationship.Property.Name);

            var isForeignKeyAtRelationshipSide = navigation.ForeignKey.DeclaringEntityType.ClrType == typeof(TResource);
            return isForeignKeyAtRelationshipSide ? navigation.ForeignKey : null;
        }

        private async Task SaveChangesAsync()
        {
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                throw new DataStoreUpdateException(exception);
            }
        }
    }
}
