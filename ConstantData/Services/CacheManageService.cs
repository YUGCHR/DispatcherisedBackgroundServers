﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Library.Models;
using CachingFramework.Redis.Contracts.Providers;

namespace ConstantData.Services
{
    public interface ICacheManageService
    {
        public Task SetStartConstants(KeyType startConstantKey, string startConstantField, ConstantsSet constantsSet);
        public Task SetConstantsStartGuidKey(KeyType startConstantKey, string startConstantField, string constantsStartGuidKey);
        public Task<TV> FetchUpdatedConstant<TK, TV>(string key, TK field);
        public Task<IDictionary<TK, TV>> FetchUpdatedConstants<TK, TV>(string key);
        public Task<bool> DeleteKeyIfCancelled(string startConstantKey);
    }

    public class CacheManageService : ICacheManageService
    {
        private readonly ICacheProviderAsync _cache;

        public CacheManageService(ICacheProviderAsync cache)
        {
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<CacheManageService>();

        public async Task SetStartConstants(KeyType keyTime, string field, ConstantsSet constantsSet)
        {
            if (field == constantsSet.ConstantsVersionBaseField.Value)
            {
                // обновлять версию констант при записи в ключ гуид
                constantsSet.ConstantsVersionNumber.Value++;
                Logs.Here().Information("ConstantsVersionNumber was incremented and become {0}.", constantsSet.ConstantsVersionNumber.Value);
            }

            await _cache.SetHashedAsync<ConstantsSet>(keyTime.Value, field, constantsSet, SetLifeTimeFromKey(keyTime));
            Logs.Here().Information("SetStartConstants set constants (EventKeyFrom for example = {0}) in key {1}.", constantsSet.EventKeyFrom.Value, keyTime.Value);
        }

        public async Task SetConstantsStartGuidKey(KeyType keyTime, string field, string constantsStartGuidKey)
        {
            await _cache.SetHashedAsync<string>(keyTime.Value, field, constantsStartGuidKey, SetLifeTimeFromKey(keyTime));
            string test = await _cache.GetHashedAsync<string>(keyTime.Value, field);
            Logs.Here().Information("SetStartConstants set {@G} \n {0} --- {1}.", new { GuidKey = keyTime.Value }, constantsStartGuidKey, test);
            // можно читать и сравнивать - и возвращать true
        }

        private TimeSpan SetLifeTimeFromKey(KeyType time)
        {
            return TimeSpan.FromDays(time.LifeTime);
        }

        public async Task<TV> FetchUpdatedConstant<TK, TV>(string key, TK field)
        {
            return await _cache.GetHashedAsync<TK, TV>(key, field);
        }

        public async Task<IDictionary<TK, TV>> FetchUpdatedConstants<TK, TV>(string key)
        {
            // Gets all the values from a hash, assuming all the values in the hash are of the same type <typeparamref name="TV" />.
            // The keys of the dictionary are the field names of type <typeparamref name="TK" /> and the values are the objects of type <typeparamref name="TV" />.
            // <typeparam name="TK">The field type</typeparam>
            // <typeparam name="TV">The value type</typeparam>

            IDictionary<TK, TV> updatedConstants = await _cache.GetHashedAllAsync<TK, TV>(key);
            bool result = await _cache.RemoveAsync(key);
            if (result)
            {
                return updatedConstants;
            }
            Logs.Here().Error("{@K} removing was failed.", new { Key = key });
            return null;
        }

        public async Task<bool> DeleteKeyIfCancelled(string startConstantKey)
        {
            return await _cache.RemoveAsync(startConstantKey);
        }
    }
}
