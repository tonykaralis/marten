using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Transforms;
using Npgsql;

namespace Marten.Storage
{
    public class ConjoinedTenancy : Tenancy, ITenancy
    {
        public ConjoinedTenancy(IConnectionFactory factory, StoreOptions options) : base(options)
        {
            Default = new Tenant(options.Storage, options, factory, Tenancy.DefaultTenantId);
            Cleaner = new DocumentCleaner(options, Default);
            Schema = new TenantSchema(options, TypeExtensions.As<Tenant>(Default));
        }

        public ITenant this[string tenantId] => new LightweightTenant(tenantId, Default);

        public ITenant Default { get; }

        public void Initialize()
        {
            seedSchemas(Default);
        }

        public IDocumentCleaner Cleaner { get; }
        public IDocumentSchema Schema { get; }
        public TenancyStyle Style { get; } = TenancyStyle.Conjoined;
    }

    public class LightweightTenant : ITenant
    {
        private readonly ITenant _inner;

        public LightweightTenant(string tenantId, ITenant inner)
        {
            _inner = inner;
            TenantId = tenantId;
        }

        public string TenantId { get; }

        public IDocumentStorage StorageFor(Type documentType)
        {
            return _inner.StorageFor(documentType);
        }

        public IDocumentMapping MappingFor(Type documentType)
        {
            return _inner.MappingFor(documentType);
        }

        public void EnsureStorageExists(Type documentType)
        {
            _inner.EnsureStorageExists(documentType);
        }

        public ISequences Sequences => _inner.Sequences;
        public IDocumentStorage<T> StorageFor<T>()
        {
            return _inner.StorageFor<T>();
        }

        public IdAssignment<T> IdAssignmentFor<T>()
        {
            return _inner.IdAssignmentFor<T>();
        }

        public TransformFunction TransformFor(string name)
        {
            return _inner.TransformFor(name);
        }

        public void ResetSchemaExistenceChecks()
        {
            _inner.ResetSchemaExistenceChecks();
        }

        public IBulkLoader<T> BulkLoaderFor<T>()
        {
            return _inner.BulkLoaderFor<T>();
        }

        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, int timeout = 30)
        {
            return _inner.OpenConnection(mode, isolationLevel, timeout);
        }

        public void ResetHiloSequenceFloor<T>(long floor)
        {
            _inner.ResetHiloSequenceFloor<T>(floor);
        }

        public DocumentMetadata MetadataFor<T>(T entity)
        {
            // THIS MAY NEED TO BE DONE DIFFERENTLY
            throw new NotImplementedException();
        }

        public Task<DocumentMetadata> MetadataForAsync<T>(T entity, CancellationToken token = new CancellationToken())
        {
            throw new NotImplementedException();
        }

        public NpgsqlConnection CreateConnection()
        {
            return _inner.CreateConnection();
        }
    }
}