﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Shared.Library.Models;

namespace ConstantData.Services
{
    public interface IOnKeysEventsSubscribeService
    {
        public void SubscribeOnEventUpdate(ConstantsSet constantsSet, string constantsStartGuidField, CancellationToken stoppingToken);
    }

    public class OnKeysEventsSubscribeService : IOnKeysEventsSubscribeService
    {
        private readonly ICacheManageService _cache;
        private readonly IKeyEventsProvider _keyEvents;

        public OnKeysEventsSubscribeService(
            IKeyEventsProvider keyEvents, ICacheManageService cache)
        {
            _keyEvents = keyEvents;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<OnKeysEventsSubscribeService>();

        private bool _flagToBlockEventUpdate;

        // подписываемся на ключ сообщения о появлении обновления констант
        public void SubscribeOnEventUpdate(ConstantsSet constantsSet, string constantsStartGuidField, CancellationToken stoppingToken)
        {
            string eventKeyUpdateConstants = constantsSet.EventKeyUpdateConstants.Value;

            Logs.Here().Information("ConstantsData subscribed on EventKey. \n {@E}", new { EventKey = eventKeyUpdateConstants });
            Logs.Here().Information("Constants version is {0}:{1}.", constantsSet.ConstantsVersionBase.Value, constantsSet.ConstantsVersionNumber.Value);

            _flagToBlockEventUpdate = true;

            _keyEvents.Subscribe(eventKeyUpdateConstants, async (string key, KeyEvent cmd) =>
            {
                if (cmd == constantsSet.EventCmd && _flagToBlockEventUpdate)
                {
                    _flagToBlockEventUpdate = false;
                    _ = CheckKeyUpdateConstants(constantsSet, constantsStartGuidField, stoppingToken);
                }
            });
        }

        private async Task CheckKeyUpdateConstants(ConstantsSet constantsSet, string constantsStartGuidField, CancellationToken stoppingToken) // Main of EventKeyFrontGivesTask key
        {
            // проверять, что константы может обновлять только админ

            string eventKeyUpdateConstants = constantsSet.EventKeyUpdateConstants.Value;
            Logs.Here().Debug("CheckKeyUpdateConstants started with key {0}.", eventKeyUpdateConstants);

            IDictionary<string, int> updatedConstants = await _cache.FetchUpdatedConstants<string, int>(eventKeyUpdateConstants); ;
            int updatedConstantsCount = updatedConstants.Count;
            Logs.Here().Debug("Fetched updated constants count = {0}.", updatedConstantsCount);

            // выбирать все поля, присваивать по таблице, при присваивании поле удалять
            // все обновляемые константы должны быть одного типа или разные типы на разных ключах
            //foreach (KeyValuePair<string, int> updatedConstant in updatedConstants)
            //{
            //    var (key, value) = updatedConstant;
            //    constantsSet = UpdatedValueAssignsToProperty(constantsSet, key, value);// ?? constantsSet;
            //}
            bool setWasUpdated;
            (setWasUpdated, constantsSet) = UpdatedValueAssignsToProperty(constantsSet, updatedConstants);
            if (setWasUpdated)
            {
                // версия констант обновится внутри SetStartConstants
                await _cache.SetStartConstants(constantsSet.ConstantsVersionBase, constantsStartGuidField, constantsSet);
            }

            // задержка, определяющая максимальную частоту обновления констант
            double timeToWaitTheConstants = constantsSet.EventKeyUpdateConstants.LifeTime;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(timeToWaitTheConstants), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if the Delay is cancelled
            }
            // перед завершением обработчика разрешаем события подписки на обновления
            _flagToBlockEventUpdate = true;
        }
        
        public static (bool, ConstantsSet) UpdatedValueAssignsToProperty(ConstantsSet constantsSet, IDictionary<string, int> updatedConstants)//(ConstantsSet constantsSet, string key, int value)
        {
            string finalPropertyToSet = constantsSet.FinalPropertyToSet.Value;

            foreach (KeyValuePair<string, int> updatedConstant in updatedConstants)
            {
                var (key, value) = updatedConstant;
                //constantsSet = UpdatedValueAssignsToProperty(constantsSet, key, value); // ?? constantsSet;
                
                int existsConstant = FetchValueOfPropertyOfProperty(constantsSet, finalPropertyToSet, key);
                // можно проверять предыдущее значение и, если новое такое же, не обновлять
                // но тогда надо проверять весь пакет и только если все не изменились, то не переписывать ключ
                // может быть когда-нибудь потом
                if (existsConstant != value)
                {
                    // но запись в ключ всё равно произойдёт, как это устранить?
                    //return constantsSet;
                    
                    object constantType = constantsSet.GetType().GetProperty(key)?.GetValue(constantsSet);

                    if (constantType == null)
                    {
                        Logs.Here().Error("Wrong {@P} was used - update failed", new { PropertyName = key });
                        return (false, constantsSet);
                    }

                    constantType.GetType().GetProperty(finalPropertyToSet)?.SetValue(constantType, value);
                    int constantWasUpdated = FetchValueOfPropertyOfProperty(constantsSet, finalPropertyToSet, key);
                }
                else
                {
                    // можно добавить сообщение, что модифицировать константу не удалось
                    // ещё можно показать значения - бывшее и которое хотели обновить
                    Logs.Here().Error("Constant {@K} will be left unchanged", new { Key = key });
                }
                // удалять поле, с которого считано обновление

            }
            return (true, constantsSet);
        }

        private static int FetchValueOfPropertyOfProperty(ConstantsSet constantsSet, string finalPropertyToSet, string key)
        {
            int constantValue = Convert.ToInt32(FetchValueOfProperty(FetchValueOfProperty(constantsSet, key), finalPropertyToSet));
            Logs.Here().Information("The value of property {0} = {1}.", key, constantValue);
            return constantValue;
        }

        private static object FetchValueOfProperty(object rrr, string propertyName)
        {
            return rrr.GetType().GetProperty(propertyName)?.GetValue(rrr);
        }
    }
}
