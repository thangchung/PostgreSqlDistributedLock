﻿using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PostgreSQLDistributedLock
{
    public sealed class DistributedLock : IDisposable
    {
        private readonly ILogger<DistributedLock> _logger;
        private bool _disposed;
        private NpgsqlConnection _connection;

        public DistributedLock(string connectionString, ILogger<DistributedLock> logger)
        {
            _logger = logger;
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            _connection = new NpgsqlConnection(builder.ToString());
            _connection.Open();
        }

        public async Task<bool> TryExecuteInDistributedLock(long lockId, Func<Task> exclusiveLockTask)
        {
            var hasLockedAcquired = await TryAcquireLockAsync(lockId);

            if (!hasLockedAcquired)
            {
                return false;
            }

            try
            {
                await exclusiveLockTask();
            }
            finally
            {
                await ReleaseLock(lockId);
            }

            return true;
        }

        private async Task<bool> TryAcquireLockAsync(long lockId)
        {
            var sessionLockCommand = $"SELECT pg_try_advisory_lock({lockId})";
            _logger.LogInformation("Trying to acquire session lock for Lock Id {@LockId}", lockId);
            var commandQuery = new NpgsqlCommand(sessionLockCommand, _connection);
            var result = await commandQuery.ExecuteScalarAsync();
            if (result != null && bool.TryParse(result.ToString(), out var lockAcquired) && lockAcquired)
            {
                _logger.LogInformation("Lock {@LockId} acquired", lockId);
                return true;
            }

            _logger.LogInformation("Lock {@LockId} rejected", lockId);
            return false;
        }

        private async Task ReleaseLock(long lockId)
        {
            var transactionLockCommand = $"SELECT pg_advisory_unlock({lockId})";
            _logger.LogInformation("Releasing session lock for {@LockId}", lockId);
            var commandQuery = new NpgsqlCommand(transactionLockCommand, _connection);
            await commandQuery.ExecuteScalarAsync();
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _logger.LogInformation("Closing database connection");
                _connection?.Close();
                _connection?.Dispose();
                _connection = null;
            }

            _disposed = true;
        }
    }
}